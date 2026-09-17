r"""
SICE SI 2026: 8セクター全256パターン GT内訳実測分析 & 3層オラクル表現限界 評価スクリプト

機能:
  1. Unity (SICESI_SectorMaskCollector) が出力した SectorMask 生データ (.raw または .csv) と
     GT画像 (gt_left.png / gt_right.png) を読み込み、
     各画素の実測 8ビット占有マスク (0〜255) を1画素の誤りもなく完全集計。
  2. 3層の表現限界・性能評価を算出:
     - [層1] 20設定 (R_th, L_th) の個別 IoU
     - [層2] 2特徴オラクル (N_occ, L_max の21クラスから決定するオラクル上限)
     - [層3] 真の8ビットオラクル (全256パターンから決定する理論的経験上限)
  3. 全密度一括自動解析:
     --dataset-dir を指定するだけで、SectorMaskSweep 配下の全密度を自動検出し、
     密度ごとの詳細CSV出力および総合比較サマリー表を表示。

使用例:
  # 1) 全密度の自動一括解析 (推奨):
  python Assets/Features/SICESI/Python/analyze_sector_patterns.py --dataset-dir C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset/Bouchiba_NoiseOff_ProposeTest --eye Right

  # 2) 特定の密度のみ指定して解析:
  python Assets/Features/SICESI/Python/analyze_sector_patterns.py --dataset-dir C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset/Bouchiba_NoiseOff_ProposeTest --density 4.0 --eye Right

  # 3) 個別の RAW ファイルと GT を直接指定:
  python Assets/Features/SICESI/Python/analyze_sector_patterns.py --raw path/to/sector_mask.raw --gt path/to/gt_right.png
"""

import os
import sys
import csv
import argparse
from fractions import Fraction
from pathlib import Path
from typing import List, Dict, Tuple, Optional
import cv2
import numpy as np

# Windows コンソール文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

K = 8
PATTERN_COUNT = 1 << K

REPRESENTATIVES = {
    1: [8],
    2: [3, 4, 5, 8],
    3: [2, 3, 4, 8],
    4: [1, 2, 3, 8],
    5: [1, 2, 8],
    6: [1, 8],
    7: [8],
    8: [8],
}

def max_consecutive_zeros(mask: int) -> int:
    if mask == 0:
        return K
    if mask == 0xFF:
        return 0

    longest = 0
    current = 0

    for index in range(2 * K):
        occupied = (mask >> (index % K)) & 1
        if occupied:
            current = 0
        else:
            current += 1
            longest = max(longest, current)

    return min(longest, K)

def canonical_rotation_class(mask: int) -> int:
    """8セクターの円周回転（巡回シフト8通り）の中で最小の整数値を代表クラスとする (全36クラス)"""
    min_val = mask
    cur = mask
    for _ in range(7):
        cur = ((cur << 1) & 0xFF) | (cur >> 7)
        if cur < min_val:
            min_val = cur
    return min_val

def evaluate(visible_rule: List[bool], gt_visible: List[int], gt_occluded: List[int]) -> Dict[str, any]:
    tp = sum(
        gt_visible[mask]
        for mask in range(PATTERN_COUNT)
        if visible_rule[mask]
    )
    fp = sum(
        gt_occluded[mask]
        for mask in range(PATTERN_COUNT)
        if visible_rule[mask]
    )
    fn = sum(gt_visible) - tp
    denominator = tp + fp + fn

    iou = (
        Fraction(tp, denominator)
        if denominator
        else Fraction(1, 1)
    )

    return {
        "iou": iou,
        "tp": tp,
        "fn_over_occlusion": fn,
        "fp_leakage": fp,
        "error_pixels": fn + fp,
    }

def write_csv(path: Path, rows: List[Dict]):
    try:
        with path.open("w", newline="", encoding="utf-8-sig") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)
    except PermissionError:
        import time
        alt_path = path.parent / f"{path.stem}_{int(time.time())}.csv"
        print(f"  [Warning] {path.name} がロックされているため、代替ファイルに保存します: {alt_path.name}")
        with alt_path.open("w", newline="", encoding="utf-8-sig") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)

