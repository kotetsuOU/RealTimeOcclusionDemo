"""
SICE SI 2026: 占有数・方向判定における差分画素（追加遮蔽画素）のGT突合・可視化スクリプト

機能:
  2つの条件 (例: (5,8) vs (4,3)) のレンダリング画像を比較し、
  「変更前は非遮蔽だったが、変更後に新しく遮蔽された画素」を抽出。
  GT画像と突き合わせて、
    - 正しい追加遮蔽 (GTも遮蔽): シアン (0, 255, 255) または 緑
    - 誤った追加遮蔽 (GTは可視 / 過剰遮蔽): マゼンタ (255, 0, 255) または 赤
  として画面上にハイライト重畳描画した画像を生成・保存します。
  また、該当画素の座標・GT判定・理論パラメータを含む CSV を出力します。

使用例:
  python visualize_sector_differential.py
  python visualize_sector_differential.py --dataset-dir C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset/Bouchiba_NoiseOff_ProposeTest --density 4.0
"""

import os
import sys
import argparse
import csv
import cv2
import numpy as np

# Windows コンソール文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

def parse_args():
    parser = argparse.ArgumentParser(description="Visualize differential pixels between two sector conditions.")
    parser.add_argument("--dataset-dir", type=str,
                        default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\Bouchiba_NoiseOff_ProposeTest",
                        help="Path to the dataset directory containing ConsecutiveSweep and GT folders.")
    parser.add_argument("--density", type=str, default="4.0",
                        help="Density string to match subfolder (e.g. '4.0').")
    parser.add_argument("--old-cond", type=str, default="sector_5_maxzero_8",
                        help="Baseline condition folder name (e.g. sector_5_maxzero_8).")
    parser.add_argument("--new-cond", type=str, default="sector_4_maxzero_3",
                        help="New condition folder name (e.g. sector_4_maxzero_3).")
    parser.add_argument("--eye", type=str, default="Right", choices=["Left", "Right", "Both"],
                        help="Eye view to analyze ('Left', 'Right', or 'Both').")
    parser.add_argument("--out-dir", type=str, default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\Pattern_Debug",
                        help="Output directory for diff images and CSV.")
    return parser.parse_args()

def find_density_folder(consecutive_dir: str, density_str: str) -> str:
    subdirs = [d for d in os.listdir(consecutive_dir) if os.path.isdir(os.path.join(consecutive_dir, d))]
    for d in subdirs:
        if density_str in d:
            return os.path.join(consecutive_dir, d)
    # フォールバック
    for d in subdirs:
        if d.startswith("density_"):
            return os.path.join(consecutive_dir, d)
    raise FileNotFoundError(f"Density folder matching '{density_str}' not found in {consecutive_dir}")

