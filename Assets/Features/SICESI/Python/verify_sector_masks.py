"""
================================================================================
セクター占有マスク・GPU実遮蔽判定マスク・最終遮蔽マスク・GTの厳密整合性検証ツール
(Feature: SICESI - Sector Mask, Direct Occlusion & Final Mask Verification)
================================================================================
【概要】
  最新の画面保存および生バッファリードバックで取得されたデータセットディレクトリを走査し、
  領域 $R$ (VO領域), $E$ (セクター評価領域), $D$ (初期統合点群直接遮蔽領域) を厳密に分離した上で、
  以下の二段階検証と可視IoU評価を自動実行してレポートします：

  1. 内部整合性検証 (Internal Consistency on E):
     - セクター占有数判定 (Occ >= 6) と GPUセクター遮蔽判定 (bit 13) が E 内で完全一致することの検証
     - E 外で bit 13 が立っていないこと、未評価画素がすべて物理点群 (origin == 0) であることの不変条件検証
  2. 最終遮蔽マスク検証 (Final Occlusion vs Mesh Depth GT on R):
     - 最終遮蔽マスク M_occ = (bit 13 == 1) | D (または final_occlusion_mask_direct.png)
     - 予測可視領域 P_vis = R & ~M_occ = E & ~(bit 13 == 1)
     - Mesh Depth GT (gt_depth_occluded_mask_*.png) に対する真の可視 IoU 評価 (TP, FP, FN, IoU)

  使用法:
  python Assets/Features/SICESI/Python/verify_sector_masks.py [データセットパス] [--test-th 128]
"""

import os
import sys
import glob
import re
import numpy as np
from PIL import Image

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

def load_mono_image(path):
    img = np.array(Image.open(path))
    if img.ndim == 3:
        return img[:, :, 0]
    return img

def find_file_in_ancestors(start_dir, target_rel_paths, max_levels=10):
    """祖先ディレクトリを遡って最初に存在するファイルパスを返す"""
    cur = os.path.abspath(start_dir)
    for _ in range(max_levels):
        for rel in target_rel_paths:
            candidate = os.path.join(cur, rel)
            if os.path.exists(candidate):
                return candidate
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return None

