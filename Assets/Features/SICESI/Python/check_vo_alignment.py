import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

b_path = os.path.join(diag_dir, "B_pre_blit_final_image.png")
c_path = os.path.join(diag_dir, "C_final_display_output.png")
vo_path = os.path.join(diag_dir, "vo_silhouette_left_exact.png")
a_dir_path = os.path.join(diag_dir, "A_raw_gpu_mask_direct.png")
a_flp_path = os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png")

b_img = np.array(Image.open(b_path))
c_img = np.array(Image.open(c_path))
vo_img = np.array(Image.open(vo_path))[:, :, 0]
a_dir = np.array(Image.open(a_dir_path))
a_flp = np.array(Image.open(a_flp_path))

print(f"VO unique values: {np.unique(vo_img)}")
print(f"VO count (>0): {np.count_nonzero(vo_img > 0):,}")

# VO の重心
pts_vo = np.argwhere(vo_img > 0)
print(f"VO 重心: X={pts_vo[:, 1].mean():.1f}, Y={pts_vo[:, 0].mean():.1f}, 面積={len(pts_vo):,} px")

# B のチャンネル確認 (R, G, B, A)
print(f"B shape: {b_img.shape}")
for ch in range(b_img.shape[2]):
    print(f"B ch {ch} unique: min={b_img[:, :, ch].min()}, max={b_img[:, :, ch].max()}, values={len(np.unique(b_img[:, :, ch]))}")

# C のチャンネル確認
print(f"C shape: {c_img.shape}")
for ch in range(c_img.shape[2]):
    print(f"C ch {ch} unique: min={c_img[:, :, ch].min()}, max={c_img[:, :, ch].max()}, values={len(np.unique(c_img[:, :, ch]))}")

# B の白画素(仮想オブジェクトで遮蔽されていない部分)の重心
b_white = b_img[:, :, 0] > 128
pts_b_white = np.argwhere(b_white)
print(f"B white 重心: X={pts_b_white[:, 1].mean():.1f}, Y={pts_b_white[:, 0].mean():.1f}, 面積={len(pts_b_white):,} px")

# C の白画素の重心
c_white = c_img[:, :, 0] > 128
pts_c_white = np.argwhere(c_white)
print(f"C white 重心: X={pts_c_white[:, 1].mean():.1f}, Y={pts_c_white[:, 0].mean():.1f}, 面積={len(pts_c_white):,} px")

# VOとB_whiteの重なり
overlap_vo_b_white = np.count_nonzero((vo_img > 0) & b_white)
overlap_vo_flip_b_white = np.count_nonzero(np.fliplr(vo_img > 0) & b_white)
print(f"VO direct & B white: {overlap_vo_b_white:,} px")
print(f"VO fliplr & B white: {overlap_vo_flip_b_white:,} px")

# VOとC_whiteの重なり
overlap_vo_c_white = np.count_nonzero((vo_img > 0) & c_white)
overlap_vo_flip_c_white = np.count_nonzero(np.fliplr(vo_img > 0) & c_white)
print(f"VO direct & C white: {overlap_vo_c_white:,} px")
print(f"VO fliplr & C white: {overlap_vo_flip_c_white:,} px")
