import os
import numpy as np
from PIL import Image, ImageFilter

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, 0]
vo_img = np.array(Image.open(os.path.join(diag_dir, "vo_silhouette_left_exact.png")))[:, :, 0] > 0
a_flp = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png"))) > 128

# 中間値画素 (0 < val < 255)
inter_mask = (c_img > 0) & (c_img < 255)
print(f"C の中間値画素総数: {np.count_nonzero(inter_mask):,} px")

# VO 外形エッジ (仮想オブジェクトのポリゴン幾何輪郭)
img_vo = Image.fromarray((vo_img * 255).astype(np.uint8))
exp_vo = np.asarray(img_vo.filter(ImageFilter.MaxFilter(3)))
con_vo = np.asarray(img_vo.filter(ImageFilter.MinFilter(3)))
vo_outer_edge = (exp_vo != con_vo)

# A (遮蔽手) の輪郭エッジ
img_a = Image.fromarray((a_flp * 255).astype(np.uint8))
exp_a = np.asarray(img_a.filter(ImageFilter.MaxFilter(3)))
con_a = np.asarray(img_a.filter(ImageFilter.MinFilter(3)))
a_occ_edge = (exp_a != con_a)

# 中間値画素の分布を調べる
inter_on_vo_outer = np.count_nonzero(inter_mask & vo_outer_edge)
inter_on_a_edge = np.count_nonzero(inter_mask & a_occ_edge)
inter_on_both = np.count_nonzero(inter_mask & (vo_outer_edge | a_occ_edge))

print(f"中間値画素のうち、VO外形輪郭 (MSAAポリゴンエッジ) 上にある画素数: {inter_on_vo_outer:,} px ({inter_on_vo_outer / np.count_nonzero(inter_mask) * 100:.2f}%)")
print(f"中間値画素のうち、A遮蔽輪郭 (点群境界) 上にある画素数:             {inter_on_a_edge:,} px ({inter_on_a_edge / np.count_nonzero(inter_mask) * 100:.2f}%)")
print(f"中間値画素のうち、いずれかの輪郭エッジ上にある画素数:               {inter_on_both:,} px ({inter_on_both / np.count_nonzero(inter_mask) * 100:.2f}%)")

# VOシルエットの内部 (エッジから離れた内部) にある中間値画素数
interior_mask = vo_img & ~vo_outer_edge & ~a_occ_edge
inter_in_interior = np.count_nonzero(inter_mask & interior_mask)
print(f"完全に内部にある中間値画素数:                                        {inter_in_interior:,} px")
