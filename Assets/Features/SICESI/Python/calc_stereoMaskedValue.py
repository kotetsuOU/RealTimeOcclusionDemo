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
  python calc_stereoMaskedValue.py C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset -c Bouchiba
"""

import sys
import os
import re
import glob
import csv
import json
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
    """論理和(Union Mask)およびGT可視領域に基づく評価指標を計算"""
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

    def calculate_metrics(self) -> Dict[str, any]:
        valid_pixels = np.sum(self.union_mask)
        if valid_pixels == 0:
            return {
                "IoU": 0.0, "OverOcc_wrt_GT_Visible": 0.0,
                "OverOcc_wrt_Union": 0.0, "UnderOcc_wrt_Union": 0.0,
                "FN_pixels": 0, "FP_pixels": 0, "TP_pixels": 0,
                "GT_Visible_pixels": 0, "Union_pixels": 0,
                "MAE": 0.0, "PSNR": 0.0, "DSSIM": 0.0
            }

        # [1] IoU & 誤遮蔽・誤透過の分析
        intersection = self.ref_mask & self.test_mask  # TP: 正しく可視
        fn_mask = self.ref_mask & (~self.test_mask)    # FN: 誤遮蔽 (Over-occlusion: 本来見えるはずが遮蔽)
        fp_mask = (~self.ref_mask) & self.test_mask    # FP: 誤透過 (Under-occlusion: 本来隠れるはずが透過)

        tp_pixels = int(np.sum(intersection))
        fn_pixels = int(np.sum(fn_mask))
        fp_pixels = int(np.sum(fp_mask))
        union_pixels = int(valid_pixels)
        gt_visible_pixels = tp_pixels + fn_pixels  # GTで可視であるべき正解画素数 (Ref Mask)

        iou = float(tp_pixels / union_pixels) if union_pixels > 0 else 0.0

        # 和集合(Union)を分母とする誤遮蔽率・誤透過率 (IoU + OverOcc_Union + UnderOcc_Union = 1.0)
        over_occ_wrt_union = float(fn_pixels / union_pixels) if union_pixels > 0 else 0.0
        under_occ_wrt_union = float(fp_pixels / union_pixels) if union_pixels > 0 else 0.0

        # GT可視領域(TP + FN)を分母とする過剰遮蔽率 (False Negative Rate = 1 - Recall)
        over_occ_wrt_gt_visible = float(fn_pixels / gt_visible_pixels) if gt_visible_pixels > 0 else 0.0

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
            "OverOcc_wrt_GT_Visible": over_occ_wrt_gt_visible,
            "OverOcc_wrt_Union": over_occ_wrt_union,
            "UnderOcc_wrt_Union": under_occ_wrt_union,
            "FN_pixels": fn_pixels,
            "FP_pixels": fp_pixels,
            "TP_pixels": tp_pixels,
            "GT_Visible_pixels": gt_visible_pixels,
            "Union_pixels": union_pixels,
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

def evaluate_stereo_pair(gt_dir: str, test_dir: str) -> Optional[Dict[str, Dict[str, any]]]:
    # 左目
    gt_left_path = find_first_image(os.path.join(gt_dir, "Left"))
    test_left_path = find_first_image(os.path.join(test_dir, "Left"))

    # 右目
    gt_right_path = find_first_image(os.path.join(gt_dir, "Right"))
    test_right_path = find_first_image(os.path.join(test_dir, "Right"))

    if not (gt_left_path and test_left_path and gt_right_path and test_right_path):
        return None

    try:
        gt_l = ImageHandler.load_rgb(gt_left_path)
        test_l = ImageHandler.load_rgb(test_left_path)
        res_l = UnionMaskMetrics(gt_l, test_l).calculate_metrics()

        gt_r = ImageHandler.load_rgb(gt_right_path)
        test_r = ImageHandler.load_rgb(test_right_path)
        res_r = UnionMaskMetrics(gt_r, test_r).calculate_metrics()
    except Exception as e:
        print(f"  [画像読み込みスキップ: {test_dir}]: {e}", flush=True)
        return None

    pixel_keys = ["TP_pixels", "FP_pixels", "FN_pixels", "GT_Visible_pixels", "Union_pixels"]
    metric_keys = ["IoU", "OverOcc_wrt_GT_Visible", "OverOcc_wrt_Union", "UnderOcc_wrt_Union", "MAE", "PSNR", "DSSIM"]

    # 左右合算 (Total: 論文等で画素数を直接足して示すための厳密な整数値)
    res_bino_total = {
        k: int(res_l[k] + res_r[k]) for k in pixel_keys
    }

    # 左右平均 (Mean: 左右カメラの平均的な性能を表す指標)
    res_bino_mean = {
        k: (res_l[k] + res_r[k]) / 2.0 for k in (pixel_keys + metric_keys)
    }

    # 画素数合算値から直接算出した総合率 (Micro-average)
    bino_union = res_bino_total["Union_pixels"]
    bino_gt_vis = res_bino_total["GT_Visible_pixels"]
    res_bino_total["IoU"] = float(res_bino_total["TP_pixels"] / bino_union) if bino_union > 0 else 0.0
    res_bino_total["OverOcc_wrt_GT_Visible"] = float(res_bino_total["FN_pixels"] / bino_gt_vis) if bino_gt_vis > 0 else 0.0
    res_bino_total["OverOcc_wrt_Union"] = float(res_bino_total["FN_pixels"] / bino_union) if bino_union > 0 else 0.0
    res_bino_total["UnderOcc_wrt_Union"] = float(res_bino_total["FP_pixels"] / bino_union) if bino_union > 0 else 0.0
    res_bino_total["MAE"] = (res_l["MAE"] + res_r["MAE"]) / 2.0
    res_bino_total["PSNR"] = (res_l["PSNR"] + res_r["PSNR"]) / 2.0
    res_bino_total["DSSIM"] = (res_l["DSSIM"] + res_r["DSSIM"]) / 2.0

    return {
        "Left": res_l,
        "Right": res_r,
        "BinocularMean": res_bino_mean,
        "BinocularTotal": res_bino_total
    }

def natural_sort_key(s: str):
    """文字列中の数値を数値として扱い自然順 (sector_0, sector_1, ..., sector_8) でソート"""
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'(\d+)', s)]

def parse_condition_metadata(cond_str: str, test_dir_abs: Optional[str] = None) -> Dict[str, any]:
    """パス文字列および evaluation_params.json から実験パラメータ（固定モード、密度、セクター数、連続0数、オクルージョン閾値）を抽出"""
    meta = {
        "Fixed_Mode": "",
        "Density_Value": "",
        "Density_Unit": "",
        "Sector": "",
        "Max_Consecutive_Zeros": "",
        "Threshold": ""
    }

    # 1. evaluation_params.json が存在する場合は正確な実数値を最優先で採用
    if test_dir_abs:
        json_path = os.path.join(test_dir_abs, "evaluation_params.json")
        if os.path.isfile(json_path):
            try:
                with open(json_path, "r", encoding="utf-8") as jf:
                    data = json.load(jf)
                    meta["Density_Value"] = float(data.get("densityValue", 0.0))
                    meta["Density_Unit"] = str(data.get("densityUnit", ""))
                    meta["Threshold"] = float(data.get("occlusionThreshold", 0.0))
                    meta["Fixed_Mode"] = str(data.get("evaluationMode", ""))
                    meta["Sector"] = str(data.get("minOccludedSectors", ""))
                    meta["Max_Consecutive_Zeros"] = str(data.get("maxConsecutiveEmptySectors", ""))
                    return meta
            except Exception:
                pass

    # 2. JSON がない場合はパス文字列から正規表現でパース
    m_fixed = re.search(r'Fixed_(Sector_\d+|Average)', cond_str, re.IGNORECASE)
    if m_fixed:
        meta["Fixed_Mode"] = m_fixed.group(1)

    m_density = re.search(r'density_([0-9\.]+)(mm|pts_cm2|pts_mm2|pts)?', cond_str, re.IGNORECASE)
    if m_density:
        try:
            meta["Density_Value"] = float(m_density.group(1))
        except ValueError:
            meta["Density_Value"] = m_density.group(1)
        meta["Density_Unit"] = m_density.group(2) if m_density.group(2) else ""

    if meta["Fixed_Mode"]:
        if "Sector_" in meta["Fixed_Mode"]:
            meta["Sector"] = meta["Fixed_Mode"].split('_')[-1]
        elif "Average" in meta["Fixed_Mode"]:
            meta["Sector"] = "Average"
    else:
        m_sec = re.search(r'sector_(\d+)', cond_str, re.IGNORECASE)
        if m_sec:
            meta["Sector"] = m_sec.group(1)
        elif "average" in cond_str.lower():
            meta["Sector"] = "Average"

    m_zero = re.search(r'maxzero_(\d+)', cond_str, re.IGNORECASE)
    if m_zero:
        meta["Max_Consecutive_Zeros"] = m_zero.group(1)

    m_occ = re.search(r'occ_([0-9\.]+)', cond_str, re.IGNORECASE)
    if m_occ:
        try:
            meta["Threshold"] = float(m_occ.group(1))
        except ValueError:
            meta["Threshold"] = m_occ.group(1)

    return meta

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

    print("\n" + "=" * 135, flush=True)
    print(f"{'Condition':<40} | {'Bino IoU':<8} | {'過剰遮蔽(GT基準)':<16} | {'誤遮蔽(Union)':<14} | {'誤透過(Union)':<14} | {'Total FN / FP':<18} | {'Bino DSSIM':<10}", flush=True)
    print("-" * 135, flush=True)

    for idx, cond in enumerate(candidate_dirs):
        test_dir = os.path.join(base_dataset_dir, cond)
        target_gt_dir = resolve_gt_dir(test_dir, base_dataset_dir)
        if not target_gt_dir:
            print(f"警告: {cond} に対応する GT フォルダが見つかりません。スキップします。", flush=True)
            continue

        res = evaluate_stereo_pair(target_gt_dir, test_dir)
        if res is None:
            continue

        # 両眼合算値 (Total: 左右加算)
        total_fn = res["BinocularTotal"]["FN_pixels"]
        total_fp = res["BinocularTotal"]["FP_pixels"]
        total_tp = res["BinocularTotal"]["TP_pixels"]
        total_gt_vis = res["BinocularTotal"]["GT_Visible_pixels"]
        total_union = res["BinocularTotal"]["Union_pixels"]

        # 指標
        b_iou = res["BinocularMean"]["IoU"]
        b_dssim = res["BinocularMean"]["DSSIM"]

        b_over_gt = res["BinocularMean"]["OverOcc_wrt_GT_Visible"] * 100
        b_over_union = res["BinocularMean"]["OverOcc_wrt_Union"] * 100
        b_under_union = res["BinocularMean"]["UnderOcc_wrt_Union"] * 100

        l_iou = res["Left"]["IoU"]
        l_over_gt = res["Left"]["OverOcc_wrt_GT_Visible"] * 100
        l_over_union = res["Left"]["OverOcc_wrt_Union"] * 100
        l_under_union = res["Left"]["UnderOcc_wrt_Union"] * 100

        r_iou = res["Right"]["IoU"]
        r_over_gt = res["Right"]["OverOcc_wrt_GT_Visible"] * 100
        r_over_union = res["Right"]["OverOcc_wrt_Union"] * 100
        r_under_union = res["Right"]["UnderOcc_wrt_Union"] * 100

        fn_fp_str = f"{total_fn} / {total_fp}"
        print(f"{cond:<40} | {b_iou:<8.4f} | {b_over_gt:>5.2f}% ({total_fn:>6}px) | {b_over_union:>5.2f}%         | {b_under_union:>5.2f}%         | {fn_fp_str:<18} | {b_dssim:<10.4f}", flush=True)

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

        meta = parse_condition_metadata(cond, test_dir)

        csv_rows.append({
            "Condition": cond,
            "Fixed_Mode": meta["Fixed_Mode"],
            "Density_Value": meta["Density_Value"],
            "Density_Unit": meta["Density_Unit"],
            "Sector_Rth": meta["Sector"],
            "Max_Consecutive_Zeros": meta["Max_Consecutive_Zeros"],
            "Occlusion_Threshold": meta["Threshold"],

            # --- 両眼集計 (論文用: 画素数は Left+Right 厳密合算、率は左右平均) ---
            "Bino_IoU": b_iou,
            "Bino_誤遮蔽率(GT可視基準)%": round(b_over_gt, 2),
            "Bino_誤遮蔽率(Union基準)%": round(b_over_union, 2),
            "Bino_誤透過率(Union基準)%": round(b_under_union, 2),
            "Bino_Total_誤遮蔽画素(FN)": total_fn,
            "Bino_Total_誤透過画素(FP)": total_fp,
            "Bino_Total_正解可視画素(TP)": total_tp,
            "Bino_Total_GT可視画素数": total_gt_vis,
            "Bino_Total_Union画素数": total_union,
            "Bino_DSSIM": b_dssim,

            # --- 左眼詳細 ---
            "Left_IoU": l_iou,
            "Left_誤遮蔽率(GT可視基準)%": round(l_over_gt, 2),
            "Left_誤遮蔽率(Union基準)%": round(l_over_union, 2),
            "Left_誤透過率(Union基準)%": round(l_under_union, 2),
            "Left_誤遮蔽画素数(FN)": int(res["Left"]["FN_pixels"]),
            "Left_誤透過画素数(FP)": int(res["Left"]["FP_pixels"]),
            "Left_正解可視画素数(TP)": int(res["Left"]["TP_pixels"]),
            "Left_GT可視画素数": int(res["Left"]["GT_Visible_pixels"]),
            "Left_Union画素数": int(res["Left"]["Union_pixels"]),
            "Left_DSSIM": res["Left"]["DSSIM"],

            # --- 右眼詳細 ---
            "Right_IoU": r_iou,
            "Right_誤遮蔽率(GT可視基準)%": round(r_over_gt, 2),
            "Right_誤遮蔽率(Union基準)%": round(r_over_union, 2),
            "Right_誤透過率(Union基準)%": round(r_under_union, 2),
            "Right_誤遮蔽画素数(FN)": int(res["Right"]["FN_pixels"]),
            "Right_誤透過画素数(FP)": int(res["Right"]["FP_pixels"]),
            "Right_正解可視画素数(TP)": int(res["Right"]["TP_pixels"]),
            "Right_GT可視画素数": int(res["Right"]["GT_Visible_pixels"]),
            "Right_Union画素数": int(res["Right"]["Union_pixels"]),
            "Right_DSSIM": res["Right"]["DSSIM"],

            # --- 補足・参考値 ---
            "Bino_Mean_誤遮蔽画素(FN)": round(res["BinocularMean"]["FN_pixels"], 1),
            "Bino_Mean_誤透過画素(FP)": round(res["BinocularMean"]["FP_pixels"], 1),
            "Bino_PSNR": res["BinocularMean"]["PSNR"],
            "Bino_MAE": res["BinocularMean"]["MAE"]
        })

    print("=" * 135, flush=True)

    # CSV保存 (Excelでそのまま開けるよう UTF-8 with BOM で出力)
    if csv_rows:
        with open(csv_path, mode="w", newline="", encoding="utf-8-sig") as f:
            writer = csv.DictWriter(f, fieldnames=csv_rows[0].keys())
            writer.writeheader()
            writer.writerows(csv_rows)
        print(f"\n[完了] 集計結果をCSVに出力しました: {csv_path}", flush=True)

if __name__ == "__main__":
    main()
