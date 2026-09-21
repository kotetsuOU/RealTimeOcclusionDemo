"""
================================================================================
セクター占有マスク・GPU実遮蔽判定マスク・テスト画像・GTの整合性自動検証ツール
(Feature: SICESI - Sector Mask & GPU Occlusion Truth Verification)
================================================================================
【概要】
  最新の画面保存手法で取得されたデータセットディレクトリを走査し、
  以下の比較とIoU評価を自動実行してレポートします：
  1. 個別/統合 8-bit パターンマスク (sector_mask_*.png) による予測
  2. GPU 実遮蔽判定マスク (gpu_occluded_mask_*.png)
  3. 実測 Test カメラ画像 (test_*_*.png)
  4. Ground Truth 手メッシュ幾何遮蔽画像 (gt_depth_occluded_mask_*.png または gt_*.png)
  5. [詳細診断] 境界帯・内部の分離集計、中間値検出、最適閾値探索 (debug_gpu_test)

  使用法:
  python Assets/Features/SICESI/Python/verify_sector_masks.py [データセットパス] [--test-th 128] [--debug]
"""

import os
import sys
import glob
import json
import numpy as np
from PIL import Image, ImageFilter

def debug_gpu_test(vo_mask, gpu_img, test_rgb, out_dir, test_th=128):
    """
    GPU遮蔽判定と実測Test画像の差分を「VO境界」「遮蔽境界」「内部」に分離して診断し、
    ピクセル値の遷移、中間値、最適閾値を集計して差分オーバーレイ画像を保存します。
    """
    if gpu_img.shape != vo_mask.shape:
        raise ValueError("GPUマスクとVOマスクのサイズが異なります。")

    if test_rgb.shape != vo_mask.shape + (3,):
        raise ValueError("Test画像はVOマスクと同じサイズのRGB画像にしてください。")

    if gpu_img.dtype != np.uint8 or test_rgb.dtype != np.uint8:
        raise ValueError("この診断は8bit画像を前提としています。")

    if not 0 <= test_th <= 255:
        raise ValueError("test_thは0〜255で指定してください。")

    total = int(np.count_nonzero(vo_mask))
    if total == 0:
        raise ValueError("VOの評価対象画素がありません。")

    os.makedirs(out_dir, exist_ok=True)

    score = test_rgb.max(axis=-1)
    gpu_occ = (gpu_img > 128) & vo_mask
    test_occ = (score <= test_th) & vo_mask

    # 赤：GPUでは可視だが、RGB閾値では遮蔽
    visible_to_occluded = vo_mask & ~gpu_occ & test_occ

    # 水色：GPUでは遮蔽だが、RGB閾値では可視
    occluded_to_visible = vo_mask & gpu_occ & ~test_occ

    diff = visible_to_occluded | occluded_to_visible
    count = int(np.count_nonzero(diff))

    print("\n  [GPU判定 vs Test画像 詳細診断]")
    print(f"    VO画素数: {total:,}")
    print(f"    不一致画素数: {count:,} / {total:,}, 一致率: {100 * (1.0 - count / total):.6f}%")

    intermediate = vo_mask & (gpu_img != 0) & (gpu_img != 255)
    print(f"    GPUマスクの中間値画素 (0,255以外): {np.count_nonzero(intermediate):,}")

    for name, mask in [
        ("GPU可視 → Test遮蔽", visible_to_occluded),
        ("GPU遮蔽 → Test可視", occluded_to_visible),
    ]:
        values = score[mask]
        print(f"    {name}: {values.size:,} pixels")
        if values.size:
            quantiles = np.percentile(values, [0, 25, 50, 75, 100])
            print(f"      Test最大チャンネル値 [min, Q1, median, Q3, max]: {quantiles}")

    def boundary_band(mask, radius):
        image = Image.fromarray(mask.astype(np.uint8) * 255)
        size = 2 * radius + 1
        expanded = np.asarray(image.filter(ImageFilter.MaxFilter(size)))
        contracted = np.asarray(image.filter(ImageFilter.MinFilter(size)))
        return expanded != contracted

    # 8近傍に基づく境界帯。領域は重複しないように分類
    for radius in [1, 2]:
        roi_edge = boundary_band(vo_mask, radius) & vo_mask
        occ_edge = boundary_band(gpu_occ, radius) & vo_mask & ~roi_edge
        interior = vo_mask & ~(roi_edge | occ_edge)

        print(f"    境界帯分析 (半径 {radius}px):")
        for name, region in [
            ("VO輪郭境界", roi_edge),
            ("遮蔽境界(VO境界除く)", occ_edge),
            ("内部バルク領域", interior),
        ]:
            area = int(np.count_nonzero(region))
            bad = int(np.count_nonzero(diff & region))
            rate = f"{100 * bad / area:.6f}%" if area else "N/A"
            print(f"      {name}: 領域={area:,} px, 不一致={bad:,} px, 領域内不一致率={rate}")

    # GPUラベル別の輝度ヒストグラムから、各閾値での不一致数を計算
    hist_occ = np.bincount(score[vo_mask & gpu_occ], minlength=256)
    hist_vis = np.bincount(score[vo_mask & ~gpu_occ], minlength=256)

    errors = (
        np.cumsum(hist_vis)
        + hist_occ.sum()
        - np.cumsum(hist_occ)
    )

    best = int(np.argmin(errors))
    print(f"    閾値感度: Th={test_th}での不一致={int(errors[test_th]):,} px | 最小不一致閾値: Th={best} (不一致: {int(errors[best]):,} px)")

    overlay = test_rgb // 3
    overlay[visible_to_occluded] = (255, 0, 0)
    overlay[occluded_to_visible] = (0, 255, 255)

    output_path = os.path.join(out_dir, "gpu_test_diff.png")
    Image.fromarray(overlay).save(output_path)
    print(f"    差分画像保存先: {output_path}")

