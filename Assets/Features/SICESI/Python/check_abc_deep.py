import os
import json
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

# 画像の読み込み
a_direct_path = os.path.join(diag_dir, "A_raw_gpu_mask_direct.png")
a_flipped_path = os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png")
b_path = os.path.join(diag_dir, "B_pre_blit_final_image.png")
c_path = os.path.join(diag_dir, "C_final_display_output.png")
d2_path = os.path.join(diag_dir, "D2_ReadPixels_Saved.png")
vo_path = os.path.join(diag_dir, "vo_silhouette_left_exact.png")

a_dir = np.array(Image.open(a_direct_path))
a_flp = np.array(Image.open(a_flipped_path))
b_img = np.array(Image.open(b_path))
c_img = np.array(Image.open(c_path))
d2_img = np.array(Image.open(d2_path))
vo_mask = np.array(Image.open(vo_path))[:, :, 0] > 0 if os.path.exists(vo_path) else None

print("=== 1. 画像基本情報 ===")
print(f"A direct shape: {a_dir.shape}, dtype: {a_dir.dtype}, unique: {np.unique(a_dir)}")
print(f"B shape: {b_img.shape}, dtype: {b_img.dtype}")
print(f"C shape: {c_img.shape}, dtype: {c_img.dtype}")
print(f"D2 shape: {d2_img.shape}, dtype: {d2_img.dtype}")
if vo_mask is not None:
    print(f"VO mask shape: {vo_mask.shape}, VO pixels: {np.count_nonzero(vo_mask):,}")

# B のチャンネル確認
if b_img.ndim == 3:
    b_gray = b_img[:, :, 0]
else:
    b_gray = b_img

# C のチャンネル確認
if c_img.ndim == 3:
    c_gray = c_img[:, :, 0]
else:
    c_gray = c_img

# 中間値の計測
def count_intermediates(arr):
    # 0 と 255 以外の画素数
    return int(np.count_nonzero((arr > 0) & (arr < 255)))

print("\n=== 2. 中間値画素数 (0 < val < 255) ===")
print(f"A (GPU生判定 direct):   {count_intermediates(a_dir):,} px")
print(f"A (GPU生判定 flipped):  {count_intermediates(a_flp):,} px")
print(f"B (表示前 FinalImage):  {count_intermediates(b_gray):,} px")
print(f"C (表示後 TargetTex):   {count_intermediates(c_gray):,} px")
print(f"D2 (ReadPixels):        {count_intermediates(d2_img[:, :, 0]):,} px")

# 重心と反転関係の調査
pts_a_dir = np.argwhere(a_dir > 128)
pts_a_flp = np.argwhere(a_flp > 128)
# B は遮蔽が黒(0)か白(255)か？
# Test描画は白背景に遮蔽部が黒(0)で描画される仕様
b_black = b_gray < 128
b_white = b_gray >= 128
pts_b_black = np.argwhere(b_black)
pts_c_black = np.argwhere(c_gray < 128)

print("\n=== 3. 幾何重心比較 (遮蔽領域) ===")
if len(pts_a_dir) > 0:
    print(f"A direct (値=255) 重心: X={pts_a_dir[:, 1].mean():.1f}, Y={pts_a_dir[:, 0].mean():.1f}, 面積={len(pts_a_dir):,} px")
if len(pts_a_flp) > 0:
    print(f"A flipped (値=255) 重心: X={pts_a_flp[:, 1].mean():.1f}, Y={pts_a_flp[:, 0].mean():.1f}, 面積={len(pts_a_flp):,} px")
if len(pts_b_black) > 0:
    print(f"B black (値<128) 重心:  X={pts_b_black[:, 1].mean():.1f}, Y={pts_b_black[:, 0].mean():.1f}, 面積={len(pts_b_black):,} px")
if len(pts_c_black) > 0:
    print(f"C black (値<128) 重心:  X={pts_c_black[:, 1].mean():.1f}, Y={pts_c_black[:, 0].mean():.1f}, 面積={len(pts_c_black):,} px")

# B と C の直接差分 vs 反転差分
diff_bc_direct = np.abs(b_gray.astype(int) - c_gray.astype(int))
diff_bc_flip = np.abs(np.fliplr(b_gray).astype(int) - c_gray.astype(int))

print("\n=== 4. B と C の関係 (B -> C で反転しているか？) ===")
print(f"B direct vs C: 不一致画素数={np.count_nonzero(diff_bc_direct > 0):,}, 平均絶対差={diff_bc_direct.mean():.4f}")
print(f"B flipped vs C: 不一致画素数={np.count_nonzero(diff_bc_flip > 0):,}, 平均絶対差={diff_bc_flip.mean():.4f}")

