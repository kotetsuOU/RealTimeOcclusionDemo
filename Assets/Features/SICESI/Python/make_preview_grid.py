import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

# 画像の読み込み
a_flp = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png")))
b_img = np.array(Image.open(os.path.join(diag_dir, "B_pre_blit_final_image.png")))[:, :, :3]
c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, :3]
vo_img = np.array(Image.open(os.path.join(diag_dir, "vo_silhouette_left_exact.png")))[:, :, :3]

# 縮小プレビュー (幅960) を作成して並べる
h, w = b_img.shape[:2]
scale = 4
preview_h = h // scale
preview_w = w // scale

def resize(img):
    return np.array(Image.fromarray(img).resize((preview_w, preview_h), Image.NEAREST))

p_a_flp = resize(np.stack([a_flp]*3, axis=-1))
p_b = resize(b_img)
p_b_flp = resize(np.fliplr(b_img))
p_c = resize(c_img)
p_vo = resize(vo_img)

# 上段: VO, C, A_flipped
# 下段: B, B_flipped, Diff(B_flipped vs C)
diff_bc = np.abs(np.fliplr(b_img).astype(int) - c_img.astype(int)).astype(np.uint8)
p_diff_bc = resize(diff_bc)

row1 = np.hstack([p_vo, p_c, p_a_flp])
row2 = np.hstack([p_b, p_b_flp, p_diff_bc])
grid = np.vstack([row1, row2])

Image.fromarray(grid).save(r"C:\Users\hongo\.gemini\antigravity-ide\brain\7370b895-5fee-41ec-9014-39602ebcc845\stage_comparison_grid.png")
print("Saved comparison grid to stage_comparison_grid.png")
