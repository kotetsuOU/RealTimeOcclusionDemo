import os
import numpy as np
from PIL import Image

diag_dir = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest\5\StageDiagnosis"

c_img = np.array(Image.open(os.path.join(diag_dir, "C_final_display_output.png")))[:, :, 0]
inter_vals = c_img[(c_img > 0) & (c_img < 255)]

vals, counts = np.unique(inter_vals, return_counts=True)
print(f"中間値のユニーク値数: {len(vals)}")
print("頻度上位 15 個:")
sorted_idx = np.argsort(-counts)
for i in sorted_idx[:15]:
    print(f"  値 {vals[i]:3d}: {counts[i]:5d} px ({counts[i]/len(inter_vals)*100:.2f}%)")

# vo_silhouette_left_exact.png の中間値も見てみる
vo_img = np.array(Image.open(os.path.join(diag_dir, "vo_silhouette_left_exact.png")))[:, :, 0]
vo_inter = (vo_img > 0) & (vo_img < 255)
print(f"\nvo_silhouette_left_exact の中間値画素数: {np.count_nonzero(vo_inter):,} px")
