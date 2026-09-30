# File: Assets/Features/SICESI/Python/make_pattern_figure_data.py
import csv
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RESULT_DIR = os.path.abspath(os.path.join(
    SCRIPT_DIR, "../../../../../Estimation/SICESI_Dataset/RuleOptimizationResults"))
VO_PATH = os.path.join(RESULT_DIR, "pattern_VO.csv")
SUMMARY_PATH = os.path.join(RESULT_DIR, "common_rules_summary.csv")
OUT_DIRS = [
    os.path.abspath(os.path.join(SCRIPT_DIR, "..", "Data", "figures", "data")),
    os.path.join(RESULT_DIR, "figures", "data"),
    os.path.abspath(os.path.join(os.getcwd(), "figures", "data")),
]

OBJECTIVE = "Mean_IoU_Maximized"
METHOD_KEYS = {
    "Fixed_8_Candidates": "occ",
    "Fixed_20_Candidates": "r20",
    "Oracle_36_RotationLUT": "lut36",
    "Oracle_256_ExactLUT": "lut256",
}
NAMES = {
    "occ": "占有数判定",
    "r20": "連続非占有規則",
    "lut36": "36クラスLUT",
    "lut256": "256パターンLUT",
}
TOP_K = 5


def pop(m):
    return bin(m).count("1")


def lmax(m):
    if m == 0:
        return 8
    bits = [(m >> i) & 1 for i in range(8)] * 2
    best = run = 0
    for b in bits:
        run = run + 1 if b == 0 else 0
        best = max(best, run)
    return best


def rot(m):
    return min(((m << r) | (m >> (8 - r))) & 0xFF for r in range(8))


def tex_int(x):
    return f"{x:,}".replace(",", "{,}")


# ------------------------------------------------------------
# 規則の読込（1 = 可視，bit m がパターン m の判定）
# ------------------------------------------------------------
HEX = {}
with open(SUMMARY_PATH, newline="", encoding="utf-8-sig") as f:
    for r in csv.DictReader(f):
        if r["objective"] == OBJECTIVE and r["method"] in METHOD_KEYS:
            HEX[METHOD_KEYS[r["method"]]] = int(r["hex_mask"], 16)
assert set(HEX) == set(NAMES), f"summary に不足: {set(NAMES) - set(HEX)}"


def vis(rule, m):
    return (HEX[rule] >> m) & 1 == 1


# 占有数判定：vis == (pop < k) となる k を特定
occ_k = [k for k in range(1, 10) if all(vis("occ", m) == (pop(m) < k) for m in range(256))]
assert len(occ_k) == 1, "占有数判定の HEX が単一閾値で表せない"
n_th_occ = occ_k[0]

# 連続非占有規則：遮蔽 = (pop >= N_th and lmax <= L_th) となる組をすべて列挙
r20_pairs = [
    (n, l) for n in range(1, 9) for l in range(0, 9)
    if all(vis("r20", m) == (not (pop(m) >= n and lmax(m) <= l)) for m in range(256))
]
assert r20_pairs, "連続非占有規則の HEX が (N_th, L_th) で表せない"

# 36クラスLUT：回転不変
assert all(vis("lut36", m) == vis("lut36", rot(m)) for m in range(256)), "36クラスLUTが回転不変でない"

print(f"占有数判定: N_th = {n_th_occ}")
print(f"連続非占有規則: 同値な (N_th, L_th) = {r20_pairs}")

# ------------------------------------------------------------
# パターン別 V, O
# ------------------------------------------------------------
V, O = {}, {}
with open(VO_PATH, newline="", encoding="utf-8-sig") as f:
    for r in csv.DictReader(f):
        V[int(r["mask"])] = int(r["V"])
        O[int(r["mask"])] = int(r["O"])
assert len(V) == 256


def fp_fn(assign):
    fp = sum(O[m] for m in range(256) if assign(m))
    fn = sum(V[m] for m in range(256) if not assign(m))
    return fp, fn


results = {k: fp_fn(lambda m, k=k: vis(k, m)) for k in NAMES}
lb_fp, lb_fn = fp_fn(lambda m: V[m] >= O[m])
lb = lb_fp + lb_fn
assert lb == sum(min(V[m], O[m]) for m in range(256))

print("\n% 表の行: 方式 & 遮蔽漏れ & 過剰遮蔽 & 合計")
for k in NAMES:
    fp, fn = results[k]
    print(f"{NAMES[k]} & {tex_int(fp)} & {tex_int(fn)} & {tex_int(fp + fn)} \\\\")
print(f"多数決割当て（下限） & {tex_int(lb_fp)} & {tex_int(lb_fn)} & {tex_int(lb)} \\\\")

