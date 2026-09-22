import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

b_img = np.array(Image.open(os.path.join(diag_dir, "B_pre_blit_final_image.png")))[:, :, 0]
c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, 0]
vo_img = np.array(Image.open(os.path.join(diag_dir, "vo_silhouette_left_exact.png")))[:, :, 0]

b_flp = np.fliplr(b_img)

# B_flp が黒 (0) で、C が白 (255) の領域
b_black_c_white = (b_flp == 0) & (c_img > 128)
pts_diff = np.argwhere(b_black_c_white)

print(f"B_flp == 0 かつ C == 255 の画素数: {len(pts_diff):,} px")
if len(pts_diff) > 0:
    min_y, min_x = pts_diff.min(axis=0)
    max_y, max_x = pts_diff.max(axis=0)
    print(f"差分領域の X範囲: [{min_x}, {max_x}], Y範囲: [{min_y}, {max_y}]")

# C が白 (255) の領域の X範囲
pts_c_white = np.argwhere(c_img > 128)
min_y_c, min_x_c = pts_c_white.min(axis=0)
max_y_c, max_x_c = pts_c_white.max(axis=0)
print(f"C_white の X範囲: [{min_x_c}, {max_x_c}], Y範囲: [{min_y_c}, {max_y_c}]")

# B_flp が白 (255) の領域の X範囲
pts_b_white = np.argwhere(b_flp == 255)
min_y_b, min_x_b = pts_b_white.min(axis=0)
max_y_b, max_x_b = pts_b_white.max(axis=0)
print(f"B_flp_white の X範囲: [{min_x_b}, {max_x_b}], Y範囲: [{min_y_b}, {max_y_b}]")

# VO が白 (255) の領域の X範囲
pts_vo = np.argwhere(vo_img > 128)
min_y_vo, min_x_vo = pts_vo.min(axis=0)
max_y_vo, max_x_vo = pts_vo.max(axis=0)
print(f"VO_white の X範囲: [{min_x_vo}, {max_x_vo}], Y範囲: [{min_y_vo}, {max_y_vo}]")
