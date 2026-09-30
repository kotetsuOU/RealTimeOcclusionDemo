"""
================================================================================
SICE SI 2026: ハーフミラー型3Dディスプレイにおけるリアルタイム遮蔽処理
両眼（Left / Right）画像マルチメトリクス評価スクリプト
(Feature: SICESI - Multi-Metrics Stereo Evaluator)
================================================================================

【概要】
  左右眼のテスト画像と GT 画像から、IoU、PSNR、SSIM、DSSIM、MAE、過剰遮蔽(FN)、
  遮蔽漏れ(FP)画素数を一括計算し、Excel/論文用の総合サマリー CSV を出力します。

【実行例】
  python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py
  python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py -c Bouchiba
  python Assets/Features/SICESI/Python/calc_stereoMaskedValue.py -c Bouchiba --export-diff
"""

import sys
import os
import csv
import argparse
from typing import Dict, Optional, List, Any

# Windows コンソール文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import cv2
import numpy as np

from sicesi_core.constants import DEFAULT_BINARY_THRESHOLD
from sicesi_core.metrics import UnionMaskMetrics
from sicesi_core.visualizer import OcclusionDiffVisualizer
from sicesi_core.dataset_loader import (
    find_first_image, resolve_gt_dir, parse_condition_metadata, natural_sort_key
)


def load_rgb_image(file_path: str) -> np.ndarray:
    img_bgr = cv2.imread(file_path, cv2.IMREAD_COLOR)
    if img_bgr is None:
        raise ValueError(f"画像の読み込みに失敗しました: {file_path}")
    return cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)


def evaluate_stereo_pair(gt_dir: str, test_dir: str, threshold: int = 128) -> Optional[Dict[str, Dict[str, Any]]]:
    # 左目
    gt_left_path = find_first_image(os.path.join(gt_dir, "Left"), is_gt=True)
    test_left_path = find_first_image(os.path.join(test_dir, "Left"), is_gt=False)

    # 右目
    gt_right_path = find_first_image(os.path.join(gt_dir, "Right"), is_gt=True)
    test_right_path = find_first_image(os.path.join(test_dir, "Right"), is_gt=False)

    if not (gt_left_path and test_left_path and gt_right_path and test_right_path):
        return None

    try:
        gt_l = load_rgb_image(gt_left_path)
        test_l = load_rgb_image(test_left_path)
        res_l = UnionMaskMetrics(gt_l, test_l, threshold=threshold).calculate_metrics()

        gt_r = load_rgb_image(gt_right_path)
        test_r = load_rgb_image(test_right_path)
        res_r = UnionMaskMetrics(gt_r, test_r, threshold=threshold).calculate_metrics()
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