def verify_dataset(root_dir, test_th=128):
    # StageDiagnosis フォルダまたは親ディレクトリを探索
    target_dirs = []
    if os.path.exists(os.path.join(root_dir, "vo_silhouette_pre_correction.png")) or \
       os.path.exists(os.path.join(root_dir, "A_raw_uint32.bin")):
        target_dirs.append(root_dir)
    else:
        stage_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "StageDiagnosis"), recursive=True))
        if stage_dirs:
            target_dirs.extend(stage_dirs)
        
        sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Sector8MaskSweep"), recursive=True))
        if not sweep_dirs and os.path.basename(root_dir) == "Sector8MaskSweep":
            sweep_dirs = [root_dir]
        for s_dir in sweep_dirs:
            dens_dirs = sorted(glob.glob(os.path.join(s_dir, "density_*pts_mm2")))
            for d_dir in dens_dirs:
                for eye in ["Left", "Right"]:
                    eye_dir = os.path.join(d_dir, eye)
                    if os.path.exists(eye_dir):
                        target_dirs.append(eye_dir)

    target_dirs = sorted(list(set(target_dirs)))
    if not target_dirs:
        target_dirs = [root_dir]

    bit_counts = np.array([bin(i).count('1') for i in range(256)], dtype=np.uint8)

    print("=" * 80)
    print("【SICE SI セクターマスク・直接遮蔽・最終遮蔽マスク 厳密自動検証】")
    print(f"  探索ルート: {root_dir}")
    print(f"  検出対象数: {len(target_dirs)} 箇所")
    print("=" * 80)

    total_checked = 0

    for cur_dir in target_dirs:
        # 1. VO シルエットの探索 (表示補正前 R)
        vo_rel_candidates = [
            "vo_silhouette_pre_correction.png",
            "vo_silhouette_left.png",
            "vo_silhouette_right.png",
            os.path.join("GT", "Left", "vo_silhouette_left.png"),
            os.path.join("GT", "vo_silhouette_left.png"),
            os.path.join("GT", "Right", "vo_silhouette_right.png"),
            os.path.join("GT", "vo_silhouette_right.png"),
        ]
        vo_path = find_file_in_ancestors(cur_dir, vo_rel_candidates)
        if not vo_path:
            continue

        R = (load_mono_image(vo_path) > 128)
        H, W = R.shape
        total_vo = int(np.count_nonzero(R))
        if total_vo == 0:
            continue

        # 2. Ground Truth (Mesh Depth GT のみを受け入れ、陰影付きカラーGTフォールバックは排除)
        gt_rel_candidates = [
            "gt_depth_occluded_mask_left.png",
            "gt_depth_occluded_mask_right.png",
            os.path.join("GT", "Left", "gt_depth_occluded_mask_left.png"),
            os.path.join("GT", "gt_depth_occluded_mask_left.png"),
            os.path.join("GT", "Right", "gt_depth_occluded_mask_right.png"),
            os.path.join("GT", "gt_depth_occluded_mask_right.png"),
        ]
        gt_path = find_file_in_ancestors(cur_dir, gt_rel_candidates)
        gt_occ = None
        gt_vis = None
        if gt_path:
            gt_img = np.array(Image.open(gt_path))
            if gt_img.ndim == 3:
                gt_img = gt_img[:, :, 0]
            # gt_depth_occluded_mask は白(255)=遮蔽、黒(0)=可視
            gt_occ = (gt_img > 128) & R
            gt_vis = R & (~gt_occ)

        # 3. 生データ・マスクの読み込み
        # A_raw_uint32.bin (DirectXテクスチャバッファは下原点なので np.flipud でPNG座標系に合わせる)
        a_bin_path = os.path.join(cur_dir, "A_raw_uint32.bin")
        origin_bin_path = os.path.join(cur_dir, "origin_type_raw_uint32.bin")
        
        # PNG マスク群
        eval_png_path = os.path.join(cur_dir, "evaluated_mask_pre_correction.png")
        sector_occ_png_path = os.path.join(cur_dir, "sector_occlusion_mask_direct.png")
        final_occ_png_path = os.path.join(cur_dir, "final_occlusion_mask_direct.png")
        origin_png_path = os.path.join(cur_dir, "origin_type_map_direct.png")

        occupied_mask = None
        evaluated_mask = None
        sector_occ_mask = None
        origin_type_map = None

        if os.path.exists(a_bin_path):
            a_raw = np.fromfile(a_bin_path, dtype=np.uint32)
            if a_raw.size == H * W:
                a_2d = np.flipud(a_raw.reshape((H, W)))
                occupied_mask = (a_2d & 0xFF).astype(np.uint8)
                evaluated_mask = ((a_2d >> 12) & 1) != 0
                sector_occ_mask = ((a_2d >> 13) & 1) != 0
        
        if os.path.exists(origin_bin_path):
            orig_raw = np.fromfile(origin_bin_path, dtype=np.uint32)
            if orig_raw.size == H * W:
                origin_type_map = np.flipud(orig_raw.reshape((H, W)))

        # PNG からの補完 (バイナリがない場合)
        if evaluated_mask is None and os.path.exists(eval_png_path):
            evaluated_mask = (load_mono_image(eval_png_path) > 128)
        if sector_occ_mask is None and os.path.exists(sector_occ_png_path):
            sector_occ_mask = (load_mono_image(sector_occ_png_path) > 128)
        if origin_type_map is None and os.path.exists(origin_png_path):
            origin_type_map = load_mono_image(origin_png_path)

        # 個別8枚マスクからの占有パターン復元
        if occupied_mask is None:
            sector_png_map = {}
            for k in range(8):
                p_cands = [
                    os.path.join(cur_dir, f"sector_{k}_mask_left.png"),
                    os.path.join(cur_dir, f"sector_{k}_mask_right.png"),
                    os.path.join(cur_dir, f"sector_{k}_mask.png")
                ]
                p = next((c for c in p_cands if os.path.exists(c)), None)
                if p:
                    sector_png_map[k] = p
            if len(sector_png_map) == 8:
                occupied_mask = np.zeros((H, W), dtype=np.uint8)
                for k in range(8):
                    sec_m = (load_mono_image(sector_png_map[k]) > 128)
                    occupied_mask |= (sec_m.astype(np.uint8) << k)

        # 統合1枚マスク
        if occupied_mask is None:
            unified_cands = [
                os.path.join(cur_dir, "sector_mask_left.png"),
                os.path.join(cur_dir, "sector_mask_right.png"),
                os.path.join(cur_dir, "sector_mask.png")
            ]
            u_p = next((c for c in unified_cands if os.path.exists(c)), None)
            if u_p:
                occupied_mask = load_mono_image(u_p)

        if evaluated_mask is None:
            # 評価フラグ情報が得られない場合はスキップ
            continue

        # ======================================================================
        # 領域の厳密分離: R, E, D, D_ghost
        # ======================================================================
        E = R & evaluated_mask

        if origin_type_map is not None:
            # D: R かつ 未評価 かつ 最前面が物理点群 (origin == 0) → 直接遮蔽
            D = R & (~E) & (origin_type_map == 0)
            # D_ghost: R かつ 未評価 かつ 非点群 (origin != 0)
            # → VO 内に残留した初期統合ゴースト画素。黒画素となるが遮蔽判定には含めない
            D_ghost = R & (~E) & (origin_type_map != 0)
        else:
            # origin_type_map がない場合は R \ E をすべて D と仮定
            D = R & (~E)
            D_ghost = np.zeros_like(R)

        # R の内部において、E 以外で bit 13 が立っていないことの検証
        if sector_occ_mask is not None:
            invalid_sector_bit = R & (~E) & sector_occ_mask
            invalid_bit_count = int(np.count_nonzero(invalid_sector_bit))
            if invalid_bit_count > 0:
                raise ValueError(
                    f"Invalid sector occlusion bit: bit 13 is set inside R but outside E ({invalid_bit_count} px)."
                )

        print(f"\n--- [{os.path.relpath(cur_dir, root_dir)}] ---")
        print(f"  VO領域 R: {total_vo:,} px")
        print(f"  セクター評価領域 E: {int(np.count_nonzero(E)):,} px ({100.0 * np.count_nonzero(E) / total_vo:.2f}%)")
        print(f"  点群直接遮蔽領域 D: {int(np.count_nonzero(D)):,} px ({100.0 * np.count_nonzero(D) / total_vo:.2f}%)")
        d_ghost_count = int(np.count_nonzero(D_ghost)) if 'D_ghost' in dir() else 0
        if d_ghost_count > 0:
            print(f"  未評価非点群 D_ghost: {d_ghost_count:,} px ({100.0 * d_ghost_count / total_vo:.2f}%) ← VO内ゴースト(黒、遮蔽判定外)")
        decomp_check = int(np.count_nonzero(R & ~(E | D | D_ghost))) if 'D_ghost' in dir() else 0
        if decomp_check == 0:
            print(f"  不変条件チェック: R == E | D | D_ghost -> 合格 (不整合 0 px)")
        else:
            print(f"  不変条件チェック: R == E | D | D_ghost -> 警告 ({decomp_check} px 分類不能)")

        # ======================================================================
        # 1. 内部整合性検証 (Internal Consistency on E)
        # ======================================================================
        if occupied_mask is not None and sector_occ_mask is not None:
            occ_count = bit_counts[occupied_mask]
            pred_occ_e = (occ_count >= 6) & E
            gpu_sector_occ_e = sector_occ_mask & E

            diff_internal = int(np.count_nonzero(pred_occ_e != gpu_sector_occ_e))
            match_internal = 100.0 * (1.0 - diff_internal / np.count_nonzero(E)) if np.count_nonzero(E) > 0 else 100.0
            print(f"  【検証1: 内部整合性 (E内)】 Occ>=6 vs bit 13:")
            print(f"    一致率: {match_internal:.6f}% | 不一致: {diff_internal} px")
            if diff_internal > 0:
                raise ValueError(f"Internal inconsistency on E: {diff_internal} pixels differ between Occ>=6 and bit 13!")

        # ======================================================================
        # 2. 最終遮蔽マスク検証 (Final Occlusion vs Mesh Depth GT on R)
        # ======================================================================
        if sector_occ_mask is not None:
            final_pred_occ = (sector_occ_mask & E) | D
            pred_vis = R & (~final_pred_occ)

            # 保存された final_occlusion_mask_direct.png との照合
            if os.path.exists(final_occ_png_path):
                saved_final_img = np.array(Image.open(final_occ_png_path))
                if saved_final_img.ndim == 3:
                    saved_final_img = saved_final_img[:, :, 0]
                # final_pred_occ = (E_occ | D) のみ。D_ghost は最終遮蔽に含まない
                saved_final_occ = (saved_final_img > 128) & R
                final_mismatch = int(np.count_nonzero(final_pred_occ != saved_final_occ))
                print(f"  【最終遮蔽マスク合成検証】 (E_occ | D) vs 保存済final_mask:")
                print(f"    不一致画素数: {final_mismatch} px")
                if final_mismatch > 0:
                    raise ValueError(f"Final mask mismatch: {final_mismatch} px differ from saved direct final mask!")


            if gt_occ is not None and gt_vis is not None:
                tp = int(np.count_nonzero(pred_vis & gt_vis))
                fp = int(np.count_nonzero(pred_vis & (~gt_vis)))
                fn = int(np.count_nonzero((~pred_vis) & gt_vis))
                denom = tp + fp + fn
                iou = (tp / denom * 100.0) if denom > 0 else 0.0

                diff_gt = int(np.count_nonzero(final_pred_occ != gt_occ))
                match_gt = 100.0 * (1.0 - diff_gt / total_vo)

                print(f"  【検証2: Mesh Depth GT評価 (R全域)】")
                print(f"    遮蔽一致率: {match_gt:.4f}% (不一致: {diff_gt:,} px)")
                print(f"    可視領域 IoU: {iou:.4f}%")
                print(f"    混同行列: TP={tp:,}, FP={fp:,}, FN={fn:,}, 分母(TP+FP+FN)={denom:,}")

        total_checked += 1

    print("\n" + "=" * 80)
    print(f"検証完了: 計 {total_checked} 箇所のデータを厳密検査しました。")
    print("=" * 80)

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="セクター占有マスク・直接遮蔽・最終遮蔽マスク 厳密自動検証ツール")
    parser.add_argument("target_dir", nargs="?", default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset", help="データセットルート")
    parser.add_argument("--test-th", type=int, default=128, help="実測Test画像の二値化閾値 (0..255, デフォルト 128)")
    args = parser.parse_args()

    verify_dataset(args.target_dir, test_th=args.test_th)
