# File: Assets/Features/SICESI/Python/evaluate_whole_vo.py
import csv
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import numpy as np
from PIL import Image

ROOT = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest"
OUT_DIR = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RuleOptimizationResults_WholeVO"
CASES = list(range(1, 7))
DENSITIES = ["0.25", "1.0", "4.0"]
EYES = ["Left", "Right"]

EXPECTED_VO = 51_767_697
EXPECTED_GT_OCC = 20_212_446


def load(path):
    return np.array(Image.open(path).convert("RGB"))[:, :, 0] > 128


def lmax_of(m):
    if m == 0:
        return 8
    bits = [(m >> i) & 1 for i in range(8)] * 2
    best = run = 0
    for b in bits:
        run = run + 1 if b == 0 else 0
        best = max(best, run)
    return best


def rot_of(m):
    return min(((m << r) | (m >> (8 - r))) & 0xFF for r in range(8))


def tex_int(x):
    return f"{int(x):,}".replace(",", "{,}")


def to_hex(z):
    return f"0x{sum(int(z[m]) << m for m in range(256)):064X}"


POP = np.array([bin(m).count("1") for m in range(256)])
LMAX = np.array([lmax_of(m) for m in range(256)])
ROT = np.array([rot_of(m) for m in range(256)])
CLASS_REPS = sorted(set(ROT.tolist()))
assert len(CLASS_REPS) == 36
CLASS_GROUPS = [np.where(ROT == c)[0] for c in CLASS_REPS]
PATTERN_GROUPS = [np.array([m]) for m in range(256)]

# ------------------------------------------------------------
# 1. 評価領域全体（vo）でパターン別 V, O を集計
# ------------------------------------------------------------
meta, V_list, O_list = [], [], []
for case in CASES:
    s_dir = os.path.join(ROOT, str(case), "Sector8MaskSweep")
    for d in DENSITIES:
        for eye in EYES:
            e = eye.lower()
            eye_dir = os.path.join(s_dir, f"density_{d}pts_mm2", eye)
            gt_dir = os.path.join(s_dir, "GT", eye)
            vo = load(os.path.join(gt_dir, f"vo_silhouette_{e}.png"))
            gt = load(os.path.join(gt_dir, f"gt_{e}.png"))
            occ = np.zeros(vo.shape, dtype=np.uint8)
            for i in range(8):
                sec = load(os.path.join(eye_dir, f"sector_{i}_mask_{e}.png"))
                occ |= (sec.astype(np.uint8) << i)
            V_list.append(np.bincount(occ[vo & gt], minlength=256))
            O_list.append(np.bincount(occ[vo & ~gt], minlength=256))
            meta.append((case, d, eye))

V = np.array(V_list, dtype=np.int64)
O = np.array(O_list, dtype=np.int64)
G = V.sum(axis=1)
Vp = V.sum(axis=0)
Op = O.sum(axis=0)
assert V.sum() + O.sum() == EXPECTED_VO, f"VO画素数不一致: {V.sum() + O.sum():,}"
assert O.sum() == EXPECTED_GT_OCC, f"GT遮蔽画素数不一致: {O.sum():,}"
print(f"画像条件数: {len(meta)}, VO画素 {V.sum() + O.sum():,}, GT可視 {V.sum():,}, GT遮蔽 {O.sum():,}")


# ------------------------------------------------------------
# 2. 評価関数（z[m] = 1 で可視）
# ------------------------------------------------------------
def ious(z):
    return (V @ z) / (G + O @ z)


def mean_iou(z):
    return float(ious(z).mean())


def local_search(z0, groups):
    z = z0.copy()
    best = mean_iou(z)
    flips = 0
    while True:
        cand_best, cand_g = best, None
        for g in groups:
            z2 = z.copy()
            z2[g] = 1 - z2[g]
            s = mean_iou(z2)
            if s > cand_best + 1e-12:
                cand_best, cand_g = s, g
        if cand_g is None:
            return z, best, flips
        z[cand_g] = 1 - z[cand_g]
        best = cand_best
        flips += 1


# 占有数判定
print("\n--- 占有数判定（可視: N_occ < N_th） ---")
occ_rules = {n: (POP < n).astype(np.int64) for n in range(1, 9)}
for n, z in occ_rules.items():
    print(f"N_th={n}: {100 * mean_iou(z):.4f}%")
