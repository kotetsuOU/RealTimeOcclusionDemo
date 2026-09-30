"""
================================================================================
セクター占有マスク・GPU実遮蔽真値マスク・テスト画像の整合性自動検証ツール
(Feature: SICESI - Sector Mask & GPU Occlusion Truth Verification)
================================================================================
【概要】
  最新の画面保存手法で取得されたデータセットディレクトリを走査し、
  以下の比較と整合性評価を自動実行してレポートします：
  1. 統合 8-bit パターンマスク (sector_mask_*.png) による各ルール予測
  2. GPU 実遮蔽判定マスク (gpu_occluded_mask_*.png)
  3. 実測 Test カメラ画像 (test_*_*.png)
  4. Ground Truth 手メッシュ遮蔽画像 (gt_*.png)

  特に、export_occlusion_diff_maps と同じ原理で、
  「GPU遮蔽真値 vs 実測 Test 画像」の画素単位の不一致を色分けした
  4ペインモンタージュ [GPU Truth | Test | Diff Mask | Overlay] を
  生成し、画面上にインタラクティブ表示およびファイル保存します：
    - 赤色 (Red: [255, 45, 45])   : 過剰遮蔽 (Over-occlusion / GPU真値は可視なのにTestで遮蔽)
    - 青色 (Blue: [30, 144, 255]) : 遮蔽漏れ (Under-occlusion / GPU真値は遮蔽なのにTestで透過)
    - 薄灰 (Gray: [200, 200, 200]): 正しく可視 (Both Visible / GPU真値・Testともに可視)
    - 暗灰 (Dark: [45, 45, 45])   : 正しく遮蔽 (Both Occluded / GPU真値・Testともに遮蔽)

【実行例】
  # 1) 通常実行（GPU真値 vs Test の差分画像を表示 + 自動保存）
  python Assets/Features/SICESI/Python/verify_sector_masks.py

  # 2) 特定の条件（例: density_1.0）のみ検証・表示
  python Assets/Features/SICESI/Python/verify_sector_masks.py -c density_1.0

  # 3) GT (手メッシュ) vs Test の差分マップを表示したい場合
  python Assets/Features/SICESI/Python/verify_sector_masks.py --target gt

  # 4) GUIウィンドウを開かず画像保存と数値レポートのみ実行 (バッチ処理向け)
  python Assets/Features/SICESI/Python/verify_sector_masks.py --no-show
================================================================================
"""

import os
import sys
import glob
import re
import argparse
import numpy as np
from PIL import Image

# Windowsコンソールでの文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import cv2

from sicesi_core.constants import (
    COLOR_CORRECT, COLOR_OCC_MATCH, COLOR_OVER_OCC, COLOR_UNDER_OCC, COLOR_BG,
    DEFAULT_BINARY_THRESHOLD
)
from sicesi_core.dataset_loader import natural_sort_key


