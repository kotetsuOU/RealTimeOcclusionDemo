# File: Assets/Features/SICESI/Python/verify_items_1_to_6.py
import os
import sys
import csv
import re
import numpy as np
from PIL import Image

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

def run_verifications():
    print("=" * 80)
    print("【確認項目 自動検算スクリプト】")
    print("=" * 80)

    # パス設定
    script_dir = os.path.dirname(os.path.abspath(__file__))
    dataset_root = r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset"
    rawtest_dir = os.path.join(dataset_root, "RawTest")
    rule_results_dir = os.path.join(dataset_root, "RuleOptimizationResults")
    pattern_vo_path = os.path.join(rule_results_dir, "pattern_VO.csv")
    common_eval_path = os.path.join(rule_results_dir, "common_rule_evaluation.csv")

    densities = ["0.25", "1.0", "4.0"]
    eyes = ["Left", "Right"]

    # --------------------------------------------------------------------------
    # #1 対象画素数の照合 (36条件)
    # --------------------------------------------------------------------------
    print("\n--- #1 対象画素数の照合 ---")
    total_target_pixels_from_images = 0
    total_V_from_images = 0
    total_O_from_images = 0

    for case_num in range(1, 7):
        case_dir = os.path.join(rawtest_dir, str(case_num), "Sector8MaskSweep")
        gt_dir = os.path.join(case_dir, "GT")
        for d_str in densities:
            d_dir = os.path.join(case_dir, f"density_{d_str}pts_mm2")
            for eye in eyes:
                eye_dir = os.path.join(d_dir, eye)
                gt_path = os.path.join(gt_dir, eye, f"gt_{eye.lower()}.png")
                vo_path = os.path.join(gt_dir, eye, f"vo_silhouette_{eye.lower()}.png")
                pt_path = os.path.join(eye_dir, f"point_mask_{eye.lower()}.png")

                gt = np.array(Image.open(gt_path))[:, :, 0] > 128
                vo = np.array(Image.open(vo_path))[:, :, 0] > 128
                pt = np.array(Image.open(pt_path))[:, :, 0] > 128

                target = vo & (~pt)
                total_target_pixels_from_images += int(np.count_nonzero(target))
                total_V_from_images += int(np.count_nonzero(target & gt))
                total_O_from_images += int(np.count_nonzero(target & (~gt)))

    # pattern_VO.csv からの読み込み
    V_csv = {}
    O_csv = {}
    with open(pattern_vo_path, newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for r in reader:
            m = int(r["mask"])
            V_csv[m] = int(r["V"])
            O_csv[m] = int(r["O"])

    sum_V_csv = sum(V_csv.values())
    sum_O_csv = sum(O_csv.values())
    sum_VO_csv = sum_V_csv + sum_O_csv
    diff_images_csv = total_target_pixels_from_images - sum_VO_csv

    print(f"対象画素数の合計 (36条件画像実測) : {total_target_pixels_from_images:,}")
    print(f"pattern_VO.csv の ΣV + ΣO         : {sum_VO_csv:,} (ΣV={sum_V_csv:,}, ΣO={sum_O_csv:,})")
    print(f"両者の差 (画像合計 - CSV合計)    : {diff_images_csv}")

    # --------------------------------------------------------------------------
    # #2 配置別・密度別の平均IoU差
    # --------------------------------------------------------------------------
    print("\n--- #2 配置別・密度別の平均IoU差 ---")
    rows_eval = []
    with open(common_eval_path, newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for r in reader:
            if "MEAN" in str(r.get("data_id", "")) or r.get("density") == "ALL" or "MEAN" in str(r.get("case", "")):
                continue
            rows_eval.append(r)

    # 差分計算 (pt = IoU * 100)
    for r in rows_eval:
        c8 = float(r["common_8_iou"])
        r["diff_r20"] = (float(r["common_20_iou"]) - c8) * 100.0
        r["diff_r36"] = (float(r["common_36_iou_objA"]) - c8) * 100.0
        r["diff_r256"] = (float(r["common_256_iou_objA"]) - c8) * 100.0
        case_str = r["case"]
        m_case = re.search(r"(\d+)", case_str)
        r["case_num"] = int(m_case.group(1)) if m_case else -1
        r["density_val"] = float(r["density"])

    print("case  r20   r36   r256")
    for c in range(1, 7):
        c_rows = [r for r in rows_eval if r["case_num"] == c]
        avg_r20 = np.mean([r["diff_r20"] for r in c_rows])
        avg_r36 = np.mean([r["diff_r36"] for r in c_rows])
        avg_r256 = np.mean([r["diff_r256"] for r in c_rows])
        print(f"{c:<5d} {avg_r20:5.2f} {avg_r36:5.2f} {avg_r256:5.2f}")

    print("density  r20   r36   r256")
    for d_val, d_str in [(0.25, "0.25"), (1.0, "1"), (4.0, "4")]:
        d_rows = [r for r in rows_eval if abs(r["density_val"] - d_val) < 1e-4]
        avg_r20 = np.mean([r["diff_r20"] for r in d_rows])
        avg_r36 = np.mean([r["diff_r36"] for r in d_rows])
        avg_r256 = np.mean([r["diff_r256"] for r in d_rows])
        print(f"{d_str:<8s} {avg_r20:5.2f} {avg_r36:5.2f} {avg_r256:5.2f}")

    # --------------------------------------------------------------------------
    # #3 規則の数
    # --------------------------------------------------------------------------
    print("\n--- #3 規則の数 ---")
    def popcount(m):
        return bin(m).count("1")

    def compute_lmax(m):
        if m == 0:
            return 8
        bits = [(m >> i) & 1 for i in range(8)] * 2
        best = run = 0
        for b in bits:
            run = run + 1 if b == 0 else 0
            best = max(best, run)
        return min(best, 8)

    occ_arr = np.array([popcount(m) for m in range(256)], dtype=int)
    lmax_arr = np.array([compute_lmax(m) for m in range(256)], dtype=int)

    rule_signatures = {}
    pair_to_sig = {}

    for n_th in range(1, 9):
        for l_th in range(0, 9):
            # N_occ >= N_th かつ L_max <= L_th なら遮蔽 (0)、それ以外は可視 (1)
            # ※ 判定配列: 1=可視, 0=遮蔽
            is_occluded = (occ_arr >= n_th) & (lmax_arr <= l_th)
            is_visible = ~is_occluded
            sig = tuple(is_visible.astype(int))
            pair_to_sig[(n_th, l_th)] = sig
            if sig not in rule_signatures:
                rule_signatures[sig] = []
            rule_signatures[sig].append((n_th, l_th))

    num_distinct_rules = len(rule_signatures)
    print(f"ユニークな判定規則の種類数: {num_distinct_rules} 種類 (期待値: 20種類)")

    # (2, 3) と (5, 8) の同値ペア確認
    sig_2_3 = pair_to_sig[(2, 3)]
    sig_5_8 = pair_to_sig[(5, 8)]

    print(f"(N_th=2, L_th=3) と一致する (N_th, L_th) の組: {rule_signatures[sig_2_3]}")
    print(f"(N_th=5, L_th=8) と一致する (N_th, L_th) の組: {rule_signatures[sig_5_8]}")

if __name__ == "__main__":
    run_verifications()