n_occ = max(occ_rules, key=lambda n: mean_iou(occ_rules[n]))
z_occ = occ_rules[n_occ]

# 20規則
sigs = {}
for n in range(1, 9):
    for l in range(0, 9):
        z = (~((POP >= n) & (LMAX <= l))).astype(np.int64)
        sigs.setdefault(z.tobytes(), (z, []))[1].append((n, l))
rules20 = list(sigs.values())
print(f"\n--- 20規則（異なる規則数: {len(rules20)}） ---")
z_r20, pairs_r20 = max(rules20, key=lambda t: mean_iou(t[0]))
print(f"選択: 同値な (N_th, L_th) = {pairs_r20}, {100 * mean_iou(z_r20):.4f}%")

# 多数決
z_maj = (Vp >= Op).astype(np.int64)
z_cls_maj = np.zeros(256, dtype=np.int64)
for g in CLASS_GROUPS:
    z_cls_maj[g] = int(Vp[g].sum() >= Op[g].sum())

# 36クラスLUT
print("\n--- 36クラスLUT 局所探索 ---")
res36 = []
for name, z0 in [("占有数判定", z_occ), ("連続非占有規則", z_r20), ("クラス多数決", z_cls_maj)]:
    z, s, f = local_search(z0, CLASS_GROUPS)
    res36.append((s, z))
    print(f"初期値 {name}: {100 * mean_iou(z0):.4f}% -> {100 * s:.4f}%（反転 {f} 回）")
z_36 = max(res36, key=lambda t: t[0])[1]

# 256パターンLUT
print("\n--- 256パターンLUT 局所探索 ---")
res256 = []
for name, z0 in [("36クラスLUT", z_36), ("多数決", z_maj), ("占有数判定", z_occ), ("連続非占有規則", z_r20)]:
    z, s, f = local_search(z0, PATTERN_GROUPS)
    res256.append((s, z))
    print(f"初期値 {name}: {100 * mean_iou(z0):.4f}% -> {100 * s:.4f}%（反転 {f} 回）")
z_256 = max(res256, key=lambda t: t[0])[1]

# ------------------------------------------------------------
# 3. 比較表
# ------------------------------------------------------------
methods = [
    ("占有数判定", z_occ),
    ("連続非占有規則", z_r20),
    ("36クラスLUT", z_36),
    ("256パターンLUT", z_256),
    ("多数決割当て", z_maj),
]
i_occ = ious(z_occ)
lb = int(np.minimum(Vp, Op).sum())

print(f"\n選択規則: 占有数判定 N_th={n_occ}, 連続非占有規則 {pairs_r20}")
print("% 表1: 方式 & 平均IoU[%] & 差[pt] & 最大悪化[pt] & 遮蔽漏れ[x10^3] & 過剰遮蔽[x10^3] & 合計[x10^3]")
rows_out = []
for name, z in methods:
    i = ious(z)
    fp = int(Op @ z)
    fn = int(Vp @ (1 - z))
    diff = 100 * (i.mean() - i_occ.mean())
    worst = max(0.0, -100 * (i - i_occ).min())
    print(f"{name} & {100 * i.mean():.2f} & ${diff:+.2f}$ & {worst:.2f} & "
          f"{fp / 1e3:.1f} & {fn / 1e3:.1f} & {(fp + fn) / 1e3:.1f} \\\\"
          f"  % min IoU {i.min():.6f}, FP {fp:,}, FN {fn:,}")
    rows_out.append((name, i))

occ_err = int(Op @ z_occ + Vp @ (1 - z_occ))
r20_err = int(Op @ z_r20 + Vp @ (1 - z_r20))
print(f"\n下限 {lb:,} = 占有数判定の誤りの {100 * lb / occ_err:.1f}%, 連続非占有規則の誤りの {100 * lb / r20_err:.1f}%")
print(f"連続非占有規則 vs 占有数判定: 遮蔽漏れ {int(Op @ z_r20 - Op @ z_occ):+,}, "
      f"過剰遮蔽 {int(Vp @ (1 - z_r20) - Vp @ (1 - z_occ)):+,}")
print(f"36クラスLUT HEX : {to_hex(z_36)}（可視 {int(z_36.sum())}/256）")
print(f"256パターンLUT HEX: {to_hex(z_256)}（可視 {int(z_256.sum())}/256）")

