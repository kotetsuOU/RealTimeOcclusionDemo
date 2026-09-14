"""
SICE SI 2026: ハーフミラー型3Dディスプレイにおけるリアルタイム遮蔽処理
両眼（Left / Right）画像マルチメトリクス評価スクリプト

ディレクトリ構造例:
  1) 密度スイープ:
     SICESI_Dataset/
       ├── GT/
       │   ├── Left/gt_left.png
       │   └── Right/gt_right.png
       ├── Proposed_Oct_density_1.00mm/
       └── Proposed_Oct_density_2.00mm/

  2) Bouchiba セクタースイープ (密度サブフォルダ階層):
     SICESI_Dataset/SectorSweep/
       ├── GT/
       │   ├── Left/gt_left.png
       │   └── Right/gt_right.png
       ├── density_1.00mm/
       │   ├── Proposed_Oct_Average/
       │   ├── Proposed_Oct_sector_1/
       │   └── ...
       │   └── Proposed_Oct_sector_8/
       └── density_2.00mm/
           ├── Proposed_Oct_Average/
           └── ...

実行方法:
  # 1) 全条件の一括評価:
  python calc_stereoMaskedValue.py

  # 2) 特定の条件名 (例: Bouchiba) のみ絞り込んで高速評価 & CSV分離保存:
  python calc_stereoMaskedValue.py -c Bouchiba
  python calc_stereoMaskedValue.py -c Proposed_Oct

  # 3) 特定の密度のみ絞り込み:
  python calc_stereoMaskedValue.py -c density_1.00

  # 4) ディレクトリ指定と組み合わせ:
  python calc_stereoMaskedValue.py C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset/SectorSweep -c Bouchiba
"""

import sys
import os
import re
import glob
import csv
import argparse
from typing import Dict, Optional, List

# Windowsコンソールでの文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import cv2
import numpy as np
from skimage.metrics import structural_similarity as ssim

class ImageHandler:
    @staticmethod
    def load_rgb(file_path: str) -> np.ndarray:
        img_bgr = cv2.imread(file_path, cv2.IMREAD_COLOR)
        if img_bgr is None:
            raise ValueError(f"画像の読み込みに失敗しました: {file_path}")
        return cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)

class UnionMaskMetrics:
    """論理和(Union Mask)に基づく4つの評価指標 (IoU, MAE, PSNR, DSSIM) を計算"""
    def __init__(self, ref_img: np.ndarray, test_img: np.ndarray, data_range: float = 255.0):
        self.ref_img = ref_img
        self.test_img = test_img
        self.data_range = float(data_range)
        self._validate_images()
        
        self.ref_mask = np.any(self.ref_img > 0, axis=-1)
        self.test_mask = np.any(self.test_img > 0, axis=-1)
        self.union_mask = self.ref_mask | self.test_mask

    def _validate_images(self) -> None:
        if self.ref_img.shape != self.test_img.shape:
            raise ValueError(f"画像サイズ不一致: 基準 {self.ref_img.shape} vs 対象 {self.test_img.shape}")

    def calculate_metrics(self) -> Dict[str, float]:
        valid_pixels = np.sum(self.union_mask)
        if valid_pixels == 0:
            return {"IoU": 0.0, "MAE": 0.0, "PSNR": 0.0, "DSSIM": 0.0}

        # [1] IoU & 誤遮蔽・誤透過の分析
        intersection = self.ref_mask & self.test_mask  # TP: 正しく表示
        fn_mask = self.ref_mask & (~self.test_mask)    # FN: 誤遮蔽 (Over-occlusion: 本来見えるはずが遮蔽)
        fp_mask = (~self.ref_mask) & self.test_mask    # FP: 誤透過 (Under-occlusion: 本来隠れるはずが透過)

        tp_pixels = int(np.sum(intersection))
        fn_pixels = int(np.sum(fn_mask))
        fp_pixels = int(np.sum(fp_mask))

        iou = float(tp_pixels / valid_pixels)
        over_occ_rate = float(fn_pixels / valid_pixels)   # 誤遮蔽率 (FN / Union)
        under_occ_rate = float(fp_pixels / valid_pixels) # 誤透過率 (FP / Union)

        # [2] Masked MAE & PSNR
        ref_f = self.ref_img.astype(np.float64)
        test_f = self.test_img.astype(np.float64)
        diff = np.abs(ref_f - test_f)[self.union_mask]
        mae = float(np.mean(diff))

        mse = float(np.mean(diff ** 2))
        psnr = float('inf') if mse == 0 else float(10 * np.log10((self.data_range ** 2) / mse))

        # [3] Masked DSSIM
        _, ssim_map = ssim(self.ref_img, self.test_img, channel_axis=-1, full=True, data_range=self.data_range)
        masked_ssim = float(np.mean(ssim_map[self.union_mask]))
        dssim = (1.0 - masked_ssim) / 2.0

        return {
            "IoU": iou,
            "OverOccRate": over_occ_rate,
            "UnderOccRate": under_occ_rate,
            "FN_pixels": fn_pixels,
            "FP_pixels": fp_pixels,
            "TP_pixels": tp_pixels,
            "Union_pixels": int(valid_pixels),
            "MAE": mae,
            "PSNR": psnr,
            "DSSIM": dssim
        }

