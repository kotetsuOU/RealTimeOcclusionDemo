# File: offline_eval.py
import os, csv
import cv2
import numpy as np

ROOT = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest"
SCENES = {1: r"2\Sector8MaskSweep"}  # フォルダ 2\Sector8MaskSweep (ユーザー検証用配置1)
DENSITIES = ["0.25", "1.0", "4.0", "16.0", "64.0"]

def read_bin(p):
    img = cv2.imread(p, cv2.IMREAD_GRAYSCALE)
    if img is None:
        raise FileNotFoundError(p)
    return img > 128

def read_occ(dd, e):
    """
    8セクター二値マスク (sector_0_mask 〜 sector_7_mask) から完全一致・ガンマ歪みゼロの
    1チャンネル 8-bit uint8 占有パターン画像を合成・生成します。
    """
    occ = np.zeros((2160, 3840), dtype=np.uint8)
    for s in range(8):
        p = os.path.join(dd, f"sector_{s}_mask_{e}.png")
        b = cv2.imread(p, cv2.IMREAD_GRAYSCALE)
        if b is None:
            # フォールバック: sector_mask_{e}.png を直接読む
            fallback_p = os.path.join(dd, f"sector_mask_{e}.png")
            fb = cv2.imread(fallback_p, cv2.IMREAD_GRAYSCALE)
            if fb is None:
                raise FileNotFoundError(p)
            return fb
        occ |= ((b > 128).astype(np.uint8) << s)
    return occ

popcount = np.array([bin(m).count("1") for m in range(256)])

def max_empty_run(m):
    bits = [(m >> i) & 1 for i in range(8)] * 2
    best = run = 0
    for b in bits:
        run = run + 1 if b == 0 else 0
        best = max(best, run)
    return min(best, 8)

maxrun = np.array([max_empty_run(m) for m in range(256)])
rot_class = np.array([min(((m << r) | (m >> (8 - r))) & 0xFF for r in range(8)) for m in range(256)])

conds = []
print("=== 実測データ読み込み・画素数確認 ===")
for sid, sub in SCENES.items():
    base = os.path.join(ROOT, sub)
    for eye, e in [("Left", "left"), ("Right", "right")]:
        vo = read_bin(os.path.join(base, "GT", eye, f"vo_silhouette_{e}.png"))
        gt_vis = read_bin(os.path.join(base, "GT", eye, f"gt_{e}.png")) & vo
        for d in DENSITIES:
            dd = os.path.join(base, f"density_{d}pts_mm2", eye)
            pm = read_bin(os.path.join(dd, f"point_mask_{e}.png"))
            occ = read_occ(dd, e)
            target = vo & ~pm
            V = np.bincount(occ[target & gt_vis], minlength=256)
            O = np.bincount(occ[target & ~gt_vis], minlength=256)
            fixed_fn = int(np.count_nonzero(vo & pm & gt_vis))
            conds.append(dict(scene=sid, density=d, eye=e, V=V, O=O, fixed_fn=fixed_fn))
            print(f"scene{sid} d={d:4s} {e:5s}: point_px_in_vo={np.count_nonzero(vo & pm):7d}, target_px={np.count_nonzero(target):7d}")

def iou(rule, c):
    vis = ~rule
    tp = c["V"][vis].sum()
    fp = c["O"][vis].sum()
    fn = c["V"][rule].sum() + c["fixed_fn"]
    denom = tp + fp + fn
    return tp / denom if denom > 0 else 0.0

Vt = sum(c["V"] for c in conds)
Ot = sum(c["O"] for c in conds)

rules = {}
for k in range(1, 9):
    rules[f"count>={k}"] = popcount >= k
for r in range(0, 8):
    rules[f"max_empty_run<={r}"] = maxrun <= r
rules["LUT256_majority"] = Ot > Vt
cls_V = np.zeros(256); cls_O = np.zeros(256)
np.add.at(cls_V, rot_class, Vt); np.add.at(cls_O, rot_class, Ot)
rules["LUT36_majority"] = cls_O[rot_class] > cls_V[rot_class]

rows = []
for name, rule in rules.items():
    vals = [iou(rule, c) for c in conds]
    rows.append([name, np.mean(vals), np.min(vals), len(vals)])
rows.sort(key=lambda x: -x[1])

out_csv1 = os.path.join(ROOT, "method_summary.csv")
with open(out_csv1, "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["method", "mean_IoU", "min_IoU", "n_conditions"])
    w.writerows([[r[0], f"{r[1]:.6f}", f"{r[2]:.6f}", r[3]] for r in rows])

# カレントディレクトリにも保存
with open("method_summary.csv", "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["method", "mean_IoU", "min_IoU", "n_conditions"])
    w.writerows([[r[0], f"{r[1]:.6f}", f"{r[2]:.6f}", r[3]] for r in rows])

out_csv2 = os.path.join(ROOT, "pattern_summary_256.csv")
with open(out_csv2, "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["mask", "bits_b7_b0", "popcount", "max_empty_run", "gt_visible", "gt_occluded",
                "visible_ratio", "E_common", "E_individual", "C_conflict"])
    for m in range(256):
        n = Vt[m] + Ot[m]
        e_ind = sum(min(c["V"][m], c["O"][m]) for c in conds)
        e_com = min(Vt[m], Ot[m])
        w.writerow([m, format(m, "08b"), popcount[m], maxrun[m], Vt[m], Ot[m],
                    f"{Vt[m] / n:.6f}" if n > 0 else "", e_com, e_ind, e_com - e_ind])

with open("pattern_summary_256.csv", "w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["mask", "bits_b7_b0", "popcount", "max_empty_run", "gt_visible", "gt_occluded",
                "visible_ratio", "E_common", "E_individual", "C_conflict"])
    for m in range(256):
        n = Vt[m] + Ot[m]
        e_ind = sum(min(c["V"][m], c["O"][m]) for c in conds)
        e_com = min(Vt[m], Ot[m])
        w.writerow([m, format(m, "08b"), popcount[m], maxrun[m], Vt[m], Ot[m],
                    f"{Vt[m] / n:.6f}" if n > 0 else "", e_com, e_ind, e_com - e_ind])

print("\n=== トップ方式 (上位10件) ===")
for r in rows[:10]:
    print(f"{r[0]:22s} mean_IoU={r[1]:.4f}")

print(f"\n[完了] 出力完了:\n- {os.path.abspath('method_summary.csv')}\n- {os.path.abspath('pattern_summary_256.csv')}")