# ------------------------------------------------------------
# 4. 配置別・密度別の差（pgfplots 用）
# ------------------------------------------------------------
cases = np.array([m[0] for m in meta])
dens = np.array([m[1] for m in meta])
diffs = {k: 100 * (ious(z) - i_occ) for k, z in [("r20", z_r20), ("r36", z_36), ("r256", z_256)]}
print("\n% siCaseDifference")
print("case r20 r36 r256")
for c in CASES:
    s = cases == c
    print(f"{c} " + " ".join(f"{diffs[k][s].mean():.2f}" for k in ["r20", "r36", "r256"]))
print("% siDensityDifference")
print("idx r20 r36 r256")
for idx, d in enumerate(DENSITIES, start=1):
    s = dens == d
    print(f"{idx} " + " ".join(f"{diffs[k][s].mean():.2f}" for k in ["r20", "r36", "r256"]))
print("% 密度別の占有数判定の平均IoU")
for d in DENSITIES:
    print(f"{d}: {100 * i_occ[dens == d].mean():.2f}%")

# ------------------------------------------------------------
# 5. 上位パターン・mask 0・半平面型
# ------------------------------------------------------------
observed = [m for m in range(256) if Vp[m] + Op[m] > 0]
order = sorted(observed, key=lambda m: -min(Vp[m], Op[m]))
print(f"\n% 表2（観測 {len(observed)} パターン）")
cum = 0
for m in order[:5]:
    n = Vp[m] + Op[m]
    mn = min(Vp[m], Op[m])
    cum += mn
    print(f"\\texttt{{{m:08b}}} & {POP[m]} & {n / 1e3:.1f} & {100 * Vp[m] / n:.1f} & "
          f"{mn / 1e3:.1f} & {100 * mn / lb:.1f} \\\\  % 累積 {100 * cum / lb:.1f}%, 判定(r20)={'可視' if z_r20[m] else '遮蔽'}")
oth = order[5:]
ov, oo = Vp[oth].sum(), Op[oth].sum()
omn = np.minimum(Vp[oth], Op[oth]).sum()
print(f"その他（{len(oth)}パターン） & -- & {(ov + oo) / 1e3:.1f} & {100 * ov / (ov + oo):.1f} & "
      f"{omn / 1e3:.1f} & {100 * omn / lb:.1f} \\\\")
print(f"合計 & -- & {(Vp.sum() + Op.sum()) / 1e3:.1f} & {100 * Vp.sum() / (Vp.sum() + Op.sum()):.1f} & "
      f"{lb / 1e3:.1f} & 100.0 \\\\")

print("\nmask 0 の密度別:")
for d in DENSITIES:
    s = dens == d
    v0, o0 = V[s, 0].sum(), O[s, 0].sum()
    print(f"  {d}: V={v0:,}, O={o0:,}, GT可視 {100 * v0 / (v0 + o0):.2f}%")

hp = rot_of(0b00011111)
hpm = np.where(ROT == hp)[0]
print(f"\n半平面クラス: 画素 {int(Vp[hpm].sum() + Op[hpm].sum()):,}, "
      f"GT可視 {100 * Vp[hpm].sum() / (Vp[hpm].sum() + Op[hpm].sum()):.1f}%, "
      f"下限 {int(np.minimum(Vp[hpm], Op[hpm]).sum()):,}, r20判定 {set(int(z_r20[m]) for m in hpm)}")
for m in (31, 241):
    for d in DENSITIES:
        s = dens == d
        v, o = V[s, m].sum(), O[s, m].sum()
        print(f"  mask {m} 密度 {d}: GT可視 {100 * v / (v + o):.1f}%")

# ------------------------------------------------------------
# 6. 保存
# ------------------------------------------------------------
os.makedirs(OUT_DIR, exist_ok=True)
with open(os.path.join(OUT_DIR, "pattern_VO_wholeVO.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["mask", "V", "O"])
    for m in range(256):
        w.writerow([m, int(Vp[m]), int(Op[m])])
with open(os.path.join(OUT_DIR, "rule_evaluation_wholeVO.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["case", "density", "eye"] + [name for name, _ in rows_out])
    for k, (c, d, e) in enumerate(meta):
        w.writerow([c, d, e] + [f"{i[k]:.6f}" for _, i in rows_out])
print(f"\n保存先: {OUT_DIR}")