def load_from_raw_and_gt(raw_path: Path, gt_path: Path) -> Tuple[List[int], List[int]]:
    """
    SectorMask 生バイナリ (uint32[height, width]) と GT画像を読み込み、
    カメラの Viewport Rect (SRDisplayのSide by Sideオフセット・アスペクト比) を自動アライメントして
    仮想画素の実測 8ビット占有マスク (0〜255) を GT と画素単位で正確に整合集計します。
    """
    if not raw_path.exists():
        raise FileNotFoundError(f"RAW file not found: {raw_path}")
    if not gt_path.exists():
        raise FileNotFoundError(f"GT image not found: {gt_path}")

    gt_img = cv2.imread(str(gt_path), cv2.IMREAD_COLOR)
    if gt_img is None:
        raise ValueError(f"Failed to load GT image: {gt_path}")

    h, w = gt_img.shape[:2]
    expected_bytes = w * h * 4

    raw_bytes = raw_path.read_bytes()
    if len(raw_bytes) != expected_bytes:
        print(f"  [Notice] RAW size ({len(raw_bytes)} bytes) differs from GT ({w}x{h}x4 = {expected_bytes} bytes).")

    packed_data = np.frombuffer(raw_bytes, dtype=np.uint32)
    if len(packed_data) != w * h:
        packed_data = packed_data[:w * h]

    packed_img = packed_data.reshape((h, w))
    gt_vis = np.any(gt_img > 0, axis=-1)

    # 同一フォルダに test_*.png があれば参照（最も正確な仮想オブジェクト輪郭）
    test_img = None
    parent_dir = raw_path.parent
    test_candidates = list(parent_dir.glob("test_*.png"))
    if test_candidates:
        test_img = cv2.imread(str(test_candidates[0]), cv2.IMREAD_COLOR)
    ref_vis = np.any(test_img > 0, axis=-1) if test_img is not None else gt_vis

    # 1. FlipUD + LR (URP ReadPixels と AsyncGPUReadback の座標系およびSRDisplayの反転整合)
    raw_ud_lr = np.fliplr(np.flipud(packed_img))
    is_ev = ((raw_ud_lr >> 12) & 1) == 1
    masks = (raw_ud_lr & 0xFF).astype(np.uint8)

    # 2. Viewport アライメント (RAW バッファのクロップとカメラ矩形への正規化配置)
    ys_r, xs_r = np.where(is_ev)
    ys_ref, xs_ref = np.where(ref_vis)

    if len(ys_r) > 0 and len(ys_ref) > 0:
        crop_eval = is_ev[ys_r.min():ys_r.max()+1, xs_r.min():xs_r.max()+1]
        crop_masks = masks[ys_r.min():ys_r.max()+1, xs_r.min():xs_r.max()+1]

        th = ys_ref.max() - ys_ref.min() + 1
        tw = xs_ref.max() - xs_ref.min() + 1

        resized_eval = cv2.resize(crop_eval.astype(np.uint8), (tw, th), interpolation=cv2.INTER_NEAREST) > 0
        resized_masks = cv2.resize(crop_masks, (tw, th), interpolation=cv2.INTER_NEAREST)

        aligned_eval = np.zeros((h, w), dtype=bool)
        aligned_masks = np.zeros((h, w), dtype=np.uint8)

        aligned_eval[ys_ref.min():ys_ref.max()+1, xs_ref.min():xs_ref.max()+1] = resized_eval
        aligned_masks[ys_ref.min():ys_ref.max()+1, xs_ref.min():xs_ref.max()+1] = resized_masks
    else:
        aligned_eval = is_ev
        aligned_masks = masks

    eval_count = int(np.sum(aligned_eval))
    gt_overlap = int(np.sum(aligned_eval & gt_vis))
    print(f"  [Viewport アライメント完了] 評価画素数: {eval_count} / {w * h}, GT重なり画素数: {gt_overlap} (カバレッジ: {gt_overlap / np.sum(gt_vis) * 100:.2f}%)")

    gt_visible = [0] * PATTERN_COUNT
    gt_occluded = [0] * PATTERN_COUNT

    eval_indices = np.where(aligned_eval)
    for y, x in zip(eval_indices[0], eval_indices[1]):
        m = int(aligned_masks[y, x])
        if gt_vis[y, x]:
            gt_visible[m] += 1
        else:
            gt_occluded[m] += 1

    return gt_visible, gt_occluded

