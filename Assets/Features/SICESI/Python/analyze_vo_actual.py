import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

a_dir = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_direct.png")))
a_flp = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png")))
b_img = np.array(Image.open(os.path.join(diag_dir, "B_pre_blit_final_image.png")))[:, :, 0]
c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, 0]

print("=== 1. 画像の階調値ヒストグラム ===")
print("A_flipped:", {int(k): int(v) for k, v in zip(*np.unique(a_flp, return_counts=True))})
print("B_direct: ", {int(k): int(v) for k, v in zip(*np.unique(b_img, return_counts=True))})
c_unique, c_counts = np.unique(c_img, return_counts=True)
c_inter_count = int(np.sum(c_counts[(c_unique > 0) & (c_unique < 255)]))
print(f"C_final: 0={c_counts[c_unique == 0][0]:,}, 255={c_counts[c_unique == 255][0]:,}, 中間値={c_inter_count:,} px (ユニーク値数: {len(c_unique)})")

# B の白領域 (仮想オブジェクト可視部) と A_flipped の白領域 (遮蔽部) の位置関係
# B_flipped を作成
b_flp = np.fliplr(b_img)

# B_flipped の白領域 (1,461,835 px) と A_flipped の白領域 (469,633 px)
b_white = b_flp == 255
b_black = b_flp == 0
a_occ = a_flp == 255
c_occ = c_img < 128
c_vis = c_img >= 128

# 仮想オブジェクトの真の描画領域 (現在フレームで実際に存在したVOの全領域)
# = B_flipped の白(可視部) + A_flipped の白(遮蔽部)
vo_actual = b_white | a_occ
print(f"\n=== 2. 現在フレームの真の仮想オブジェクト領域 (VO_actual) ===")
print(f"VO_actual (可視 + 遮蔽) 総画素数: {np.count_nonzero(vo_actual):,} px")
print(f"  うち可視 (B_flipped == 255):    {np.count_nonzero(b_white):,} px")
print(f"  うち遮蔽 (A_flipped == 255):    {np.count_nonzero(a_occ):,} px")
print(f"  重複 (両方白?):                 {np.count_nonzero(b_white & a_occ):,} px")

# VO_actual 領域内での A vs B の双方向照合
# Aの判定: 遮蔽 = a_occ, 可視 = (~a_occ) & vo_actual
# Bの判定: 遮蔽 = b_black & vo_actual, 可視 = b_white
tp_ab = np.count_nonzero(a_occ & b_black & vo_actual)
tn_ab = np.count_nonzero((~a_occ) & b_white & vo_actual)
fn_ab = np.count_nonzero(a_occ & b_white & vo_actual)
fp_ab = np.count_nonzero((~a_occ) & b_black & vo_actual)

print("\n=== 3. VO_actual 内での A vs B 双方向混同行列 ===")
print(f"  TP (両方遮蔽):         {tp_ab:>9,} px")
print(f"  TN (両方可視):         {tn_ab:>9,} px")
print(f"  FN (A遮蔽・B可視):     {fn_ab:>9,} px  <-- 取りこぼし")
print(f"  FP (A可視・B遮蔽):     {fp_ab:>9,} px  <-- 過剰遮蔽")
print(f"  不一致 (FN+FP):        {fn_ab + fp_ab:>9,} px")
print(f"  双方向一致率:          {(tp_ab + tn_ab)/np.count_nonzero(vo_actual)*100:.6f}%")

# VO_actual 内での A vs C (表示後) の照合
tp_ac = np.count_nonzero(a_occ & c_occ & vo_actual)
tn_ac = np.count_nonzero((~a_occ) & c_vis & vo_actual)
fn_ac = np.count_nonzero(a_occ & c_vis & vo_actual)
fp_ac = np.count_nonzero((~a_occ) & c_occ & vo_actual)

print("\n=== 4. VO_actual 内での A vs C (表示後) 双方向混同行列 ===")
print(f"  TP (両方遮蔽):         {tp_ac:>9,} px")
print(f"  TN (両方可視):         {tn_ac:>9,} px")
print(f"  FN (A遮蔽・C可視):     {fn_ac:>9,} px")
print(f"  FP (A可視・C遮蔽):     {fp_ac:>9,} px")
print(f"  不一致 (FN+FP):        {fn_ac + fp_ac:>9,} px ({(fn_ac + fp_ac)/np.count_nonzero(vo_actual)*100:.4f}%)")
print(f"  双方向一致率:          {(tp_ac + tn_ac)/np.count_nonzero(vo_actual)*100:.6f}%")

# B_flipped vs C の画素値の差
diff_bc = np.abs(b_flp.astype(int) - c_img.astype(int))
diff_bc_in_vo = diff_bc[vo_actual]
mismatch_bc_in_vo = np.count_nonzero(diff_bc_in_vo > 0)
print("\n=== 5. VO_actual 内での B_flipped vs C の画素値差分 ===")
print(f"  画素値が完全一致 (差=0):   {np.count_nonzero(diff_bc_in_vo == 0):,} px ({(1.0 - mismatch_bc_in_vo/len(diff_bc_in_vo))*100:.4f}%)")
print(f"  画素値が異なる (差>0):     {mismatch_bc_in_vo:,} px ({mismatch_bc_in_vo/len(diff_bc_in_vo)*100:.4f}%)")
print(f"  最大絶対誤差:              {diff_bc_in_vo.max()}")
print(f"  平均絶対誤差:              {diff_bc_in_vo.mean():.4f}")
