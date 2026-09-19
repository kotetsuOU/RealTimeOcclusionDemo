"""
================================================================================
セクター占有マスク・GPU実遮蔽真値マスク・テスト画像の整合性自動検証ツール
(Feature: SICESI - Sector Mask & GPU Occlusion Truth Verification)
================================================================================
【概要】
  最新の画面保存手法で取得されたデータセットディレクトリを走査し、
  以下の比較とIoU評価を自動実行してレポートします：
  1. 統合 8-bit パターンマスク (sector_mask_*.png) による各ルール予測
  2. GPU 実遮蔽判定マスク (gpu_occluded_mask_*.png)
  3. 実測 Test カメラ画像 (test_*_*.png)
  4. Ground Truth 手メッシュ遮蔽画像 (gt_*.png)

  python Assets/Features/SICESI/Python/verify_sector_masks.py
"""

import os
import sys
import glob
import json
import numpy as np
from PIL import Image

def verify_dataset(root_dir, test_th=128):
    sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Sector8MaskSweep"), recursive=True))
    if not sweep_dirs:
        # 直下も検索
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
                vo_mask = (vo_img > 128)
                total_vo = np.sum(vo_mask)

                print(f"\n--- [{os.path.basename(s_dir)} / {os.path.basename(d_dir)} / {eye}] ---")
                print(f"  VO シルエット画素数: {total_vo:,} pixels")

                # Test画像 vs GT の評価
                if test_path and gt_path:
                    test_img = np.array(Image.open(test_path))[:, :, :3]
                    gt_img = np.array(Image.open(gt_path))[:, :, 0]
                    test_vis = (test_img.max(axis=-1) > test_th) & vo_mask
                    gt_vis = (gt_img > 128) & vo_mask
                    
                    tp = np.sum(test_vis & gt_vis)
                    fp = np.sum(test_vis & (~gt_vis))
                    fn = np.sum((~test_vis) & gt_vis)
                    denom = tp + fp + fn
                    iou = (tp / denom * 100.0) if denom > 0 else 0.0
                    print(f"  実測 Test 画像 (Th>{test_th}) vs GT: IoU = {iou:.4f}% (TP:{tp:,}, FP:{fp:,}, FN:{fn:,})")

                # 占有マスクの読み込み
                occupied_mask = None
                mask_source = "未検出"
                sector_pngs = sorted(glob.glob(os.path.join(eye_dir, "sector_*_mask_*.png")))
                if len(sector_pngs) >= 8:
                    occupied_mask = np.zeros(vo_mask.shape, dtype=np.uint8)
                    for k in range(8):
                        p = next((x for x in sector_pngs if f"sector_{k}_mask" in os.path.basename(x)), None)
                        if p:
                            sec_m = (np.array(Image.open(p))[:, :, 0] > 128)
                            occupied_mask |= (sec_m.astype(np.uint8) << k)
                    mask_source = f"個別8枚二値マスク (ガンマ歪みなし・高精度)"
                elif os.path.exists(unified_mask_path):
                    occupied_mask = np.array(Image.open(unified_mask_path))[:, :, 0]
                    mask_source = "統合1枚マスク (ガンマ歪みの影響あり)"

                # GPU 実遮蔽判定マスクの評価
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
                            match_pct = (1.0 - diff / total_vo) * 100.0
                            print(f"  占有パターン (Occ >= {r}) vs GPU真値: 一致率 {match_pct:.4f}% (不一致: {diff:,} 画素)")

                            if test_path:
                                test_img = np.array(Image.open(test_path))[:, :, :3]
                                test_occ = vo_mask & (test_img.max(axis=-1) <= test_th)
                                diff_p_test = np.sum((pred_occ != test_occ) & vo_mask)
                                match_p_test = (1.0 - diff_p_test / total_vo) * 100.0
                                print(f"  占有パターン (Occ >= {r}) vs 実測 Test 画像 (Th<={test_th}): 一致率 {match_p_test:.4f}% (不一致: {diff_p_test:,} 画素)")

                    if test_path:
                        test_img = np.array(Image.open(test_path))[:, :, :3]
                        test_occ = vo_mask & (test_img.max(axis=-1) <= test_th)
                        diff_test = np.sum((gpu_occ != test_occ) & vo_mask)
                        match_test = (1.0 - diff_test / total_vo) * 100.0
                        print(f"  GPU真値 vs 実測 Test 画像 (Th<={test_th}): 一致率 {match_test:.4f}% (不一致: {diff_test:,} 画素)")

                total_checked += 1

    print("\n" + "=" * 80)
    print(f"検証完了: 計 {total_checked} 条件のデータを検査しました。")
    print("=" * 80)

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="セクター占有マスク・GPU遮蔽真値・Test画像 自動検証ツール")
    parser.add_argument("target_dir", nargs="?", default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset", help="データセットルート")
    parser.add_argument("--test-th", type=int, default=128, help="実測Test画像の二値化閾値 (0..255, デフォルト 128)")
    args = parser.parse_args()

    verify_dataset(args.target_dir, test_th=args.test_th)