def process_single_eye(eye: str, density_folder: str, gt_dir: str, old_cond: str, new_cond: str, out_dir: str):
    # 画像ファイル検索
    old_folder = os.path.join(density_folder, old_cond, eye)
    new_folder = os.path.join(density_folder, new_cond, eye)
    gt_eye_dir = os.path.join(gt_dir, eye)

    def find_first_png(d):
        pngs = [f for f in os.listdir(d) if f.lower().endswith(".png")]
        if not pngs:
            raise FileNotFoundError(f"No PNG found in {d}")
        return os.path.join(d, pngs[0])

    old_path = find_first_png(old_folder)
    new_path = find_first_png(new_folder)
    gt_path = find_first_png(gt_eye_dir)

    print(f"[{eye} Eye]")
    print(f"  Old Condition: {old_path}")
    print(f"  New Condition: {new_path}")
    print(f"  Ground Truth:  {gt_path}")

    old_img = cv2.imread(old_path, cv2.IMREAD_COLOR)
    new_img = cv2.imread(new_path, cv2.IMREAD_COLOR)
    gt_img  = cv2.imread(gt_path,  cv2.IMREAD_COLOR)

    if old_img is None or new_img is None or gt_img is None:
        raise ValueError("Failed to load one or more images.")

    # 可視マスク (RGB > 0)
    old_vis = np.any(old_img > 0, axis=-1)
    new_vis = np.any(new_img > 0, axis=-1)
    gt_vis  = np.any(gt_img > 0, axis=-1)

    # 追加遮蔽画素: old_vis (非遮蔽) かつ not new_vis (遮蔽)
    newly_occluded = old_vis & (~new_vis)
    total_newly_occ = int(np.sum(newly_occluded))

    # 正しい追加遮蔽 (GTも遮蔽): newly_occluded かつ not gt_vis
    correct_occ = newly_occluded & (~gt_vis)
    correct_count = int(np.sum(correct_occ))

    # 誤った追加遮蔽 (GTは可視 / 過剰遮蔽): newly_occluded かつ gt_vis
    erroneous_occ = newly_occluded & gt_vis
    erroneous_count = int(np.sum(erroneous_occ))

    print(f"  -> Total Newly Occluded: {total_newly_occ}")
    print(f"     [Correct]   GT is Occluded (Under-occlusion prevented): {correct_count}")
    print(f"     [Erroneous] GT is Visible (Over-occlusion created):      {erroneous_count}")

    # 可視化画像の生成
    # 背景として、視認しやすいように明度を抑えたグレースケールベースを作成
    gray_bg = cv2.cvtColor(gt_img, cv2.COLOR_BGR2GRAY)
    canvas = cv2.cvtColor(gray_bg // 2, cv2.COLOR_GRAY2BGR)

    # 仮想物体の輪郭を薄く表示 (参照用)
    canvas[gt_vis] = (canvas[gt_vis] * 0.5 + np.array([40, 40, 40])).astype(np.uint8)

    # 正しい追加遮蔽を シアン (BGR: 255, 255, 0)
    canvas[correct_occ] = [255, 255, 0]

    # 誤った追加遮蔽を マゼンタ (BGR: 255, 0, 255)
    canvas[erroneous_occ] = [255, 0, 255]

    # 拡大表示でも目立つように、該当画素をわずかに膨張させたオーバーレイも作成
    overlay_dilated = canvas.copy()
    kernel = np.ones((3, 3), np.uint8)
    corr_dilated = cv2.dilate(correct_occ.astype(np.uint8), kernel) > 0
    err_dilated  = cv2.dilate(erroneous_occ.astype(np.uint8), kernel) > 0
    overlay_dilated[corr_dilated] = [255, 255, 0]
    overlay_dilated[err_dilated]  = [255, 0, 255]

    os.makedirs(out_dir, exist_ok=True)
    out_img_path = os.path.join(out_dir, f"diff_{old_cond}_to_{new_cond}_{eye}.png")
    out_dilated_path = os.path.join(out_dir, f"diff_{old_cond}_to_{new_cond}_{eye}_highlight.png")
    cv2.imwrite(out_img_path, canvas)
    cv2.imwrite(out_dilated_path, overlay_dilated)
    print(f"  -> Saved differential image: {out_img_path}")
    print(f"  -> Saved highlighted image:    {out_dilated_path}")

    # CSV出力
    csv_rows = []
    y_indices, x_indices = np.where(newly_occluded)
    for y, x in zip(y_indices, x_indices):
        is_correct = bool(correct_occ[y, x])
        csv_rows.append({
            "eye": eye,
            "pixel_x": int(x),
            "pixel_y": int(y),
            "status": "Correct_Additional_Occlusion" if is_correct else "Erroneous_Over_Occlusion",
            "gt_visible": int(gt_vis[y, x]),
            "gt_occluded": int(not gt_vis[y, x]),
            "old_cond_visible": int(old_vis[y, x]),
            "new_cond_visible": int(new_vis[y, x]),
            "theoretical_N_occ": 4,
            "theoretical_L_max": 3
        })

    return csv_rows, total_newly_occ, correct_count, erroneous_count

def main():
    args = parse_args()
    consecutive_dir = os.path.join(args.dataset_dir, "ConsecutiveSweep")
    gt_dir = os.path.join(args.dataset_dir, "GT")
    if not os.path.exists(gt_dir):
        # 共通 GT フォールバック
        gt_dir = os.path.join(os.path.dirname(args.dataset_dir), "GT")

    density_folder = find_density_folder(consecutive_dir, args.density)
    print(f"Dataset Dir:    {args.dataset_dir}")
    print(f"Density Folder: {density_folder}")
    print(f"Comparison:     {args.old_cond} -> {args.new_cond}")

    eyes_to_process = ["Left", "Right"] if args.eye == "Both" else [args.eye]
    all_rows = []

    for eye in eyes_to_process:
        rows, total, corr, err = process_single_eye(eye, density_folder, gt_dir, args.old_cond, args.new_cond, args.out_dir)
        all_rows.extend(rows)

    if all_rows:
        csv_path = os.path.join(args.out_dir, f"newly_occluded_pixels_{args.old_cond}_to_{args.new_cond}.csv")
        with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:
            writer = csv.DictWriter(f, fieldnames=list(all_rows[0].keys()))
            writer.writeheader()
            writer.writerows(all_rows)
        print(f"Saved pixel record CSV: {csv_path}")
        print(f"Total analyzed newly occluded pixels: {len(all_rows)}")

if __name__ == "__main__":
    main()