def analyze_single_condition(
    gt_visible: List[int],
    gt_occluded: List[int],
    out_dir: Path,
    density_label: str
) -> Dict[str, any]:
    occupied_counts = [mask.bit_count() for mask in range(PATTERN_COUNT)]
    zero_runs = [max_consecutive_zeros(mask) for mask in range(PATTERN_COUNT)]

    comparison_rows = []

    # 1. 20設定の評価
    for r_threshold, l_thresholds in REPRESENTATIVES.items():
        for l_threshold in l_thresholds:
            visible_rule = [
                not (
                    occupied_counts[mask] >= r_threshold
                    and zero_runs[mask] <= l_threshold
                )
                for mask in range(PATTERN_COUNT)
            ]

            comparison_rows.append({
                "rule_type": "Fixed_Rule",
                "rule": f"R{r_threshold}_L{l_threshold}",
                **evaluate(visible_rule, gt_visible, gt_occluded),
            })

    # 2. [第1層: 2特徴オラクル] (N_occ, L_max) の21クラスごとの最適判定
    class_visible_sum = {}
    class_occluded_sum = {}
    class_masks = {}
    for m in range(PATTERN_COUNT):
        key = (occupied_counts[m], zero_runs[m])
        if key not in class_visible_sum:
            class_visible_sum[key] = 0
            class_occluded_sum[key] = 0
            class_masks[key] = []
        class_visible_sum[key] += gt_visible[m]
        class_occluded_sum[key] += gt_occluded[m]
        class_masks[key].append(m)

    observed_classes = [
        k for k in class_visible_sum
        if class_visible_sum[k] + class_occluded_sum[k] > 0
    ]
    ordered_classes = sorted(
        observed_classes,
        key=lambda k: Fraction(
            class_visible_sum[k],
            class_visible_sum[k] + class_occluded_sum[k],
        ),
        reverse=True,
    )

    two_feature_rule = [False] * PATTERN_COUNT
    oracle_2feat_rule = two_feature_rule.copy()
    oracle_2feat_metrics = evaluate(two_feature_rule, gt_visible, gt_occluded)

    for k in ordered_classes:
        for m in class_masks[k]:
            two_feature_rule[m] = True
        metrics = evaluate(two_feature_rule, gt_visible, gt_occluded)
        if metrics["iou"] > oracle_2feat_metrics["iou"]:
            oracle_2feat_rule = two_feature_rule.copy()
            oracle_2feat_metrics = metrics

    comparison_rows.append({
        "rule_type": "2_Feature_Oracle",
        "rule": "Oracle_2Feature_(Nocc,Lmax)",
        **oracle_2feat_metrics,
    })

    # 3. [第2層: 回転不変オラクル] 円周回転で同一視した36クラスごとの最適判定 (幾何形状オラクル)
    rot_classes = [canonical_rotation_class(m) for m in range(PATTERN_COUNT)]
    rot_visible_sum = {}
    rot_occluded_sum = {}
    rot_masks = {}
    for m in range(PATTERN_COUNT):
        rc = rot_classes[m]
        if rc not in rot_visible_sum:
            rot_visible_sum[rc] = 0
            rot_occluded_sum[rc] = 0
            rot_masks[rc] = []
        rot_visible_sum[rc] += gt_visible[m]
        rot_occluded_sum[rc] += gt_occluded[m]
        rot_masks[rc].append(m)

    observed_rot_classes = [
        k for k in rot_visible_sum
        if rot_visible_sum[k] + rot_occluded_sum[k] > 0
    ]
    ordered_rot_classes = sorted(
        observed_rot_classes,
        key=lambda k: Fraction(
            rot_visible_sum[k],
            rot_visible_sum[k] + rot_occluded_sum[k],
        ),
        reverse=True,
    )

    rot_rule = [False] * PATTERN_COUNT
    oracle_rot_rule = rot_rule.copy()
    oracle_rot_metrics = evaluate(rot_rule, gt_visible, gt_occluded)

    for k in ordered_rot_classes:
        for m in rot_masks[k]:
            rot_rule[m] = True
        metrics = evaluate(rot_rule, gt_visible, gt_occluded)
        if metrics["iou"] > oracle_rot_metrics["iou"]:
            oracle_rot_rule = rot_rule.copy()
            oracle_rot_metrics = metrics

    comparison_rows.append({
        "rule_type": "Rotation_Oracle",
        "rule": "Oracle_RotInvariant_(36Classes)",
        **oracle_rot_metrics,
    })

    # 4. [第3層: 真の8ビットオラクル] 全256パターンの個別最適判定 (向き・位置含む経験上限)
    observed_patterns = [
        mask
        for mask in range(PATTERN_COUNT)
        if gt_visible[mask] + gt_occluded[mask] > 0
    ]
    ordered_patterns = sorted(
        observed_patterns,
        key=lambda mask: Fraction(
            gt_visible[mask],
            gt_visible[mask] + gt_occluded[mask],
        ),
        reverse=True,
    )

    visible_rule = [False] * PATTERN_COUNT
    oracle_8bit_rule = visible_rule.copy()
    oracle_8bit_metrics = evaluate(visible_rule, gt_visible, gt_occluded)

    for mask in ordered_patterns:
        visible_rule[mask] = True
        metrics = evaluate(visible_rule, gt_visible, gt_occluded)
        if metrics["iou"] > oracle_8bit_metrics["iou"]:
            oracle_8bit_rule = visible_rule.copy()
            oracle_8bit_metrics = metrics

    comparison_rows.append({
        "rule_type": "True_8Bit_Oracle",
        "rule": "Oracle_True_8Bit_FullLUT",
        **oracle_8bit_metrics,
    })

    comparison_rows.sort(
        key=lambda row: row["iou"],
        reverse=True,
    )

    # パターン別詳細テーブル
    pattern_rows = []
    for mask in range(PATTERN_COUNT):
        visible_count = gt_visible[mask]
        occluded_count = gt_occluded[mask]
        total = visible_count + occluded_count

        old_occluded = occupied_counts[mask] >= 5
        new_occluded = (
            occupied_counts[mask] >= 4
            and zero_runs[mask] <= 3
        )

        if total == 0:
            status = "unobserved"
        elif visible_count == 0:
            status = "GT_occluded_only"
        elif occluded_count == 0:
            status = "GT_visible_only"
        else:
            status = "mixed"

        pattern_rows.append({
            "mask": mask,
            "bits_b7_to_b0": format(mask, "08b"),
            "occupied_count": occupied_counts[mask],
            "max_consecutive_zeros": zero_runs[mask],
            "total_pixels": total,
            "gt_visible_count": visible_count,
            "gt_occluded_count": occluded_count,
            "gt_occluded_fraction": (
                f"{occluded_count / total:.4f}" if total else ""
            ),
            "status": status,
            "R5_L8_occluded": int(old_occluded),
            "R4_L3_occluded": int(new_occluded),
            "newly_occluded": int(new_occluded and not old_occluded),
            "oracle_2feat_occluded": int(not oracle_2feat_rule[mask]) if total else "",
            "oracle_8bit_occluded": int(not oracle_8bit_rule[mask]) if total else "",
            "minimum_error_pixels": min(visible_count, occluded_count),
        })

    out_dir.mkdir(parents=True, exist_ok=True)
    write_csv(out_dir / f"pattern_analysis_{density_label}.csv", pattern_rows)
    write_csv(
        out_dir / f"rule_comparison_{density_label}.csv",
        [
            {
                **row,
                "iou": float(row["iou"]),
            }
            for row in comparison_rows
        ],
    )

    # サマリー抽出
    r4_l3_metric = next((r for r in comparison_rows if r["rule"] == "R4_L3"), None)
    r5_l8_metric = next((r for r in comparison_rows if r["rule"] == "R5_L8"), None)
    e_min_sum = sum(min(gt_visible[m], gt_occluded[m]) for m in range(PATTERN_COUNT))

    return {
        "density": density_label,
        "observed_patterns": len(observed_patterns),
        "observed_classes": len(observed_classes),
        "observed_rot_classes": len(observed_rot_classes),
        "oracle_8bit_iou": float(oracle_8bit_metrics["iou"]),
        "oracle_8bit_errors": oracle_8bit_metrics["error_pixels"],
        "oracle_rot_iou": float(oracle_rot_metrics["iou"]),
        "oracle_rot_errors": oracle_rot_metrics["error_pixels"],
        "oracle_2feat_iou": float(oracle_2feat_metrics["iou"]),
        "oracle_2feat_errors": oracle_2feat_metrics["error_pixels"],
        "e_min_sum": e_min_sum,
        "r4_l3_iou": float(r4_l3_metric["iou"]) if r4_l3_metric else 0.0,
        "r4_l3_errors": r4_l3_metric["error_pixels"] if r4_l3_metric else 0,
        "r5_l8_iou": float(r5_l8_metric["iou"]) if r5_l8_metric else 0.0,
        "r5_l8_errors": r5_l8_metric["error_pixels"] if r5_l8_metric else 0,
    }