occ_err = sum(results["occ"])
r20_err = sum(results["r20"])
print("\n--- 本文用 ---")
print(f"連続非占有規則 vs 占有数判定: 遮蔽漏れ {results['r20'][0] - results['occ'][0]:+,}, "
      f"過剰遮蔽 {results['r20'][1] - results['occ'][1]:+,}")
print(f"下限 / 占有数判定の誤り     : {100 * lb / occ_err:.1f}%")
print(f"下限 / 連続非占有規則の誤り : {100 * lb / r20_err:.1f}%")
print(f"割当て変更で除去できる誤り（連続非占有規則基準）: {r20_err - lb:,}")

# ------------------------------------------------------------
# 上位パターン（min(V,O) 降順）
# ------------------------------------------------------------
observed = [m for m in range(256) if V[m] + O[m] > 0]
order = sorted(observed, key=lambda m: -min(V[m], O[m]))
top = order[:TOP_K]
others = order[TOP_K:]
print(f"\n観測パターン数: {len(observed)}（未観測 {256 - len(observed)}）")
print("% 表2の行: パターン & 占有数 & 画素数[x10^3] & GT可視[%] & min[x10^3] & 割合[%]")
cum = 0
for m in top:
    n = V[m] + O[m]
    mn = min(V[m], O[m])
    cum += mn
    print(f"\\texttt{{{m:08b}}} & {pop(m)} & {n / 1e3:.1f} & {100 * V[m] / n:.1f} & "
          f"{mn / 1e3:.1f} & {100 * mn / lb:.1f} \\\\  % 累積 {100 * cum / lb:.1f}%")
ov = sum(V[m] for m in others)
oo = sum(O[m] for m in others)
omn = sum(min(V[m], O[m]) for m in others)
print(f"その他（{len(others)}パターン） & -- & {(ov + oo) / 1e3:.1f} & {100 * ov / (ov + oo):.1f} & "
      f"{omn / 1e3:.1f} & {100 * omn / lb:.1f} \\\\")
tv = sum(V.values())
to = sum(O.values())
print(f"合計 & -- & {(tv + to) / 1e3:.1f} & {100 * tv / (tv + to):.1f} & {lb / 1e3:.1f} & 100.0 \\\\")

# ------------------------------------------------------------
# 半平面クラス（00011111 の回転同値類）
# ------------------------------------------------------------
hp = rot(0b00011111)
hp_members = [m for m in range(256) if rot(m) == hp]
hp_v = sum(V[m] for m in hp_members)
hp_o = sum(O[m] for m in hp_members)
hp_min = sum(min(V[m], O[m]) for m in hp_members)
hp_assign = {m: ("可視" if vis("r20", m) else "遮蔽") for m in hp_members}
print(f"\n半平面クラス: {[f'{m:08b}' for m in hp_members]}")
print(f"  連続非占有規則での判定: {set(hp_assign.values())}")
print(f"  画素数 {hp_v + hp_o:,}, GT可視 {100 * hp_v / (hp_v + hp_o):.1f}%, "
      f"下限 {hp_min:,}（下限の {100 * hp_min / lb:.1f}%）")
print(f"  31+241 の下限: {min(V[31], O[31]) + min(V[241], O[241]):,}"
      f"（下限の {100 * (min(V[31], O[31]) + min(V[241], O[241])) / lb:.1f}%）")

# ------------------------------------------------------------
# 図データ（判定は選択された連続非占有規則）
# ------------------------------------------------------------
recs = []
for m in observed:
    n = V[m] + O[m]
    v = vis("r20", m)
    correct = V[m] if v else O[m]
    recs.append(dict(mask=m, bits=f"{m:08b}", pop=pop(m), lmax=lmax(m), cls=rot(m),
                     n=n, V=V[m], O=O[m], acc=100 * correct / n, err=n - correct, v=v))

for out_dir in OUT_DIRS:
    os.makedirs(out_dir, exist_ok=True)

    def write(name, cond):
        with open(os.path.join(out_dir, name), "w", encoding="utf-8") as f:
            f.write("mask bits pop lmax cls n V O acc err\n")
            for r in recs:
                if cond(r):
                    f.write(f"{r['mask']} {r['bits']} {r['pop']} {r['lmax']} {r['cls']} "
                            f"{r['n']} {r['V']} {r['O']} {r['acc']:.4f} {r['err']}\n")

    write("pattern_visible.dat", lambda r: r["v"])
    write("pattern_occluded.dat", lambda r: not r["v"])
    # 新規則では半平面クラスは可視側になるため，判定によらずクラス全体を出力する
    write("pattern_halfplane.dat", lambda r: r["cls"] == hp)

assert sum(r["err"] for r in recs) == r20_err
print(f"\n図データ出力先: {OUT_DIRS}")
