import os
import numpy as np
from PIL import Image

case_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\4\Sector8MaskSweep"
d_dir = os.path.join(case_dir, "density_4.0pts_mm2", "Left")
gt_dir = os.path.join(case_dir, "GT", "Left")

gt_img = np.array(Image.open(os.path.join(gt_dir, "gt_left.png")))
gt_mask = (gt_img[:, :, 0] > 128) if gt_img.ndim == 3 else (gt_img > 128)
vo_img = np.array(Image.open(os.path.join(gt_dir, "vo_silhouette_left.png")))
vo_mask = (vo_img[:, :, 0] > 128) if vo_img.ndim == 3 else (vo_img > 128)

gpu_img = np.array(Image.open(os.path.join(d_dir, "gpu_occluded_mask_left.png")))
gpu_occ = (gpu_img[:, :, 0] > 128) if gpu_img.ndim == 3 else (gpu_img > 128)

sec_counts = np.zeros(gt_mask.shape, dtype=np.int32)
for s in range(8):
    s_img = np.array(Image.open(os.path.join(d_dir, f"sector_{s}_mask_left.png")))
    s_bit = (s_img[:, :, 0] > 128) if s_img.ndim == 3 else (s_img > 128)
    sec_counts += s_bit.astype(np.int32)

pred_occ_6 = (sec_counts >= 6) & vo_mask
diff_mask = (gpu_occ != pred_occ_6) & vo_mask
diff_coords = np.argwhere(diff_mask)

print(f"Total VO pixels: {np.count_nonzero(vo_mask)}")
print(f"GPU occ pixels: {np.count_nonzero(gpu_occ & vo_mask)}")
print(f"Sector>=6 occ pixels: {np.count_nonzero(pred_occ_6)}")
print(f"Difference pixels: {len(diff_coords)} ({len(diff_coords)/np.count_nonzero(vo_mask)*100:.5f}%)")

if len(diff_coords) > 0:
    ys, xs = diff_coords[:, 0], diff_coords[:, 1]
    print(f"Diff Y range: [{ys.min()}, {ys.max()}], X range: [{xs.min()}, {xs.max()}]")
    print(f"Sector counts on difference pixels: {np.bincount(sec_counts[diff_mask])}")