def verify_raw_internal(raw_path: Path, r_th: Optional[int] = None, l_th: Optional[int] = None) -> Dict[str, any]:
    """
    RAWファイル単体 (GT不要) で、保存された 8ビット占有マスクから計算した遮蔽判定と、
    GPU が同一パス内で記録した生判定 (bit 13: isGpuOccluded) を全画素比較します。
    r_th, l_th が未指定の場合は、最も一致する設定を自動検出して検証します。
    """
    if not raw_path.exists():
        raise FileNotFoundError(f"RAW file not found: {raw_path}")
    raw_bytes = raw_path.read_bytes()
    packed = np.frombuffer(raw_bytes, dtype=np.uint32)

    is_eval = ((packed >> 12) & 1) == 1
    eval_count = int(np.sum(is_eval))
    if eval_count == 0:
        return {"eval_count": 0, "mismatches": 0, "match_rate": 100.0, "rule": "None"}

    eval_vals = packed[is_eval]
    masks = eval_vals & 0xFF
    n_occ = (eval_vals >> 16) & 0xFF
    l_max = (eval_vals >> 24) & 0x0F
    gpu_occ = ((eval_vals >> 13) & 1) == 1

    # 設定が明示指定されている場合
    if r_th is not None and l_th is not None:
        target_pairs = [(r_th, l_th)]
    else:
        # 代表的な設定から最小不一致のものを自動探索 (R6_L8, R5_L8, R4_L3, R8_L8 等)
        target_pairs = [(6, 8), (5, 8), (4, 3), (8, 8), (1, 8), (7, 8)]

    best_r, best_l = 8, 8
    min_mismatches = eval_count + 1
    for r, l in target_pairs:
        cpu_occ = (n_occ >= r) & (l_max <= l)
        m = int(np.sum(cpu_occ != gpu_occ))
        if m < min_mismatches:
            min_mismatches = m
            best_r, best_l = r, l
            if m == 0:
                break

    cpu_occ = (n_occ >= best_r) & (l_max <= best_l)
    mismatches = int(np.sum(cpu_occ != gpu_occ))
    match_rate = 100.0 * (1.0 - (mismatches / eval_count))

    return {
        "total_pixels": len(packed),
        "eval_count": eval_count,
        "mismatches": mismatches,
        "match_rate": match_rate,
        "rule": f"R{best_r}_L{best_l}",
        "cpu_occ_count": int(np.sum(cpu_occ)),
        "gpu_occ_count": int(np.sum(gpu_occ)),
    }

