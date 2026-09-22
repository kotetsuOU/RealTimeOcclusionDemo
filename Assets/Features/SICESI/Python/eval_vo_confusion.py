import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

a_dir = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_direct.png"))) > 128
a_flp = np.array(Image.open(os.path.join(diag_dir, "A_raw_gpu_mask_flipped.png"))) > 128
b_img = np.array(Image.open(os.path.join(diag_dir, "B_pre_blit_final_image.png")))[:, :, 0]
c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, 0]
vo_mask = np.array(Image.open(os.path.join(diag_dir, "vo_silhouette_left_exact.png")))[:, :, 0] > 0

# B の黒画素（遮蔽判定）
b_occ_direct = b_img < 128
b_occ_flp = np.fliplr(b_occ_direct)

# C の黒画素（遮蔽判定）
c_occ = c_img < 128

print(f"VO総画素数: {np.count_nonzero(vo_mask):,} px")

def eval_pair(name_a, mask_a, name_b, mask_b, mask_roi):
    # ROI (VO領域) 内での評価
    # True = 遮蔽 (Occ), False = 可視 (Vis)
    a = mask_a[mask_roi]
    b = mask_b[mask_roi]
    
    tp = np.count_nonzero(a & b)
    tn = np.count_nonzero((~a) & (~b))
    fn = np.count_nonzero(a & (~b)) # aが遮蔽、bが可視
    fp = np.count_nonzero((~a) & b) # aが可視、bが遮蔽
    
    total = len(a)
    mismatch = fn + fp
    print(f"\n--- [{name_a} vs {name_b}] ---")
    print(f"  {name_a} 遮蔽: {np.count_nonzero(a):,} px")
    print(f"  {name_b} 遮蔽: {np.count_nonzero(b):,} px")
    print(f"  TP (両方遮蔽):      {tp:>9,} px")
    print(f"  TN (両方可視):      {tn:>9,} px")
    print(f"  FN ({name_a}遮蔽・{name_b}可視): {fn:>9,} px")
    print(f"  FP ({name_a}可視・{name_b}遮蔽): {fp:>9,} px")
    print(f"  不一致 (FN+FP):     {mismatch:>9,} px ({mismatch/total*100:.4f}%)")
    print(f"  一致率:             {(tp+tn)/total*100:.4f}%")

# 1. A_flipped vs C (VO内)
eval_pair("A_flipped", a_flp, "C_final", c_occ, vo_mask)

# 2. A_direct vs C (VO内)
eval_pair("A_direct", a_dir, "C_final", c_occ, vo_mask)

# 3. B_flipped vs C (VO内)
eval_pair("B_flipped", b_occ_flp, "C_final", c_occ, vo_mask)

# 4. B_direct vs C (VO内)
eval_pair("B_direct", b_occ_direct, "C_final", c_occ, vo_mask)

# 5. A_flipped vs B_flipped (VO内)
eval_pair("A_flipped", a_flp, "B_flipped", b_occ_flp, vo_mask)

# 6. A_direct vs B_direct (VO内)
eval_pair("A_direct", a_dir, "B_direct", b_occ_direct, vo_mask)
