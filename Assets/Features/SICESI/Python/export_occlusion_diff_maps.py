"""
================================================================================
SICE SI 2026: オクルージョン定性評価用 差分可視化スクリプト
(Feature: SICESI - Difference & Error Map Generator)
================================================================================

【目的】
  GT (正解手メッシュ画像) と Test (実測レンダリング画像) を画素比較し、
  過剰遮蔽 (赤: FN) および 遮蔽漏れ (青: FP) を色分け可視化した差分マップを生成します。

【実行例】
  python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba
  python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba --mode montage
  python Assets/Features/SICESI/Python/export_occlusion_diff_maps.py -c Bouchiba --in-place
"""

import os
import sys
import argparse
import cv2

# Windows コンソール文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

from sicesi_core.constants import (
    COLOR_CORRECT, COLOR_OCC_MATCH, COLOR_OVER_OCC, COLOR_UNDER_OCC, COLOR_BG,
    DEFAULT_BINARY_THRESHOLD
)
from sicesi_core.visualizer import OcclusionDiffVisualizer
from sicesi_core.dataset_loader import (
    natural_sort_key, find_first_image, resolve_gt_dir
)


def parse_args():
    parser = argparse.ArgumentParser(
        description="SICE SI 2026: オクルージョン定性評価用 差分可視化マップ生成スクリプト"
    )
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_dataset = os.path.abspath(os.path.join(script_dir, "../../../../../Estimation/SICESI_Dataset"))

    parser.add_argument(
        "dataset_dir",
        nargs="?",
        default=default_dataset,
        help="データセットディレクトリ"
    )
    parser.add_argument(
        "-c", "--condition", "--filter", "-f",
        dest="condition_filter",
        default=None,
        help="評価対象とする条件名の絞り込みキーワード (例: -c Bouchiba, -c density_1.00)"
    )
    parser.add_argument(
        "-o", "--output-dir",
        dest="output_dir",
        default=None,
        help="差分マップの出力先ベースディレクトリ (省略時かつ --in-place なしの場合はデータセット直下の DiffMaps フォルダ)"
    )
    parser.add_argument(
        "--in-place",
        action="store_true",
        dest="in_place",
        help="元のフォルダ構造を崩さず、各評価対象の Left/Right フォルダ直下に差分画像を直接出力する (推奨)"
    )
    parser.add_argument(
        "-m", "--mode",
        dest="mode",
        choices=["all", "mask", "overlay", "montage"],
        default="all",
        help="出力する画像の種類 (all: 全て, mask: 差分マスクのみ, overlay: オーバーレイのみ, montage: 4ペイン横連結のみ)"
    )
    parser.add_argument(
        "--sides",
        dest="sides",
        choices=["left", "right", "both"],
        default="both",
        help="出力対象の眼 (left, right, both)"
    )
    parser.add_argument(
        "-th", "--threshold",
        dest="threshold",
        type=int,
        default=DEFAULT_BINARY_THRESHOLD,
        help="二値化判定の閾値 (0-255, デフォルト: 128。パイプライン一貫性のための50%%カバレッジ基準)"
    )
    return parser.parse_args()