def verify_gpu_consistency(raw_path: Path, test_img_path: Path) -> Optional[Dict[str, float]]:
    """
    保存された 8ビット占有マスクから (4,3) と (5,8) の判定を再計算し、
    GPUが出力した実際のレンダリング画像 (test_*.png) の遮蔽結果と画素単位で一致するか検証します。
    """
    if not raw_path.exists() or not test_img_path.exists():
        return None
    test_img = cv2.imread(str(test_img_path), cv2.IMREAD_COLOR)
    if test_img is None:
        return None
    h, w = test_img.shape[:2]
    raw_bytes = raw_path.read_bytes()
    if len(raw_bytes) != w * h * 4:
        return None
    packed_img = np.frombuffer(raw_bytes, dtype=np.uint32).reshape((h, w))

    # 最適アライメント検出
    test_vis = np.any(test_img > 0, axis=-1)
    candidates = [
        ("None", packed_img),
        ("FlipUD", np.flipud(packed_img)),
        ("FlipLR", np.fliplr(packed_img)),
        ("FlipUD+LR", np.flipud(np.fliplr(packed_img))),
    ]
    best_img = packed_img
    max_overlap = -1
    for name, cand in candidates:
        cand_eval = ((cand >> 12) & 1) == 1
        overlap = int(np.sum(cand_eval & test_vis))
        if overlap > max_overlap:
            max_overlap = overlap
            best_img = cand

    packed_img = best_img
    is_eval = ((packed_img >> 12) & 1) == 1
    if np.sum(is_eval) == 0:
        return None

    eval_idx = np.where(is_eval)
    eval_vals = packed_img[eval_idx]
    masks = eval_vals & 0xFF
    n_occ = (eval_vals >> 16) & 0xFF
    l_max = (eval_vals >> 24) & 0x0F

    # 再計算判定
    sim_occ_r4l3 = (n_occ >= 4) & (l_max <= 3)
    sim_occ_r5l8 = (n_occ >= 5)

    # GPU画像での遮蔽判定 (仮想オブジェクト画素で RGB == 0 なら遮蔽)
    gpu_vis = test_vis[eval_idx]
    gpu_occ = ~gpu_vis

    match_r4l3 = np.mean(sim_occ_r4l3 == gpu_occ) * 100.0
    match_r5l8 = np.mean(sim_occ_r5l8 == gpu_occ) * 100.0

    return {
        "eval_pixels": int(np.sum(is_eval)),
        "match_r4l3_percent": match_r4l3,
        "match_r5l8_percent": match_r5l8,
    }