def find_first_image(folder_path: str) -> Optional[str]:
    for ext in ("*.png", "*.bmp", "*.jpg"):
        files = glob.glob(os.path.join(folder_path, ext))
        if files:
            return sorted(files)[0]
    return None

def evaluate_stereo_pair(gt_dir: str, test_dir: str) -> Optional[Dict[str, Dict[str, float]]]:
    # 左目
    gt_left_path = find_first_image(os.path.join(gt_dir, "Left"))
    test_left_path = find_first_image(os.path.join(test_dir, "Left"))

    # 右目
    gt_right_path = find_first_image(os.path.join(gt_dir, "Right"))
    test_right_path = find_first_image(os.path.join(test_dir, "Right"))

    if not (gt_left_path and test_left_path and gt_right_path and test_right_path):
        return None

    gt_l = ImageHandler.load_rgb(gt_left_path)
    test_l = ImageHandler.load_rgb(test_left_path)
    res_l = UnionMaskMetrics(gt_l, test_l).calculate_metrics()

    gt_r = ImageHandler.load_rgb(gt_right_path)
    test_r = ImageHandler.load_rgb(test_right_path)
    res_r = UnionMaskMetrics(gt_r, test_r).calculate_metrics()

    # 両眼平均
    res_bino = {
        k: (res_l[k] + res_r[k]) / 2.0 for k in res_l.keys()
    }

    return {
        "Left": res_l,
        "Right": res_r,
        "BinocularMean": res_bino
    }

def natural_sort_key(s: str):
    """文字列中の数値を数値として扱い自然順 (sector_0, sector_1, ..., sector_8) でソート"""
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'(\d+)', s)]

def parse_args():
    parser = argparse.ArgumentParser(
        description="SICE SI 2026: 両眼画像マルチメトリクス評価スクリプト"
    )
    parser.add_argument(
        "dataset_dir",
        nargs="?",
        default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset",
        help="データセットディレクトリ (省略時: C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset)"
    )
    parser.add_argument(
        "-c", "--condition", "--filter", "-f",
        dest="condition_filter",
        default=None,
        help="評価対象とする条件名の絞り込みキーワード (例: -c Bouchiba, -c Proposed_Oct, -c density_1.00)"
    )
    parser.add_argument(
        "-o", "--output-csv",
        dest="output_csv",
        default=None,
        help="出力先CSVファイル名 (省略時は stereo_evaluation_summary[_condition].csv)"
    )
    parser.add_argument(
        "--export-diff",
        action="store_true",
        dest="export_diff",
        help="評価と同時に定性分析用の差分マップ（誤遮蔽:赤、誤透過:青）を出力します"
    )
    parser.add_argument(
        "--diff-dir",
        dest="diff_dir",
        default=None,
        help="差分マップの保存先ディレクトリ（省略時かつ --diff-in-place なしの場合はデータセット直下の DiffMaps フォルダ）"
    )
    parser.add_argument(
        "--diff-in-place",
        action="store_true",
        dest="diff_in_place",
        help="差分マップを元の各評価フォルダ (Left/Right直下) に保存し、フォルダ構造を維持する"
    )
    return parser.parse_args()

def resolve_gt_dir(test_dir_abs: str, base_dataset_dir: str) -> Optional[str]:
    """
    テストフォルダに対して最も適切な GT ディレクトリを階層的に探索・解決します。
    1. test_dir の祖先ディレクトリ直下にある 'GT' (例: SICESI_Dataset/ConditionName/GT)
    2. base_dataset_dir 直下の 'GT' (例: SICESI_Dataset/GT)
    3. base_dataset_dir 親階層の 'GT'
    4. base_dataset_dir 配下の再帰探索で見つかる任意の有効な 'GT'
    """
    curr = os.path.abspath(test_dir_abs)
    base = os.path.abspath(base_dataset_dir)

    while True:
        candidate = os.path.join(curr, "GT")
        if os.path.isdir(candidate) and os.path.isdir(os.path.join(candidate, "Left")):
            return candidate
        if curr == base or os.path.dirname(curr) == curr:
            break
        curr = os.path.dirname(curr)

    candidate = os.path.join(base, "GT")
    if os.path.isdir(candidate) and os.path.isdir(os.path.join(candidate, "Left")):
        return candidate

    parent_candidate = os.path.join(base, "..", "GT")
    if os.path.isdir(parent_candidate) and os.path.isdir(os.path.join(parent_candidate, "Left")):
        return os.path.abspath(parent_candidate)

    for root, dirs, _ in os.walk(base):
        if "GT" in dirs:
            c = os.path.join(root, "GT")
            if os.path.isdir(os.path.join(c, "Left")):
                return c

    return None