def main():
    args = parse_args()
    base_dataset_dir = os.path.abspath(args.dataset_dir)
    condition_filter = args.condition_filter

    if args.output_dir:
        base_output_dir = os.path.abspath(args.output_dir)
    else:
        base_output_dir = os.path.join(base_dataset_dir, "DiffMaps")

    print("=== SICE SI オクルージョン定性評価 差分マップ生成 ===", flush=True)
    print(f"データセットディレクトリ: {base_dataset_dir}", flush=True)
    print(f"差分マップ保存先       : {base_output_dir}", flush=True)
    print(f"二値化判定閾値         : {args.threshold} (50%カバレッジ基準)", flush=True)
    print(f"出力モード             : {args.mode}", flush=True)
    print(f"対象眼                 : {args.sides}", flush=True)

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
        print("エラー: 評価対象フォルダ (Left/Right を含むフォルダ) が見つかりません。", flush=True)
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

    print(f"\n処理対象条件数: {len(candidate_dirs)} 件\n", flush=True)

    eyes_to_process = []
    if args.sides in ("left", "both"):
        eyes_to_process.append("Left")
    if args.sides in ("right", "both"):
        eyes_to_process.append("Right")

    success_count = 0

    for idx, cond in enumerate(candidate_dirs):
        test_dir = os.path.join(base_dataset_dir, cond)
        target_gt_dir = resolve_gt_dir(test_dir, base_dataset_dir)

        if not target_gt_dir:
            print(f"[{idx+1}/{len(candidate_dirs)}] スキップ (GTなし): {cond}", flush=True)
            continue

        if args.in_place:
            print(f"[{idx+1}/{len(candidate_dirs)}] 差分マップ生成中 (元フォルダ直下に出力): {cond}", flush=True)
        else:
            cond_out_dir = os.path.join(base_output_dir, cond)
            os.makedirs(cond_out_dir, exist_ok=True)
            print(f"[{idx+1}/{len(candidate_dirs)}] 差分マップ生成中: {cond}", flush=True)

        for eye in eyes_to_process:
            gt_eye_dir = os.path.join(target_gt_dir, eye)
            test_eye_dir = os.path.join(test_dir, eye)

            gt_img_path = find_first_image(gt_eye_dir, is_gt=True)
            test_img_path = find_first_image(test_eye_dir, is_gt=False)

            if not (gt_img_path and test_img_path):
                print(f"  - {eye}: 画像が見つかりません (GT: {gt_img_path}, Test: {test_img_path})", flush=True)
                continue

            gt_bgr = cv2.imread(gt_img_path, cv2.IMREAD_COLOR)
            test_bgr = cv2.imread(test_img_path, cv2.IMREAD_COLOR)

            if gt_bgr is None or test_bgr is None:
                continue

            gt_rgb = cv2.cvtColor(gt_bgr, cv2.COLOR_BGR2RGB)
            test_rgb = cv2.cvtColor(test_bgr, cv2.COLOR_BGR2RGB)

            vis = OcclusionDiffVisualizer(gt_rgb, test_rgb, threshold=args.threshold)
            stats = vis.get_statistics()

            title = f"{cond} [{eye}]"

            if args.in_place:
                save_dir = test_eye_dir
                prefix = "diff_"
            else:
                save_dir = cond_out_dir
                prefix = f"diff_{eye.lower()}_"

            # 1) 差分マスク
            if args.mode in ("all", "mask"):
                diff_mask = vis.generate_diff_mask()
                diff_bgr = cv2.cvtColor(diff_mask, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}mask.png") if args.in_place else os.path.join(save_dir, f"diff_mask_{eye.lower()}.png")
                cv2.imwrite(out_path, diff_bgr)

            # 2) オーバーレイ
            if args.mode in ("all", "overlay"):
                overlay = vis.generate_overlay()
                overlay_bgr = cv2.cvtColor(overlay, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}overlay.png") if args.in_place else os.path.join(save_dir, f"diff_overlay_{eye.lower()}.png")
                cv2.imwrite(out_path, overlay_bgr)

            # 3) モンタージュ
            if args.mode in ("all", "montage"):
                montage = vis.generate_montage(title=title)
                montage_bgr = cv2.cvtColor(montage, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}montage.png") if args.in_place else os.path.join(save_dir, f"diff_montage_{eye.lower()}.png")
                cv2.imwrite(out_path, montage_bgr)

            print(f"  - {eye:<5}: IoU={stats['IoU']:.4f} | 誤遮蔽(赤)={stats['FN_pixels']:,}px ({stats['OverOccRate']*100:.1f}%) | 誤透過(青)={stats['FP_pixels']:,}px ({stats['UnderOccRate']*100:.1f}%)", flush=True)

        success_count += 1

    print(f"\n[完了] {success_count} 件の条件の差分マップを保存しました: {base_output_dir}", flush=True)


if __name__ == "__main__":
    main()
