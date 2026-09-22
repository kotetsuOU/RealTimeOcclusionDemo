import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

files = [
    "B_pre_blit_final_image.png",
    "C_normal_blit_from_B.png",
    "C_lossless_blit_from_B.png",
    "C_final_display_output.png",
    "C_lossless_display_output.png"
]

for fname in files:
    fpath = os.path.join(diag_dir, fname)
    if os.path.exists(fpath):
        img = np.array(Image.open(fpath))
        gray = img[:, :, 0] if img.ndim == 3 else img
        inter = np.count_nonzero((gray > 0) & (gray < 255))
        u = np.unique(gray)
        print(f"[{fname}] shape={img.shape}, unique={len(u)} values (min={gray.min()}, max={gray.max()}), intermediate={inter:,} px")
        if len(u) <= 10:
            print(f"   values: {u}")
    else:
        print(f"[{fname}] NOT FOUND")

# C_final vs C_lossless_display
c1 = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, :3]
c2 = np.array(Image.open(os.path.join(diag_dir, "C_lossless_display_output.png")))[:, :, :3]
diff_c1_c2 = np.abs(c1.astype(int) - c2.astype(int))
print(f"\nC_final vs C_lossless_display: 異なる画素数 = {np.count_nonzero(diff_c1_c2.max(axis=-1) > 0):,}, 最大誤差 = {diff_c1_c2.max()}")

# B vs C_lossless_blit
b = np.array(Image.open(os.path.join(diag_dir, "B_pre_blit_final_image.png")))[:, :, :3]
c_loss_b = np.array(Image.open(os.path.join(diag_dir, "C_lossless_blit_from_B.png")))[:, :, :3]
diff_b_loss = np.abs(b.astype(int) - c_loss_b.astype(int))
print(f"B vs C_lossless_blit: 異なる画素数 = {np.count_nonzero(diff_b_loss.max(axis=-1) > 0):,}, 最大誤差 = {diff_b_loss.max()}")

# B vs C_normal_blit
c_norm_b = np.array(Image.open(os.path.join(diag_dir, "C_normal_blit_from_B.png")))[:, :, :3]
diff_b_norm = np.abs(b.astype(int) - c_norm_b.astype(int))
print(f"B vs C_normal_blit: 異なる画素数 = {np.count_nonzero(diff_b_norm.max(axis=-1) > 0):,}, 最大誤差 = {diff_b_norm.max()}")