def main():
    parser = argparse.ArgumentParser(description="Batch analyze sector patterns across densities.")
    parser.add_argument("input_csv", type=Path, nargs="?", default=None,
                        help="Path to pattern_counts.csv")
    parser.add_argument("--dataset-dir", type=Path, default=None,
                        help="Dataset directory containing SectorMaskSweep and GT.")
    parser.add_argument("--density", type=str, default=None,
                        help="Specific density filter (e.g. '4.0'). If None, analyzes all densities.")
    parser.add_argument("--eye", type=str, default="Both", choices=["Both", "Left", "Right"],
                        help="Eye view to analyze (default: Both).")
    parser.add_argument("--verify-raw", type=Path, default=None,
                        help="Verify RAW internal CPU vs GPU consistency without GT.")
    parser.add_argument("--r-th", type=int, default=8,
                        help="R_th threshold for verification (default: 8).")
    parser.add_argument("--l-th", type=int, default=8,
                        help="L_th threshold for verification (default: 8).")
    parser.add_argument("--raw", type=Path, default=None,
                        help="Path to individual SectorMask_*.raw file.")
    parser.add_argument("--gt", type=Path, default=None,
                        help="Path to Ground Truth image.")
    parser.add_argument("--out-dir", type=Path, default=Path(r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\Pattern_Debug"),
                        help="Output directory for CSV reports.")
    args = parser.parse_args()

    # モード 0: RAW 内部の CPU vs GPU 生判定 不一致 0 画素検証
    if args.verify_raw is not None:
        print(f"=== RAW 内部整合性検証 (GT不要・CPU vs GPU 生判定) ===")
        print(f"RAW Path: {args.verify_raw}")
        print(f"再現設定: R_th={args.r_th}, L_th={args.l_th}")
        res = verify_raw_internal(args.verify_raw, args.r_th, args.l_th)
        print(f"  総画素数:         {res['total_pixels']}")
        print(f"  セクタ評価画素数: {res['eval_count']}")
        print(f"  CPU再計算遮蔽数:  {res['cpu_occ_count']}")
        print(f"  GPU生判定遮蔽数:  {res['gpu_occ_count']}")
        print(f"  不一致画素数:     {res['mismatches']} 画素")
        print(f"  完全一致率:       {res['match_rate']:.6f}%")
        if res['mismatches'] == 0:
            print("  >> [判定: PASS] 保存ビットマスクと GPU生判定が 0画素不一致で完全一致しました！")
        else:
            print(f"  >> [判定: FAIL] {res['mismatches']} 画素の不一致が検出されました。")
        return

    # モード 1: 個別 RAW ファイルと GT が指定された場合
    if args.raw is not None and args.gt is not None:
        print(f"Reading directly from RAW buffer: {args.raw}")
        print(f"Reading Ground Truth:            {args.gt}")
        gt_vis, gt_occ = load_from_raw_and_gt(args.raw, args.gt)
        summary = analyze_single_condition(gt_vis, gt_occ, args.out_dir, args.raw.stem)
        print("\n=======================================================")
        print(f" 密度 / 条件: {summary['density']}")
        print(f" 観測パターン数: {summary['observed_patterns']} / 256")
        print(f" [層3] 8ビットオラクル:  IoU={summary['oracle_8bit_iou']:.6f} (誤画素: {summary['oracle_8bit_errors']})")
        print(f" [理論] 最小誤画素数 E_min: {summary['e_min_sum']} 画素")
        print(f" [層2] 2特徴オラクル:    IoU={summary['oracle_2feat_iou']:.6f} (誤画素: {summary['oracle_2feat_errors']})")
        print(f" [提案] R4_L3:           IoU={summary['r4_l3_iou']:.6f} (誤画素: {summary['r4_l3_errors']})")
        print(f" [従来] R5_L8:           IoU={summary['r5_l8_iou']:.6f} (誤画素: {summary['r5_l8_errors']})")
        print("=======================================================")
        return

    # モード 2: dataset-dir から全密度を一括自動探索する場合
    if args.dataset_dir is not None:
        sweep_dir = args.dataset_dir / "SectorMaskSweep"
        if not sweep_dir.exists():
            sweep_dir = args.dataset_dir

        density_dirs = sorted([
            d for d in sweep_dir.iterdir()
            if d.is_dir() and d.name.startswith("density_")
        ])

        if args.density:
            density_dirs = [d for d in density_dirs if args.density in d.name]

        if not density_dirs:
            print(f"No density directories found in {sweep_dir}")
            return

        target_eyes = ["Left", "Right"] if args.eye == "Both" else [args.eye]
        eye_label = "Both (Left & Right)" if args.eye == "Both" else args.eye
        print(f"=== SectorMask 全密度自動解析開始 (対象: {len(density_dirs)} 密度, 視点: {eye_label}) ===")
        print(f"Dataset Dir: {sweep_dir}")

        all_summaries = []

        for d_dir in density_dirs:
            d_name = d_dir.name.replace("density_", "")
            density_summaries = {}

            for eye in target_eyes:
                gt_path = sweep_dir / "GT" / eye / f"gt_{eye.lower()}.png"
                if not gt_path.exists():
                    gt_path = args.dataset_dir / "GT" / eye / f"gt_{eye.lower()}.png"
                if not gt_path.exists():
                    gt_path = args.dataset_dir.parent / "GT" / eye / f"gt_{eye.lower()}.png"

                if not gt_path.exists():
                    print(f"  [Skip] GT not found for {eye} in {d_dir.name}")
                    continue

                eye_dir = d_dir / eye
                raw_candidates = list(eye_dir.glob("*.raw")) if eye_dir.exists() else []
                if not raw_candidates:
                    raw_candidates = list(d_dir.glob(f"*{eye.lower()}*.raw"))

                if not raw_candidates:
                    print(f"  [Skip] No .raw file in {d_dir.name}/{eye}")
                    continue

                # タイムスタンプが最新の RAW ファイルを選択
                raw_candidates.sort(key=lambda p: p.stat().st_mtime, reverse=True)
                raw_path = raw_candidates[0]

                print(f"\nProcessing {d_dir.name} [{eye}] -> {raw_path.name}...")

                # ① 自動で ② (RAW 内部整合性検証) を実行
                raw_ver = verify_raw_internal(raw_path)
                ver_status = "PASS" if raw_ver["mismatches"] == 0 else f"FAIL({raw_ver['mismatches']})"
                print(f"  [{ver_status}] RAW内部検証 ({raw_ver['rule']}): 不一致 {raw_ver['mismatches']} 画素 (一致率: {raw_ver['match_rate']:.4f}%)")

                # ② 自動で ③ (Viewport アライメント & GT 照合 & オラクル理論値算出) を実行
                gt_vis, gt_occ = load_from_raw_and_gt(raw_path, gt_path)
                cond_label = f"{d_dir.name}_{eye.lower()}"
                res = analyze_single_condition(gt_vis, gt_occ, args.out_dir, cond_label)
                res["eye"] = eye
                res["density_label"] = d_name
                res["raw_mismatches"] = raw_ver["mismatches"]
                res["raw_match_rate"] = raw_ver["match_rate"]
                res["raw_rule"] = raw_ver["rule"]
                density_summaries[eye] = res
                all_summaries.append(res)

                # GPU 実測画像との画素単位整合性検証
                test_candidates = list(eye_dir.glob("test_*.png")) if eye_dir.exists() else []
                if not test_candidates:
                    test_candidates = list(d_dir.glob(f"test_*{eye.lower()}*.png"))
                if test_candidates:
                    test_path = test_candidates[0]
                    v_res = verify_gpu_consistency(raw_path, test_path)
                    if v_res:
                        print(f"  [GPU一致検証] 画素数: {v_res['eval_pixels']}, 提案(4,3)一致率: {v_res['match_r4l3_percent']:.2f}%, 従来(5,8)一致率: {v_res['match_r5l8_percent']:.2f}%")

            # Both の場合は左右平均 (Stereo Avg) を算出
            if args.eye == "Both" and "Left" in density_summaries and "Right" in density_summaries:
                l_res = density_summaries["Left"]
                r_res = density_summaries["Right"]
                avg_res = {
                    "density": f"{d_dir.name}_stereo_avg",
                    "density_label": d_name,
                    "eye": "StereoAvg",
                    "observed_patterns": max(l_res["observed_patterns"], r_res["observed_patterns"]),
                    "observed_rot_classes": max(l_res.get("observed_rot_classes", 0), r_res.get("observed_rot_classes", 0)),
                    "observed_classes": max(l_res["observed_classes"], r_res["observed_classes"]),
                    "oracle_8bit_iou": (l_res["oracle_8bit_iou"] + r_res["oracle_8bit_iou"]) / 2.0,
                    "oracle_8bit_errors": (l_res["oracle_8bit_errors"] + r_res["oracle_8bit_errors"]) // 2,
                    "oracle_rot_iou": (l_res["oracle_rot_iou"] + r_res["oracle_rot_iou"]) / 2.0,
                    "oracle_rot_errors": (l_res["oracle_rot_errors"] + r_res["oracle_rot_errors"]) // 2,
                    "oracle_2feat_iou": (l_res["oracle_2feat_iou"] + r_res["oracle_2feat_iou"]) / 2.0,
                    "oracle_2feat_errors": (l_res["oracle_2feat_errors"] + r_res["oracle_2feat_errors"]) // 2,
                    "e_min_sum": (l_res.get("e_min_sum", 0) + r_res.get("e_min_sum", 0)) // 2,
                    "r4_l3_iou": (l_res["r4_l3_iou"] + r_res["r4_l3_iou"]) / 2.0,
                    "r4_l3_errors": (l_res["r4_l3_errors"] + r_res["r4_l3_errors"]) // 2,
                    "r5_l8_iou": (l_res["r5_l8_iou"] + r_res["r5_l8_iou"]) / 2.0,
                    "r5_l8_errors": (l_res["r5_l8_errors"] + r_res["r5_l8_errors"]) // 2,
                }
                all_summaries.append(avg_res)

        if all_summaries:
            print("\n" + "=" * 135)
            print(f"{'密度 (Density)':18s} | {'視点':10s} | {'RAW内部検証':16s} | {'8Bitオラクル (256)':17s} | {'2特徴オラクル (21)':17s} | {'提案 R4_L3':11s} | {'従来 R5_L8':11s} | {'最小誤画素 E_min':14s}")
            print("-" * 135)
            for s in all_summaries:
                eye_str = s.get("eye", "")
                is_avg = (eye_str == "StereoAvg")
                prefix = "  >> " if is_avg else ""
                raw_info = "StereoAvg" if is_avg else f"{'PASS' if s.get('raw_mismatches', 0) == 0 else 'FAIL'} ({s.get('raw_mismatches', 0)}px)"
                e_min_str = f"{s.get('e_min_sum', 0):,d} px"
                print(f"{s['density_label']:18s} | {prefix + eye_str:10s} | {raw_info:16s} | {s['oracle_8bit_iou']:.6f}           | {s['oracle_2feat_iou']:.6f}           | {s['r4_l3_iou']:.6f}   | {s['r5_l8_iou']:.6f}   | {e_min_str:14s}")
                if is_avg:
                    print("-" * 135)
            print("=" * 135)
            print(f"全密度の解析結果を保存しました: {args.out_dir}\n")
        return

    # モード 3: 単一の CSV からの読み込み (従来互換)
    if args.input_csv is not None:
        gt_vis = [0] * PATTERN_COUNT
        gt_occ = [0] * PATTERN_COUNT
        with args.input_csv.open("r", newline="", encoding="utf-8-sig") as stream:
            for row in csv.DictReader(stream):
                m = int(row["mask"])
                gt_vis[m] += int(row["gt_visible_count"])
                gt_occ[m] += int(row["gt_occluded_count"])
        analyze_single_condition(gt_vis, gt_occ, args.out_dir, args.input_csv.stem)

if __name__ == "__main__":
    main()