def parse_args():
    parser = argparse.ArgumentParser(description="SICE SI 2026: 両眼画像マルチメトリクス評価スクリプト")
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_dataset = os.path.abspath(os.path.join(script_dir, "../../../../../Estimation/SICESI_Dataset"))

    parser.add_argument(
        "dataset_dir",
        nargs="?",
        default=default_dataset,
        help=f"データセットディレクトリ (省略時: {default_dataset})"
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
    parser.add_argument(
        "-th", "--threshold",
        dest="threshold",
        type=int,
        default=DEFAULT_BINARY_THRESHOLD,
        help="二値化判定の閾値 (0-255, デフォルト: 128。パイプライン一貫性のための50%%カバレッジ基準。0指定で従来の>0判定)"
    )
    return parser.parse_args()


def main():
    args = parse_args()
    base_dataset_dir = os.path.abspath(args.dataset_dir)
    condition_filter = args.condition_filter

    if args.output_csv:
        csv_path = os.path.abspath(args.output_csv)
    else:
        suffix = f"_{condition_filter}" if condition_filter else ""
        csv_path = os.path.join(base_dataset_dir, f"stereo_evaluation_summary{suffix}.csv")

    diff_output_base = os.path.abspath(args.diff_dir) if args.diff_dir else os.path.join(base_dataset_dir, "DiffMaps")

    print("=== SICE SI 2026: リアルタイム遮蔽処理 ステレオ画像マルチメトリクス評価 ===", flush=True)
    print(f"データセットディレクトリ: {base_dataset_dir}", flush=True)
    print(f"二値化判定閾値         : {args.threshold} (50%カバレッジ基準)", flush=True)
    print(f"結果CSV出力先          : {csv_path}", flush=True)
    if args.export_diff:
        print(f"差分マップ保存先       : {'元フォルダ直下 (in-place)' if args.diff_in_place else diff_output_base}", flush=True)

    all_candidate_dirs = []
    for root, dirs, _ in os.walk(base_dataset_dir):
        rel_parts = os.path.relpath(root, base_dataset_dir).split(os.sep)
        if "GT" in rel_parts or "DiffMaps" in rel_parts or "RuleOptimizationResults" in rel_parts:
            continue
        if "Left" in dirs and "Right" in dirs:
            rel_path = os.path.relpath(root, base_dataset_dir).replace("\\", "/")
            all_candidate_dirs.append(rel_path)

    all_candidate_dirs.sort(key=natural_sort_key)

    if not all_candidate_dirs:
        print("エラー: 評価対象フォルダ (Left/Right を含むフォルダ) が見つかりませんでした。", flush=True)
        return

    if condition_filter:
        pattern = condition_filter.lower()
        candidate_dirs = [d for d in all_candidate_dirs if pattern in d.lower()]
        print(f"フィルタ適用: '{condition_filter}' (一致: {len(candidate_dirs)} / 全 {len(all_candidate_dirs)} 件)", flush=True)
        if not candidate_dirs:
            print(f"警告: キーワード '{condition_filter}' に一致するフォルダが見つかりませんでした。", flush=True)
            return
    else:
        candidate_dirs = all_candidate_dirs

    print(f"\n評価対象フォルダ数: {len(candidate_dirs)} 件", flush=True)
    print("-" * 135, flush=True)
    print(f"{'条件名 / 評価フォルダ':<45} | {'両眼合算IoU':<10} | {'左目IoU':<8} | {'右目IoU':<8} | {'両眼平均MAE':<10} | {'両眼平均PSNR':<11} | {'両眼平均DSSIM':<12}", flush=True)
    print("-" * 135, flush=True)

    csv_rows = []

    for cond in candidate_dirs:
        test_dir = os.path.join(base_dataset_dir, cond)
        target_gt_dir = resolve_gt_dir(test_dir, base_dataset_dir)

        if not target_gt_dir:
            print(f"{cond:<45} | [GTが見つかりません]", flush=True)
            continue

        res = evaluate_stereo_pair(target_gt_dir, test_dir, threshold=args.threshold)
        if not res:
            print(f"{cond:<45} | [画像が見つかりません/読み込み失敗]", flush=True)
            continue

        b_iou_tot = res["BinocularTotal"]["IoU"]
        l_iou = res["Left"]["IoU"]
        r_iou = res["Right"]["IoU"]
        b_mae = res["BinocularMean"]["MAE"]
        b_psnr = res["BinocularMean"]["PSNR"]
        b_dssim = res["BinocularMean"]["DSSIM"]

        psnr_str = f"{b_psnr:.2f} dB" if b_psnr != float('inf') else "inf dB"
        print(f"{cond:<45} | {b_iou_tot*100:>8.2f}% | {l_iou*100:>6.2f}% | {r_iou*100:>6.2f}% | {b_mae:>10.2f} | {psnr_str:>11} | {b_dssim:>12.4f}", flush=True)

        # 差分マップ生成
        if args.export_diff:
            for eye in ["Left", "Right"]:
                gt_path = find_first_image(os.path.join(target_gt_dir, eye), is_gt=True)
                test_path = find_first_image(os.path.join(test_dir, eye), is_gt=False)
                if gt_path and test_path:
                    gt_rgb = load_rgb_image(gt_path)
                    test_rgb = load_rgb_image(test_path)
                    vis = OcclusionDiffVisualizer(gt_rgb, test_rgb, threshold=args.threshold)
                    diff_mask = vis.generate_diff_mask()
                    diff_bgr = cv2.cvtColor(diff_mask, cv2.COLOR_RGB2BGR)

                    if args.diff_in_place:
                        out_dir = os.path.join(test_dir, eye)
                        out_name = f"diff_mask_{eye.lower()}.png"
                    else:
                        out_dir = os.path.join(diff_output_base, cond)
                        out_name = f"diff_mask_{eye.lower()}.png"
                    os.makedirs(out_dir, exist_ok=True)
                    cv2.imwrite(os.path.join(out_dir, out_name), diff_bgr)

        meta = parse_condition_metadata(cond, test_dir)

        csv_rows.append({
            "Condition_Path": cond,
            "Fixed_Mode": meta["Fixed_Mode"],
            "Density_Value": meta["Density_Value"],
            "Density_Unit": meta["Density_Unit"],
            "Sector": meta["Sector"],
            "Max_Consecutive_Zeros": meta["Max_Consecutive_Zeros"],
            "Threshold": meta["Threshold"],

            "Bino_Total_IoU%": round(b_iou_tot * 100, 2),
            "Bino_Total_誤遮蔽率(Union基準)%": round(res["BinocularTotal"]["OverOcc_wrt_Union"] * 100, 2),
            "Bino_Total_誤透過率(Union基準)%": round(res["BinocularTotal"]["UnderOcc_wrt_Union"] * 100, 2),
            "Bino_Total_過剰遮蔽率(GT可視基準)%": round(res["BinocularTotal"]["OverOcc_wrt_GT_Visible"] * 100, 2),

            "Left_IoU%": round(l_iou * 100, 2),
            "Left_誤遮蔽画素数(FN)": int(res["Left"]["FN_pixels"]),
            "Left_誤透過画素数(FP)": int(res["Left"]["FP_pixels"]),
            "Left_正解可視画素数(TP)": int(res["Left"]["TP_pixels"]),

            "Right_IoU%": round(r_iou * 100, 2),
            "Right_誤遮蔽画素数(FN)": int(res["Right"]["FN_pixels"]),
            "Right_誤透過画素数(FP)": int(res["Right"]["FP_pixels"]),
            "Right_正解可視画素数(TP)": int(res["Right"]["TP_pixels"]),

            "Bino_Mean_PSNR": round(b_psnr, 2) if b_psnr != float('inf') else "inf",
            "Bino_Mean_MAE": round(b_mae, 4),
            "Bino_Mean_DSSIM": round(b_dssim, 4)
        })

    print("-" * 135, flush=True)

    if csv_rows:
        os.makedirs(os.path.dirname(csv_path), exist_ok=True)
        with open(csv_path, mode="w", newline="", encoding="utf-8-sig") as f:
            writer = csv.DictWriter(f, fieldnames=list(csv_rows[0].keys()))
            writer.writeheader()
            writer.writerows(csv_rows)
        print(f"\n[完了] 集計結果をCSVに出力しました: {csv_path}", flush=True)


if __name__ == "__main__":
    main()
