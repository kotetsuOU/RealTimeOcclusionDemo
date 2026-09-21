import os
import glob
import numpy as np
from PIL import Image

def verify():
    case_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\4\Sector8MaskSweep"
    gt_dir = os.path.join(case_dir, "GT")
    left_gt_path = os.path.join(gt_dir, "Left", "gt_left.png")
    left_vo_path = os.path.join(gt_dir, "Left", "vo_silhouette_left.png")
    
    gt_img = np.array(Image.open(left_gt_path))
    gt_mask = (gt_img[:, :, 0] > 128) if gt_img.ndim == 3 else (gt_img > 128)
    vo_img = np.array(Image.open(left_vo_path))
    vo_mask = (vo_img[:, :, 0] > 128) if vo_img.ndim == 3 else (vo_img > 128)
    
    H, W = gt_mask.shape
    total_vo = np.count_nonzero(vo_mask)
    gt_vis = gt_mask & vo_mask
    gt_occ = (~gt_mask) & vo_mask
    
    print(f"============================================================")
    print(f"【検証結果】 Mesh Depth GT と占有マスクの一致性検証")
    print(f"  対象ケース: {case_dir}")
    print(f"  解像度: {W} x {H}")
    print(f"  仮想物体総画素数 (VO): {total_vo:,} px")
    print(f"  GT可視画素数: {np.count_nonzero(gt_vis):,} px ({np.count_nonzero(gt_vis)/total_vo*100:.2f}%)")
    print(f"  GT遮蔽画素数: {np.count_nonzero(gt_occ):,} px ({np.count_nonzero(gt_occ)/total_vo*100:.2f}%)")
    print(f"============================================================")

    for d in [0.25, 1.0, 4.0, 16.0, 64.0]:
        d_dir = os.path.join(case_dir, f"density_{d}pts_mm2", "Left")
        if not os.path.exists(d_dir):
            continue
            
        # 8セクター二値マスクから N_occ
        sec_pngs = [os.path.join(d_dir, f"sector_{s}_mask_left.png") for s in range(8)]
        sec_counts = np.zeros((H, W), dtype=np.int32)
        for s, p in enumerate(sec_pngs):
            s_img = np.array(Image.open(p))
            s_bit = (s_img[:, :, 0] > 128) if s_img.ndim == 3 else (s_img > 128)
            sec_counts += s_bit.astype(np.int32)
            
        # GPU実遮蔽マスク
        gpu_occ_path = os.path.join(d_dir, "gpu_occluded_mask_left.png")
        gpu_img = np.array(Image.open(gpu_occ_path))
        gpu_occ = (gpu_img[:, :, 0] > 128) if gpu_img.ndim == 3 else (gpu_img > 128)
        gpu_occ = gpu_occ & vo_mask
        gpu_vis = (~gpu_occ) & vo_mask
        
        # 占有数 N_occ >= 6 (パイプラインの標準Bouchiba判定設定)
        pred_occ_6 = (sec_counts >= 6) & vo_mask
        pred_vis_6 = (~pred_occ_6) & vo_mask
        
        # 1. 占有マスク (N_occ >= 6) と GPU実遮蔽マスクの内部一致率
        match_gpu_sec = np.count_nonzero((gpu_occ == pred_occ_6) & vo_mask)
        mismatch_gpu_sec = np.count_nonzero((gpu_occ != pred_occ_6) & vo_mask)
        consistency = match_gpu_sec / total_vo * 100
        
        # 2. GT vs GPU実遮蔽マスクのIoU
        gpu_inter = np.count_nonzero(gpu_vis & gt_vis)
        gpu_union = np.count_nonzero(gpu_vis | gt_vis)
        gpu_iou = gpu_inter / gpu_union * 100
        gpu_err = np.count_nonzero(gpu_vis != gt_vis)
        
        # 3. 各セクター閾値 (th=1..8) での可視IoU
        ious = []
        for th in range(1, 9):
            p_occ = (sec_counts >= th) & vo_mask
            p_vis = (~p_occ) & vo_mask
            inter = np.count_nonzero(p_vis & gt_vis)
            union = np.count_nonzero(p_vis | gt_vis)
            ious.append(inter / union * 100)
            
        best_th = np.argmax(ious) + 1
        best_iou = np.max(ious)
        
        print(f"\n[密度 {d:5.2f} pts/mm2]")
        print(f"  ・GPU実判定 vs 8セクター合成(th=6) の内部一致率: {consistency:.4f}% (不一致わずか {mismatch_gpu_sec} px / {total_vo:,} px)")
        print(f"  ・GT vs GPU実判定 の可視IoU: {gpu_iou:.2f}% (誤り率: {gpu_err/total_vo*100:.2f}%)")
        print(f"  ・最適セクター閾値: th={best_th} (最高IoU: {best_iou:.2f}%)")
        print(f"    - th=1: {ious[0]:.2f}% | th=4: {ious[3]:.2f}% | th=5: {ious[4]:.2f}% | th=6: {ious[5]:.2f}% | th=8: {ious[7]:.2f}%")

if __name__ == "__main__":
    verify()