def generate_gpu_diff_montage(gpu_occ_mask: np.ndarray, test_rgb: np.ndarray, vo_mask: np.ndarray,
                              gt_rgb: np.ndarray = None, title: str = "",
                              threshold: int = DEFAULT_BINARY_THRESHOLD) -> tuple:
    """
    【GPU真値 vs 実測 Test 画像】の差分マップおよび 4ペイン横連結モンタージュを生成します。
    [ GPU Truth (Reference) | Test (Result) | Diff Mask | Overlay ]
    """
    h, w = test_rgb.shape[:2]

    # GPU真値判定 (VO内)
    gpu_occ = gpu_occ_mask & vo_mask
    gpu_vis = vo_mask & (~gpu_occ)

    # 実測 Test 判定 (VO内: 統一閾値 threshold に基づく二値化)
    test_vis = (test_rgb[:, :, 0] > threshold) & vo_mask
    test_occ = vo_mask & (~test_vis)

    # 4分割論理判定
    both_vis  = gpu_vis & test_vis       # 正しく可視 (Both Visible)
    both_occ  = gpu_occ & test_occ       # 正しく遮蔽 (Both Occluded)
    over_occ  = gpu_vis & test_occ       # 過剰遮蔽 (Over-occlusion / 赤: GPU真値は可視なのにTestで遮蔽)
    under_occ = gpu_occ & test_vis       # 遮蔽漏れ (Under-occlusion / 青: GPU真値は遮蔽なのにTestで透過)

    total_vo = int(np.sum(vo_mask))
    match_px = int(np.sum(both_vis | both_occ))
    diff_px  = int(np.sum(over_occ | under_occ))
    over_px  = int(np.sum(over_occ))
    under_px = int(np.sum(under_occ))

    match_rate = (match_px / total_vo * 100.0) if total_vo > 0 else 100.0
    diff_rate  = (diff_px / total_vo * 100.0) if total_vo > 0 else 0.0
    over_rate  = (over_px / total_vo * 100.0) if total_vo > 0 else 0.0
    under_rate = (under_px / total_vo * 100.0) if total_vo > 0 else 0.0

    # 1) パネル1: GPU Truth (Reference Rendering)
    # 実測画像やGT画像の色味を参照し、GPU真値通りに遮蔽されたVO画像を作成
    gpu_ref_rgb = np.full((h, w, 3), COLOR_BG, dtype=np.uint8)
    if gt_rgb is not None:
        vis_colors = np.where(test_rgb[:, :, 0:1] > threshold, test_rgb, gt_rgb)
    else:
        vis_colors = test_rgb
    gpu_ref_rgb[gpu_vis] = vis_colors[gpu_vis]
    # 遮蔽領域の輪郭をうっすら暗色で表現
    gpu_ref_rgb[gpu_occ] = COLOR_OCC_MATCH

    # 2) パネル2: Test (Result)
    panel_test = test_rgb

    # 3) パネル3: 差分マスク (Diff Mask)
    diff_mask = np.full((h, w, 3), COLOR_BG, dtype=np.uint8)
    diff_mask[both_occ]  = COLOR_OCC_MATCH
    diff_mask[both_vis]  = COLOR_CORRECT
    diff_mask[over_occ]  = COLOR_OVER_OCC
    diff_mask[under_occ] = COLOR_UNDER_OCC

    # 4) パネル4: オーバーレイ画像 (Overlay)
    alpha = 0.65
    overlay = test_rgb.copy()
    if over_px > 0:
        # 過剰遮蔽（消えてしまった領域）: 本来見えるはずだった色調に赤をブレンドして補完表示
        ref_area = vis_colors[over_occ].astype(np.float32)
        if np.any(np.all(ref_area <= 10, axis=-1)):
            ref_area[np.all(ref_area <= 10, axis=-1)] = [200, 200, 200]
        red_tint = np.full_like(ref_area, COLOR_OVER_OCC, dtype=np.float32)
        overlay[over_occ] = (ref_area * (1.0 - alpha) + red_tint * alpha).clip(0, 255).astype(np.uint8)
    if under_px > 0:
        # 遮蔽漏れ（透けてしまった領域）: 実測描画の上に青色をブレンド
        test_area = test_rgb[under_occ].astype(np.float32)
        blue_tint = np.full_like(test_area, COLOR_UNDER_OCC, dtype=np.float32)
        overlay[under_occ] = (test_area * (1.0 - alpha) + blue_tint * alpha).clip(0, 255).astype(np.uint8)

    # 4ペインモンタージュの描画
    panels = [gpu_ref_rgb, panel_test, diff_mask, overlay]
    labels = ["GPU Truth (Reference)", "Test (Result)", "Diff Mask", "Overlay"]

    scale = max(1.0, w / 1280.0)
    header_h = int(60 * scale)
    footer_h = int(80 * scale)
    total_w = w * 4
    total_h = h + header_h + footer_h

    canvas = np.zeros((total_h, total_w, 3), dtype=np.uint8)

    for i, (pan, lbl) in enumerate(zip(panels, labels)):
        xs = i * w
        canvas[header_h:header_h + h, xs:xs + w] = pan

        # パネル上部ラベル
        cv2.putText(canvas, lbl, (xs + int(25 * scale), int(42 * scale)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.9 * scale, (230, 230, 230), max(1, int(2 * scale)), cv2.LINE_AA)
        if i > 0:
            cv2.line(canvas, (xs, 0), (xs, total_h), (70, 70, 70), max(1, int(1 * scale)))

    # タイトル
    if title:
        cv2.putText(canvas, f"[GPU Truth vs Test] {title}", (total_w - int(1700 * scale), int(42 * scale)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.85 * scale, (180, 215, 255), max(1, int(2 * scale)), cv2.LINE_AA)

    chip_y = header_h + h + int(24 * scale)
    chip_size = int(28 * scale)
    footer_y = header_h + h + int(48 * scale)

    # 赤凡例: 過剰遮蔽
    x1 = int(40 * scale)
    cv2.rectangle(canvas, (x1, chip_y), (x1 + chip_size, chip_y + chip_size), [int(c) for c in COLOR_OVER_OCC], -1)
    cv2.putText(canvas, f"Over-occlusion (赤 / GPU可視・Test遮蔽): {over_px:,} px ({over_rate:.2f}%)",
                (x1 + chip_size + int(12 * scale), footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.75 * scale, (255, 130, 130), max(1, int(2 * scale)), cv2.LINE_AA)

    # 青凡例: 遮蔽漏れ
    x2 = int(1150 * scale)
    cv2.rectangle(canvas, (x2, chip_y), (x2 + chip_size, chip_y + chip_size), [int(c) for c in COLOR_UNDER_OCC], -1)
    cv2.putText(canvas, f"Under-occlusion (青 / GPU遮蔽・Test可視): {under_px:,} px ({under_rate:.2f}%)",
                (x2 + chip_size + int(12 * scale), footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.75 * scale, (110, 190, 255), max(1, int(2 * scale)), cv2.LINE_AA)

    # 一致率
    x3 = int(2250 * scale)
    cv2.putText(canvas, f"Match: {match_rate:.4f}%  (Diff: {diff_px:,} px / {diff_rate:.2f}%)",
                (x3, footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.9 * scale, (0, 255, 180), max(1, int(2 * scale)), cv2.LINE_AA)

    stats = {
        "target": "gpu_vs_test",
        "match_rate": match_rate,
        "match_px": match_px,
        "diff_px": diff_px,
        "over_px": over_px,
        "under_px": under_px,
        "over_rate": over_rate,
        "under_rate": under_rate,
        "total_vo": total_vo
    }
    return canvas, stats


def generate_gt_diff_montage(gt_rgb: np.ndarray, test_rgb: np.ndarray, vo_mask: np.ndarray,
                             title: str = "", extra_info: str = "",
                             threshold: int = DEFAULT_BINARY_THRESHOLD) -> tuple:
    """
    【GT (手メッシュ) vs 実測 Test 画像】の差分マップおよび 4ペイン横連結モンタージュを生成します。
    [ GT (Reference) | Test (Result) | Error Mask | Overlay ]
    """
    h, w = gt_rgb.shape[:2]

    # VO領域内での可視判定 (統一閾値 threshold に基づく二値化)
    gt_vis = (gt_rgb[:, :, 0] > threshold) & vo_mask
    test_vis = (test_rgb[:, :, 0] > threshold) & vo_mask

    tp_mask = gt_vis & test_vis        # 正しく表示
    fn_mask = gt_vis & (~test_vis)     # 誤遮蔽 (Over-occlusion) - 赤
    fp_mask = (~gt_vis) & test_vis     # 誤透過 (Under-occlusion) - 青
    tn_mask = (~gt_vis) & (~test_vis)  # 正しく遮蔽 / 背景

    tp_px = int(np.sum(tp_mask))
    fn_px = int(np.sum(fn_mask))
    fp_px = int(np.sum(fp_mask))
    union_px = tp_px + fn_px + fp_px

    iou = (tp_px / union_px * 100.0) if union_px > 0 else 0.0
    fn_rate = (fn_px / union_px * 100.0) if union_px > 0 else 0.0
    fp_rate = (fp_px / union_px * 100.0) if union_px > 0 else 0.0

    diff_mask = np.full((h, w, 3), COLOR_BG, dtype=np.uint8)
    diff_mask[tp_mask] = COLOR_CORRECT
    diff_mask[fn_mask] = COLOR_OVER_OCC
    diff_mask[fp_mask] = COLOR_UNDER_OCC

    alpha = 0.65
    overlay = test_rgb.copy()
    if np.any(fn_mask):
        gt_area = gt_rgb[fn_mask].astype(np.float32)
        red_tint = np.full_like(gt_area, COLOR_OVER_OCC, dtype=np.float32)
        overlay[fn_mask] = (gt_area * (1.0 - alpha) + red_tint * alpha).clip(0, 255).astype(np.uint8)
    if np.any(fp_mask):
        test_area = test_rgb[fp_mask].astype(np.float32)
        blue_tint = np.full_like(test_area, COLOR_UNDER_OCC, dtype=np.float32)
        overlay[fp_mask] = (test_area * (1.0 - alpha) + blue_tint * alpha).clip(0, 255).astype(np.uint8)

    panels = [gt_rgb, test_rgb, diff_mask, overlay]
    labels = ["GT (Reference)", "Test (Result)", "Error Mask", "Overlay"]

    scale = max(1.0, w / 1280.0)
    header_h = int(60 * scale)
    footer_h = int(80 * scale)
    total_w = w * 4
    total_h = h + header_h + footer_h

    canvas = np.zeros((total_h, total_w, 3), dtype=np.uint8)

    for i, (pan, lbl) in enumerate(zip(panels, labels)):
        xs = i * w
        canvas[header_h:header_h + h, xs:xs + w] = pan
        cv2.putText(canvas, lbl, (xs + int(25 * scale), int(42 * scale)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.9 * scale, (230, 230, 230), max(1, int(2 * scale)), cv2.LINE_AA)
        if i > 0:
            cv2.line(canvas, (xs, 0), (xs, total_h), (70, 70, 70), max(1, int(1 * scale)))

    if title:
        cv2.putText(canvas, f"[GT vs Test] {title}", (total_w - int(1500 * scale), int(42 * scale)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.85 * scale, (180, 215, 255), max(1, int(2 * scale)), cv2.LINE_AA)

    chip_y = header_h + h + int(24 * scale)
    chip_size = int(28 * scale)
    footer_y = header_h + h + int(48 * scale)

    # 赤凡例
    x1 = int(40 * scale)
    cv2.rectangle(canvas, (x1, chip_y), (x1 + chip_size, chip_y + chip_size), [int(c) for c in COLOR_OVER_OCC], -1)
    cv2.putText(canvas, f"Over-occlusion (FN / 誤遮蔽): {fn_px:,} px ({fn_rate:.2f}%)",
                (x1 + chip_size + int(12 * scale), footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.75 * scale, (255, 130, 130), max(1, int(2 * scale)), cv2.LINE_AA)

    # 青凡例
    x2 = int(1050 * scale)
    cv2.rectangle(canvas, (x2, chip_y), (x2 + chip_size, chip_y + chip_size), [int(c) for c in COLOR_UNDER_OCC], -1)
    cv2.putText(canvas, f"Under-occlusion (FP / 誤透過): {fp_px:,} px ({fp_rate:.2f}%)",
                (x2 + chip_size + int(12 * scale), footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.75 * scale, (110, 190, 255), max(1, int(2 * scale)), cv2.LINE_AA)

    # IoU
    x3 = int(2050 * scale)
    cv2.putText(canvas, f"Mask IoU: {iou:.4f}%",
                (x3, footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.9 * scale, (0, 255, 180), max(1, int(2 * scale)), cv2.LINE_AA)

    if extra_info:
        x4 = int(2700 * scale)
        cv2.putText(canvas, extra_info,
                    (x4, footer_y), cv2.FONT_HERSHEY_SIMPLEX, 0.75 * scale, (220, 220, 180), max(1, int(2 * scale)), cv2.LINE_AA)

    stats = {
        "target": "gt_vs_test",
        "iou": iou,
        "tp": tp_px,
        "fp": fp_px,
        "fn": fn_px,
        "union": union_px,
        "fn_rate": fn_rate,
        "fp_rate": fp_rate
    }
    return canvas, stats


def show_interactive_gallery(eval_results: list, auto_delay: int = 0):
    """
    OpenCVウィンドウを利用して、生成された差分モンタージュ画像を前後に切り替えながら閲覧します。
    [M] または [Tab] キーで GPU真値比較 ⇔ GT比較 をトグル切り替え可能です。
    """
    if not eval_results:
        return

    win_name = "SICE SI Occlusion Verification - Diff Map Viewer"
    try:
        cv2.namedWindow(win_name, cv2.WINDOW_NORMAL)
        cv2.resizeWindow(win_name, 1750, 360)
    except Exception as e:
        print(f"  [Notice] GUIウィンドウの初期化をスキップしました: {e}")
        return

    idx = 0
    total = len(eval_results)
    current_mode = "gpu"  # "gpu" or "gt"

    print("\n" + "=" * 80)
    print("【インタラクティブ差分画像ビューワ起動】")
    print("  [Space / Enter / → / N]: 次の条件へ")
    print("  [← / B / P]           : 前の条件へ戻る")
    print("  [M / Tab]             : 比較モード切替 (GPU真値 vs Test ⇔ GT vs Test)")
    print("  [ESC / Q]              : ビューワ終了")
    print("=" * 80 + "\n")

    while 0 <= idx < total:
        item = eval_results[idx]
        title = item["title"]

        # 選択されたモードの画像を取得（無ければもう片方）
        if current_mode == "gpu" and "canvas_gpu_bgr" in item:
            canvas_bgr = item["canvas_gpu_bgr"]
            mode_lbl = "GPU Truth vs Test"
        elif "canvas_gt_bgr" in item:
            canvas_bgr = item["canvas_gt_bgr"]
            mode_lbl = "GT vs Test"
        elif "canvas_gpu_bgr" in item:
            canvas_bgr = item["canvas_gpu_bgr"]
            mode_lbl = "GPU Truth vs Test"
        else:
            idx += 1
            continue

        h, w = canvas_bgr.shape[:2]
        preview_w = 1750
        preview_h = max(200, int(h * (preview_w / w)))
        preview_img = cv2.resize(canvas_bgr, (preview_w, preview_h), interpolation=cv2.INTER_AREA)

        # 操作ガイドバーをプレビュー上部にオーバーレイ
        guide_text = f"[{idx + 1}/{total}] [{mode_lbl}] {title}  |  [Space]: Next, [B]: Prev, [Tab/M]: Toggle Mode, [ESC]: Exit"
        cv2.putText(preview_img, guide_text, (20, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2, cv2.LINE_AA)

        cv2.imshow(win_name, preview_img)

        key = cv2.waitKey(auto_delay)
        if key == -1 and auto_delay > 0:
            idx += 1
            continue

        key = key & 0xFF
        if key in (27, ord('q'), ord('Q')):  # ESC or Q
            break
        elif key in (32, 13, ord('n'), ord('N'), 83):  # Space, Enter, N, Right Arrow
            idx += 1
        elif key in (8, ord('b'), ord('B'), ord('p'), ord('P'), 81):  # Backspace, B, P, Left Arrow
            idx = max(0, idx - 1)
        elif key in (9, ord('m'), ord('M')):  # Tab or M
            current_mode = "gt" if current_mode == "gpu" else "gpu"

    try:
        cv2.destroyAllWindows()
    except Exception:
        pass


def verify_dataset(root_dir: str, condition_filter: str = None, target_mode: str = "gpu",
                   show_ui: bool = True, save_images: bool = True,
                   save_dir_override: str = None, auto_delay: int = 0,
                   binary_thresh: int = DEFAULT_BINARY_THRESHOLD):
    sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Sector8MaskSweep"), recursive=True))
    if not sweep_dirs:
        if os.path.basename(root_dir) == "Sector8MaskSweep":
            sweep_dirs = [root_dir]
        else:
            sweep_dirs = [root_dir]

    bit_counts = np.array([bin(i).count('1') for i in range(256)], dtype=np.uint8)

    print("=" * 80)
    print(f"【SICE SI セクターマスク & GPU遮蔽真値 自動検証】")
    print(f"  探索対象: {root_dir}")
    if condition_filter:
        print(f"  絞り込み: '{condition_filter}'")
    print(f"  判定閾値: {binary_thresh} (パイプライン一貫性: 50%カバレッジ基準)")
    print(f"  比較対象: {'GPU真値 vs 実測 Test (推奨)' if target_mode == 'gpu' else ('GT vs 実測 Test' if target_mode == 'gt' else '両方 (GPU & GT)')}")
    print(f"  画像表示: {'有効 (インタラクティブ表示)' if show_ui else '無効 (コンソールのみ)'}")
    print(f"  差分保存: {'有効 (各条件フォルダ直下に diff_montage_*.png を保存)' if save_images else '無効'}")
    print("=" * 80)

    total_checked = 0
    eval_results = []

    for s_dir in sweep_dirs:
        dens_dirs = sorted(glob.glob(os.path.join(s_dir, "density_*pts_mm2")), key=natural_sort_key)
        for d_dir in dens_dirs:
            cond_name = f"{os.path.basename(s_dir)} / {os.path.basename(d_dir)}"
            if condition_filter and condition_filter.lower() not in cond_name.lower():
                continue

            dens_str = os.path.basename(d_dir).replace("density_", "").replace("pts_mm2", "")
            for eye in ["Left", "Right"]:
                eye_dir = os.path.join(d_dir, eye)
                if not os.path.exists(eye_dir):
                    continue

                # 必須ファイルの探索
                vo_candidates = [
                    os.path.join(s_dir, "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "GT", f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", f"vo_silhouette_{eye.lower()}.png")
                ]
                vo_path = next((c for c in vo_candidates if os.path.exists(c)), None)
                if not vo_path:
                    continue

                gt_candidates = [
                    os.path.join(s_dir, "GT", eye, f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "GT", f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", eye, f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", f"gt_{eye.lower()}.png")
                ]
                gt_path = next((c for c in gt_candidates if os.path.exists(c)), None)

                test_files = glob.glob(os.path.join(eye_dir, f"test_*_{eye.lower()}.png"))
                test_path = test_files[0] if test_files else None

                gpu_mask_path = os.path.join(eye_dir, f"gpu_occluded_mask_{eye.lower()}.png")
                unified_mask_path = os.path.join(eye_dir, f"sector_mask_{eye.lower()}.png")

                vo_img = np.array(Image.open(vo_path))[:, :, 0]
                vo_mask = (vo_img > binary_thresh)
                total_vo = np.sum(vo_mask)

                title_str = f"{os.path.basename(s_dir)} / {os.path.basename(d_dir)} [{eye}]"
                print(f"\n--- [{title_str}] ---")
                print(f"  VO シルエット画素数: {total_vo:,} pixels")

                # 画像読み込み
                test_rgb = None
                if test_path and os.path.exists(test_path):
                    test_bgr = cv2.imread(test_path)
                    if test_bgr is not None:
                        test_rgb = cv2.cvtColor(test_bgr, cv2.COLOR_BGR2RGB)

                gt_rgb = None
                if gt_path and os.path.exists(gt_path):
                    gt_bgr = cv2.imread(gt_path)
                    if gt_bgr is not None:
                        gt_rgb = cv2.cvtColor(gt_bgr, cv2.COLOR_BGR2RGB)

                # 占有マスクの読み込み
                occupied_mask = None
                mask_source = "未検出"
                sector_pngs = sorted(glob.glob(os.path.join(eye_dir, "sector_*_mask_*.png")), key=natural_sort_key)
                if len(sector_pngs) >= 8:
                    occupied_mask = np.zeros(vo_mask.shape, dtype=np.uint8)
                    for k in range(8):
                        p = next((x for x in sector_pngs if f"sector_{k}_mask" in os.path.basename(x)), None)
                        if p:
                            sec_m = (np.array(Image.open(p))[:, :, 0] > binary_thresh)
                            occupied_mask |= (sec_m.astype(np.uint8) << k)
                    mask_source = "個別8枚二値マスク (ガンマ歪みなし・高精度)"
                elif os.path.exists(unified_mask_path):
                    occupied_mask = np.array(Image.open(unified_mask_path))[:, :, 0]
                    mask_source = "統合1枚マスク (ガンマ歪みの影響あり)"

                # GPU 実遮蔽判定マスクの読み込みと評価
                gpu_occ = None
                item_eval = {"title": title_str}

                if os.path.exists(gpu_mask_path):
                    gpu_img = np.array(Image.open(gpu_mask_path))[:, :, 0]
                    gpu_occ = (gpu_img > binary_thresh) & vo_mask

                    if occupied_mask is not None:
                        print(f"  占有パターンソース: {mask_source}")
                        # 占有数 >= 6 での予測と比較 (デフォルトの SectorThreshold R=6)
                        occ_counts = bit_counts[occupied_mask]
                        for r in [6]:
                            pred_occ = (occ_counts >= r) & vo_mask
                            diff = np.sum((pred_occ != gpu_occ) & vo_mask)
                            match_pct = (1.0 - diff / total_vo) * 100.0
                            print(f"  占有パターン (Occ >= {r}) vs GPU真値: 一致率 {match_pct:.4f}% (不一致: {diff:,} 画素)")

                            if test_rgb is not None:
                                test_occ = vo_mask & (~(test_rgb[:, :, 0] > binary_thresh))
                                diff_p_test = np.sum((pred_occ != test_occ) & vo_mask)
                                match_p_test = (1.0 - diff_p_test / total_vo) * 100.0
                                print(f"  占有パターン (Occ >= {r}) vs 実測 Test 画像: 一致率 {match_p_test:.4f}% (不一致: {diff_p_test:,} 画素)")

                    # 【GPU真値 vs 実測 Test 画像】の比較と差分モンタージュ生成
                    if test_rgb is not None:
                        canvas_gpu_rgb, stats_gpu = generate_gpu_diff_montage(
                            gpu_occ, test_rgb, vo_mask, gt_rgb=gt_rgb, title=title_str, threshold=binary_thresh
                        )
                        canvas_gpu_bgr = cv2.cvtColor(canvas_gpu_rgb, cv2.COLOR_RGB2BGR)
                        item_eval["canvas_gpu_bgr"] = canvas_gpu_bgr
                        item_eval["stats_gpu"] = stats_gpu

                        print(f"  ★ GPU真値 vs 実測 Test 画像: 一致率 {stats_gpu['match_rate']:.4f}% (不一致: {stats_gpu['diff_px']:,} 画素 / {100.0 - stats_gpu['match_rate']:.2f}%)")
                        print(f"    - 過剰遮蔽 (Over-occlusion / 赤): {stats_gpu['over_px']:,} px ({stats_gpu['over_rate']:.2f}%) [GPU可視・Test遮蔽]")
                        print(f"    - 遮蔽漏れ (Under-occlusion / 青): {stats_gpu['under_px']:,} px ({stats_gpu['under_rate']:.2f}%) [GPU遮蔽・Test可視]")

                        if save_images and target_mode in ("gpu", "both"):
                            out_dir = save_dir_override if save_dir_override else eye_dir
                            os.makedirs(out_dir, exist_ok=True)
                            out_path_gpu = os.path.join(out_dir, f"diff_gpu_montage_{eye.lower()}.png")
                            cv2.imwrite(out_path_gpu, canvas_gpu_bgr)
                            # デフォルトの diff_montage_left.png としても保存
                            out_path_default = os.path.join(out_dir, f"diff_montage_{eye.lower()}.png")
                            cv2.imwrite(out_path_default, canvas_gpu_bgr)
                            print(f"    -> GPU差分モンタージュ保存: {out_path_gpu}")

                # 【GT (手メッシュ) vs 実測 Test 画像】の評価
                if test_rgb is not None and gt_rgb is not None:
                    gpu_info = f"GPU Match: {item_eval['stats_gpu']['match_rate']:.2f}%" if "stats_gpu" in item_eval else ""
                    canvas_gt_rgb, stats_gt = generate_gt_diff_montage(
                        gt_rgb, test_rgb, vo_mask, title=title_str, extra_info=gpu_info, threshold=binary_thresh
                    )
                    canvas_gt_bgr = cv2.cvtColor(canvas_gt_rgb, cv2.COLOR_RGB2BGR)
                    item_eval["canvas_gt_bgr"] = canvas_gt_bgr
                    item_eval["stats_gt"] = stats_gt

                    print(f"  実測 Test 画像 vs GT: IoU = {stats_gt['iou']:.4f}% (TP:{stats_gt['tp']:,}, FP:{stats_gt['fp']:,}, FN:{stats_gt['fn']:,})")
                    print(f"    - 誤遮蔽 (Over-occlusion / 赤): {stats_gt['fn']:,} px ({stats_gt['fn_rate']:.2f}%)")
                    print(f"    - 誤透過 (Under-occlusion / 青): {stats_gt['fp']:,} px ({stats_gt['fp_rate']:.2f}%)")

                    if save_images and target_mode in ("gt", "both"):
                        out_dir = save_dir_override if save_dir_override else eye_dir
                        os.makedirs(out_dir, exist_ok=True)
                        out_path_gt = os.path.join(out_dir, f"diff_gt_montage_{eye.lower()}.png")
                        cv2.imwrite(out_path_gt, canvas_gt_bgr)
                        print(f"    -> GT差分モンタージュ保存: {out_path_gt}")

                if "canvas_gpu_bgr" in item_eval or "canvas_gt_bgr" in item_eval:
                    eval_results.append(item_eval)

                total_checked += 1

    print("\n" + "=" * 80)
    print(f"検証完了: 計 {total_checked} 条件のデータを検査しました。")
    if save_images:
        print("  ※ 各条件フォルダ直下に差分モンタージュ画像が保存されました。")
    print("=" * 80)

    # インタラクティブ画像ビューワの表示
    if show_ui and eval_results:
        show_interactive_gallery(eval_results, auto_delay=auto_delay)


def parse_args():
    parser = argparse.ArgumentParser(
        description="SICE SI 2026: セクターマスク & GPU遮蔽真値 自動検証・差分マップ画像表示ツール"
    )
    parser.add_argument(
        "dataset_dir",
        nargs="?",
        default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset",
        help="データセットディレクトリ (省略時: デフォルトパス)"
    )
    parser.add_argument(
        "-c", "--condition", "--filter", "-f",
        dest="condition_filter",
        default=None,
        help="評価対象とする条件名の絞り込みキーワード (例: -c density_1.0)"
    )
    parser.add_argument(
        "-t", "--target",
        dest="target_mode",
        choices=["gpu", "gt", "both"],
        default="gpu",
        help="可視化・保存対象の差分マップ (gpu: GPU真値 vs Test [デフォルト], gt: GT vs Test, both: 両方)"
    )
    parser.add_argument(
        "--no-show",
        action="store_true",
        dest="no_show",
        help="画像ウィンドウを表示せず、コンソール出力と画像保存のみ実行する (バッチ処理用)"
    )
    parser.add_argument(
        "--no-save",
        action="store_true",
        dest="no_save",
        help="差分画像のファイル保存を行わない"
    )
    parser.add_argument(
        "-o", "--output-dir",
        dest="output_dir",
        default=None,
        help="差分画像の保存先ディレクトリ (省略時は各眼フォルダ直下に配置)"
    )
    parser.add_argument(
        "--delay",
        type=int,
        default=0,
        help="スライドショー自動送りの遅延時間 (ms)。0 の場合はキー入力待ち"
    )
    parser.add_argument(
        "-th", "--threshold",
        type=int,
        default=DEFAULT_BINARY_THRESHOLD,
        dest="binary_threshold",
        help=f"可視・遮蔽二値化判定の閾値 (0〜255, デフォルト: {DEFAULT_BINARY_THRESHOLD} = 50%%カバレッジ)"
    )
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    verify_dataset(
        root_dir=args.dataset_dir,
        condition_filter=args.condition_filter,
        target_mode=args.target_mode,
        show_ui=not args.no_show,
        save_images=not args.no_save,
        save_dir_override=args.output_dir,
        auto_delay=args.delay,
        binary_thresh=args.binary_threshold
    )