def verify_dataset(root_dir, test_th=128, enable_debug=False):
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
    print("=" * 80)

    total_checked = 0

    for s_dir in sweep_dirs:
        dens_dirs = sorted(glob.glob(os.path.join(s_dir, "density_*pts_mm2")))
        for d_dir in dens_dirs:
            dens_str = os.path.basename(d_dir).replace("density_", "").replace("pts_mm2", "")
            for eye in ["Left", "Right"]:
                eye_dir = os.path.join(d_dir, eye)
                if not os.path.exists(eye_dir):
                    continue

                # 1. VO シルエットの探索
                vo_candidates = [
                    os.path.join(s_dir, "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "GT", f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", f"vo_silhouette_{eye.lower()}.png"),
                ]
                vo_path = next((c for c in vo_candidates if os.path.exists(c)), None)
                if not vo_path:
                    continue

                vo_img = np.array(Image.open(vo_path))[:, :, 0]
                vo_mask = (vo_img > 128)
                total_vo = int(np.sum(vo_mask))

                # 2. Ground Truth (GT) の探索 (Mesh Depth GT を最優先)
                gt_depth_candidates = [
                    os.path.join(s_dir, "GT", eye, f"gt_depth_occluded_mask_{eye.lower()}.png"),
                    os.path.join(s_dir, "GT", f"gt_depth_occluded_mask_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", eye, f"gt_depth_occluded_mask_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", f"gt_depth_occluded_mask_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", eye, f"gt_depth_occluded_mask_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", f"gt_depth_occluded_mask_{eye.lower()}.png"),
                ]
                gt_depth_path = next((c for c in gt_depth_candidates if os.path.exists(c)), None)

                gt_legacy_candidates = [
                    os.path.join(s_dir, "GT", eye, f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "GT", f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", eye, f"gt_{eye.lower()}.png"),
                    os.path.join(s_dir, "..", "GT", f"gt_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", eye, f"gt_{eye.lower()}.png"),
                    os.path.join(root_dir, "GT", f"gt_{eye.lower()}.png"),
                ]
                gt_legacy_path = next((c for c in gt_legacy_candidates if os.path.exists(c)), None)

                gt_path = gt_depth_path if gt_depth_path else gt_legacy_path
                is_depth_gt = (gt_path == gt_depth_path and gt_depth_path is not None)

                # GT の安全・独立な読み込み
                gt_vis = None
                gt_occ = None
                if gt_path:
                    if is_depth_gt:
                        # gt_depth_occluded_mask は 白(255)=遮蔽, 黒(0)=可視
                        gt_depth_img = np.array(Image.open(gt_path))[:, :, 0]
                        gt_occ = (gt_depth_img > 128) & vo_mask
                        gt_vis = vo_mask & (~gt_occ)
                        gt_label = f"GPU生深度直接比較 GT ({os.path.basename(gt_path)})"
                    else:
                        # 旧 gt は 赤チャンネル > 128 が可視 (黒い手で遮蔽)
                        gt_img = np.array(Image.open(gt_path))[:, :, 0]
                        gt_vis = (gt_img > 128) & vo_mask
                        gt_occ = vo_mask & (~gt_vis)
                        gt_label = f"旧カラー描画 GT ({os.path.basename(gt_path)}) [陰影含む]"

                # 3. Test 画像の探索
                test_files = sorted(glob.glob(os.path.join(eye_dir, f"test_*_{eye.lower()}.png")))
                test_path = test_files[0] if test_files else None

                gpu_mask_path = os.path.join(eye_dir, f"gpu_occluded_mask_{eye.lower()}.png")
                unified_mask_path = os.path.join(eye_dir, f"sector_mask_{eye.lower()}.png")

                print(f"\n--- [{os.path.basename(s_dir)} / {os.path.basename(d_dir)} / {eye}] ---")
                print(f"  VO シルエット画素数: {total_vo:,} pixels")
                if gt_path:
                    print(f"  GTソース: {gt_label}")

                # Test画像 vs GT の評価
                if test_path and gt_vis is not None:
                    test_img = np.array(Image.open(test_path))[:, :, :3]
                    test_vis = (test_img.max(axis=-1) > test_th) & vo_mask
                    
                    tp = np.sum(test_vis & gt_vis)
                    fp = np.sum(test_vis & (~gt_vis))
                    fn = np.sum((~test_vis) & gt_vis)
                    denom = tp + fp + fn
                    iou = (tp / denom * 100.0) if denom > 0 else 0.0
                    print(f"  実測 Test 画像 (Th>{test_th}) vs GT: 可視 IoU = {iou:.4f}% (TP:{tp:,}, FP:{fp:,}, FN:{fn:,})")

                # 4. 占有マスクの読み込み (セクター0〜7の8枚完全存在をチェック)
                occupied_mask = None
                mask_source = "未検出"
                sector_png_map = {}
                for k in range(8):
                    p = os.path.join(eye_dir, f"sector_{k}_mask_{eye.lower()}.png")
                    if os.path.exists(p):
                        sector_png_map[k] = p

                if len(sector_png_map) == 8:
                    occupied_mask = np.zeros(vo_mask.shape, dtype=np.uint8)
                    for k in range(8):
                        sec_m = (np.array(Image.open(sector_png_map[k]))[:, :, 0] > 128)
                        occupied_mask |= (sec_m.astype(np.uint8) << k)
                    mask_source = "個別8枚二値マスク (ガンマ歪みなし・高精度)"
                elif os.path.exists(unified_mask_path):
                    occupied_mask = np.array(Image.open(unified_mask_path))[:, :, 0]
                    mask_source = "統合1枚マスク (ガンマ歪みの影響あり)"

                # 5. GPU 実遮蔽判定マスクの評価
                if os.path.exists(gpu_mask_path):
                    gpu_img = np.array(Image.open(gpu_mask_path))[:, :, 0]
                    gpu_occ = (gpu_img > 128) & vo_mask

                    if occupied_mask is not None:
                        print(f"  占有パターンソース: {mask_source}")
                        # 占有数 >= 6 での予測と比較 (デフォルトの SectorThreshold R=6)
                        occ_counts = bit_counts[occupied_mask]
                        for r in [6]:
                            pred_occ = (occ_counts >= r) & vo_mask
                            diff = np.sum((pred_occ != gpu_occ) & vo_mask)
                            match_pct = (1.0 - diff / total_vo) * 100.0 if total_vo > 0 else 0.0
                            print(f"  占有パターン (Occ >= {r}) vs GPU判定: 一致率 {match_pct:.4f}% (不一致: {diff:,} 画素)")

                            if test_path:
                                test_img = np.array(Image.open(test_path))[:, :, :3]
                                test_occ = vo_mask & (test_img.max(axis=-1) <= test_th)
                                diff_p_test = np.sum((pred_occ != test_occ) & vo_mask)
                                match_p_test = (1.0 - diff_p_test / total_vo) * 100.0 if total_vo > 0 else 0.0
                                print(f"  占有パターン (Occ >= {r}) vs 実測 Test 画像 (Th<={test_th}): 一致率 {match_p_test:.4f}% (不一致: {diff_p_test:,} 画素)")

                    if test_path:
                        test_img = np.array(Image.open(test_path))[:, :, :3]
                        test_occ = vo_mask & (test_img.max(axis=-1) <= test_th)
                        diff_test = np.sum((gpu_occ != test_occ) & vo_mask)
                        match_test = (1.0 - diff_test / total_vo) * 100.0 if total_vo > 0 else 0.0
                        print(f"  GPU判定 vs 実測 Test 画像 (Th<={test_th}): 一致率 {match_test:.4f}% (不一致: {diff_test:,} 画素)")

                        # 詳細診断の実行 (--debug または 初回条件)
                        if enable_debug or total_checked == 0:
                            debug_out_dir = os.path.join(eye_dir, "debug_gpu_test")
                            debug_gpu_test(
                                vo_mask=vo_mask,
                                gpu_img=gpu_img,
                                test_rgb=test_img,
                                out_dir=debug_out_dir,
                                test_th=test_th
                            )

                    # GPU判定 vs GT の直接比較
                    if gt_occ is not None and gt_vis is not None:
                        diff_gt = np.sum((gpu_occ != gt_occ) & vo_mask)
                        match_gt = (1.0 - diff_gt / total_vo) * 100.0 if total_vo > 0 else 0.0

                        gpu_vis = vo_mask & (~gpu_occ)
                        tp_g = np.sum(gpu_vis & gt_vis)
                        fp_g = np.sum(gpu_vis & (~gt_vis))
                        fn_g = np.sum((~gpu_vis) & gt_vis)
                        denom_g = tp_g + fp_g + fn_g
                        iou_g = (tp_g / denom_g * 100.0) if denom_g > 0 else 0.0

                        print(f"  GPU判定 vs GT: 遮蔽一致率 {match_gt:.4f}% (不一致: {diff_gt:,} 画素), 可視 IoU = {iou_g:.4f}% (TP:{tp_g:,}, FP:{fp_g:,}, FN:{fn_g:,})")

                total_checked += 1

    print("\n" + "=" * 80)
    print(f"検証完了: 計 {total_checked} 条件のデータを検査しました。")
    print("=" * 80)

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="セクター占有マスク・GPU遮蔽真値・Test画像 自動検証ツール")
    parser.add_argument("target_dir", nargs="?", default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset", help="データセットルート")
    parser.add_argument("--test-th", type=int, default=128, help="実測Test画像の二値化閾値 (0..255, デフォルト 128)")
    parser.add_argument("--debug", action="store_true", help="全条件で詳細診断 (debug_gpu_test) を実行")
    args = parser.parse_args()

    verify_dataset(args.target_dir, test_th=args.test_th, enable_debug=args.debug)