# A と B の関係 (A direct vs B, A flipped vs B)
# Aの遮蔽は val=255, Bの遮蔽は val=0
a_dir_occ = a_dir > 128
a_flp_occ = a_flp > 128

overlap_a_dir_b = np.count_nonzero(a_dir_occ & b_black)
overlap_a_flp_b = np.count_nonzero(a_flp_occ & b_black)

print("\n=== 5. A と B の重なり (A direct vs B vs A flipped) ===")
print(f"A direct (255) & B black (0):  {overlap_a_dir_b:,} px")
print(f"A flipped (255) & B black (0): {overlap_a_flp_b:,} px")

# VO 領域内での厳密評価
if vo_mask is not None:
    print("\n=== 6. VO領域内での A vs B 双方向混同行列 ===")
    vo_total = np.count_nonzero(vo_mask)
    
    # 向きを確定した上で比較 (重なりが大きい方を採用)
    target_a_occ = a_flp_occ if overlap_a_flp_b > overlap_a_dir_b else a_dir_occ
    a_name = "A_flipped" if overlap_a_flp_b > overlap_a_dir_b else "A_direct"
    
    # VO内の各画素判定
    # A_occ: True=遮蔽, False=可視
    # B_occ: True=遮蔽(black), False=可視(white)
    a_in_vo = target_a_occ[vo_mask]
    b_in_vo = b_black[vo_mask]
    c_in_vo = (c_gray < 128)[vo_mask]
    
    # A vs B
    tp_ab = np.count_nonzero(a_in_vo & b_in_vo)        # A遮蔽 & B遮蔽
    tn_ab = np.count_nonzero((~a_in_vo) & (~b_in_vo))  # A可視 & B可視
    fn_ab = np.count_nonzero(a_in_vo & (~b_in_vo))      # A遮蔽 & B可視 (Aが遮蔽と言ったのにBで遮蔽されなかった)
    fp_ab = np.count_nonzero((~a_in_vo) & b_in_vo)      # A可視 & B遮蔽 (Aが可視と言ったのにBで黒くなった)
    
    print(f"採用したAの向き: {a_name}")
    print(f"VO内総画素数: {vo_total:,} px")
    print(f"  A遮蔽画素数: {np.count_nonzero(a_in_vo):,} px")
    print(f"  B遮蔽画素数: {np.count_nonzero(b_in_vo):,} px")
    print(f"  [混同行列 A vs B]")
    print(f"    TP (両方遮蔽):        {tp_ab:,} px")
    print(f"    TN (両方可視):        {tn_ab:,} px")
    print(f"    FN (A遮蔽・B可視):    {fn_ab:,} px  <-- 他AIの懸念1 (取りこぼし)")
    print(f"    FP (A可視・B遮蔽):    {fp_ab:,} px  <-- 他AIの懸念2 (過剰遮蔽)")
    
    # A vs C
    tp_ac = np.count_nonzero(a_in_vo & c_in_vo)
    tn_ac = np.count_nonzero((~a_in_vo) & (~c_in_vo))
    fn_ac = np.count_nonzero(a_in_vo & (~c_in_vo))
    fp_ac = np.count_nonzero((~a_in_vo) & c_in_vo)
    
    print(f"\n  [混同行列 A vs C]")
    print(f"    TP (両方遮蔽):        {tp_ac:,} px")
    print(f"    TN (両方可視):        {tn_ac:,} px")
    print(f"    FN (A遮蔽・C可視):    {fn_ac:,} px")
    print(f"    FP (A可視・C遮蔽):    {fp_ac:,} px")
    print(f"    不一致合計 (FN+FP):   {fn_ac + fp_ac:,} px ({(fn_ac + fp_ac)/vo_total*100:.4f}%)")

    # B vs C
    tp_bc = np.count_nonzero(b_in_vo & c_in_vo)
    tn_bc = np.count_nonzero((~b_in_vo) & (~c_in_vo))
    fn_bc = np.count_nonzero(b_in_vo & (~c_in_vo))
    fp_bc = np.count_nonzero((~b_in_vo) & c_in_vo)
    
    print(f"\n  [混同行列 B vs C]")
    print(f"    TP (両方遮蔽):        {tp_bc:,} px")
    print(f"    TN (両方可視):        {tn_bc:,} px")
    print(f"    FN (B遮蔽・C可視):    {fn_bc:,} px")
    print(f"    FP (B可視・C遮蔽):    {fp_bc:,} px")
    print(f"    不一致合計 (FN+FP):   {fn_bc + fp_bc:,} px ({(fn_bc + fp_bc)/vo_total*100:.4f}%)")