def main():
    args = parse_args()
    base_dataset_dir = args.dataset_dir
    condition_filter = args.condition_filter

    print(f"=== SICE SI 両眼遮蔽評価スクリプト ===", flush=True)
    print(f"データセットディレクトリ: {base_dataset_dir}", flush=True)

    # 評価対象ディレクトリの探索:
    # 直下に "Left" と "Right" の両方が存在するフォルダーを再帰的に検出
    all_candidate_dirs = []
    for root, dirs, _ in os.walk(base_dataset_dir):
        rel_parts = os.path.relpath(root, base_dataset_dir).split(os.sep)
        if "GT" in rel_parts:
            continue
        if "Left" in dirs and "Right" in dirs:
            rel_path = os.path.relpath(root, base_dataset_dir).replace("\\", "/")
            all_candidate_dirs.append(rel_path)

    all_candidate_dirs.sort(key=natural_sort_key)

    if not all_candidate_dirs:
        print("警告: 評価対象フォルダ (Left/Right を含むフォルダ) が見つかりません。", flush=True)
        return

    # 条件名 (ConditionName) による絞り込みフィルタの適用
    if condition_filter:
        pattern = condition_filter.lower()
        candidate_dirs = [d for d in all_candidate_dirs if pattern in d.lower()]
        print(f"フィルタ適用: '{condition_filter}' (一致: {len(candidate_dirs)} / 全 {len(all_candidate_dirs)} 件)", flush=True)
        if not candidate_dirs:
            print(f"警告: キーワード '{condition_filter}' に一致するフォルダが見つかりませんでした。", flush=True)
            return
    else:
        candidate_dirs = all_candidate_dirs
        # フォルダから検出された条件プレフィックスの一覧を表示
        detected_prefixes = set()
        for d in all_candidate_dirs:
            leaf = d.split('/')[-1]
            parts = leaf.split('_')
            if parts:
                detected_prefixes.add(parts[0])
        if len(detected_prefixes) > 1:
            print(f"検出された条件種別: {', '.join(sorted(detected_prefixes))}", flush=True)
            print("※ 特定の条件のみ計算したい場合は -c <条件名> (例: -c Bouchiba) を指定できます。", flush=True)

    # CSV 出力パスの決定
    if args.output_csv:
        csv_path = os.path.join(base_dataset_dir, args.output_csv)
    elif condition_filter:
        safe_tag = re.sub(r'[\\/:*?"<>| ]', '_', condition_filter)
        csv_path = os.path.join(base_dataset_dir, f"stereo_evaluation_summary_{safe_tag}.csv")
    else:
        csv_path = os.path.join(base_dataset_dir, "stereo_evaluation_summary.csv")

    csv_rows = []

    print("\n" + "=" * 125, flush=True)
    print(f"{'Condition':<40} | {'Bino IoU':<8} | {'誤遮蔽(過剰)':<12} | {'誤透過(漏れ)':<12} | {'Left(赤/青)':<16} | {'Right(赤/青)':<16} | {'Bino DSSIM':<10}", flush=True)
    print("-" * 125, flush=True)

    for idx, cond in enumerate(candidate_dirs):
        test_dir = os.path.join(base_dataset_dir, cond)
        target_gt_dir = resolve_gt_dir(test_dir, base_dataset_dir)
        if not target_gt_dir:
            print(f"警告: {cond} に対応する GT フォルダが見つかりません。スキップします。", flush=True)
            continue

        res = evaluate_stereo_pair(target_gt_dir, test_dir)
        if res is None:
            continue

        l_iou = res["Left"]["IoU"]
        r_iou = res["Right"]["IoU"]
        b_iou = res["BinocularMean"]["IoU"]
        b_dssim = res["BinocularMean"]["DSSIM"]

        l_over = res["Left"]["OverOccRate"] * 100
        l_under = res["Left"]["UnderOccRate"] * 100
        r_over = res["Right"]["OverOccRate"] * 100
        r_under = res["Right"]["UnderOccRate"] * 100
        b_over = res["BinocularMean"]["OverOccRate"] * 100
        b_under = res["BinocularMean"]["UnderOccRate"] * 100

        print(f"{cond:<40} | {b_iou:<8.4f} | {b_over:>5.1f}% ({int(res['BinocularMean']['FN_pixels']):>5}px) | {b_under:>5.1f}% ({int(res['BinocularMean']['FP_pixels']):>5}px) | {l_over:>4.1f}% / {l_under:>4.1f}%    | {r_over:>4.1f}% / {r_under:>4.1f}%    | {b_dssim:<10.4f}", flush=True)

        if args.export_diff:
            try:
                from export_occlusion_diff_maps import OcclusionDiffVisualizer
                if not args.diff_in_place:
                    diff_base = os.path.abspath(args.diff_dir) if args.diff_dir else os.path.join(base_dataset_dir, "DiffMaps")
                    cond_out = os.path.join(diff_base, cond)
                    os.makedirs(cond_out, exist_ok=True)

                for eye in ("Left", "Right"):
                    gt_path = find_first_image(os.path.join(target_gt_dir, eye))
                    test_eye_dir = os.path.join(test_dir, eye)
                    test_path = find_first_image(test_eye_dir)
                    if gt_path and test_path:
                        gt_bgr = cv2.imread(gt_path, cv2.IMREAD_COLOR)
                        test_bgr = cv2.imread(test_path, cv2.IMREAD_COLOR)
                        if gt_bgr is not None and test_bgr is not None:
                            gt_rgb = cv2.cvtColor(gt_bgr, cv2.COLOR_BGR2RGB)
                            test_rgb = cv2.cvtColor(test_bgr, cv2.COLOR_BGR2RGB)
                            vis = OcclusionDiffVisualizer(gt_rgb, test_rgb)
                            montage = vis.generate_montage(title=f"{cond} [{eye}]")
                            save_dir = test_eye_dir if args.diff_in_place else cond_out
                            prefix = "diff_" if args.diff_in_place else f"diff_{eye.lower()}_"
                            cv2.imwrite(os.path.join(save_dir, f"{prefix}montage.png") if args.diff_in_place else os.path.join(save_dir, f"diff_montage_{eye.lower()}.png"), cv2.cvtColor(montage, cv2.COLOR_RGB2BGR))
                            cv2.imwrite(os.path.join(save_dir, f"{prefix}mask.png") if args.diff_in_place else os.path.join(save_dir, f"diff_mask_{eye.lower()}.png"), cv2.cvtColor(vis.generate_diff_mask(), cv2.COLOR_RGB2BGR))
                            cv2.imwrite(os.path.join(save_dir, f"{prefix}overlay.png") if args.diff_in_place else os.path.join(save_dir, f"diff_overlay_{eye.lower()}.png"), cv2.cvtColor(vis.generate_overlay(), cv2.COLOR_RGB2BGR))
            except Exception as e:
                print(f"  [差分出力エラー]: {e}", flush=True)

        csv_rows.append({
            "Condition": cond,
            "Bino_IoU": b_iou,
            "Bino_誤遮蔽率(過剰遮蔽)%": round(b_over, 2),
            "Bino_誤透過率(遮蔽漏れ)%": round(b_under, 2),
            "Bino_誤遮蔽画素数(FN)": int(res["BinocularMean"]["FN_pixels"]),
            "Bino_誤透過画素数(FP)": int(res["BinocularMean"]["FP_pixels"]),
            "Left_IoU": l_iou,
            "Left_誤遮蔽率%": round(l_over, 2),
            "Left_誤透過率%": round(l_under, 2),
            "Left_誤遮蔽画素数": int(res["Left"]["FN_pixels"]),
            "Left_誤透過画素数": int(res["Left"]["FP_pixels"]),
            "Right_IoU": r_iou,
            "Right_誤遮蔽率%": round(r_over, 2),
            "Right_誤透過率%": round(r_under, 2),
            "Right_誤遮蔽画素数": int(res["Right"]["FN_pixels"]),
            "Right_誤透過画素数": int(res["Right"]["FP_pixels"]),
            "Bino_DSSIM": b_dssim,
            "Left_DSSIM": res["Left"]["DSSIM"],
            "Right_DSSIM": res["Right"]["DSSIM"],
            "Bino_PSNR": res["BinocularMean"]["PSNR"],
            "Bino_MAE": res["BinocularMean"]["MAE"]
        })

    print("=" * 125, flush=True)

    # CSV保存 (Excelでそのまま開けるよう UTF-8 with BOM で出力)
    if csv_rows:
        with open(csv_path, mode="w", newline="", encoding="utf-8-sig") as f:
            writer = csv.DictWriter(f, fieldnames=csv_rows[0].keys())
            writer.writeheader()
            writer.writerows(csv_rows)
        print(f"\n[完了] 集計結果をCSVに出力しました: {csv_path}", flush=True)

if __name__ == "__main__":
    main()
