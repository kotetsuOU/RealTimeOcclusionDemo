"""
================================================================================
占有パターンCSVから個別最適・共通最適を求める自動解析スクリプト
(Feature: SICESI - Pattern Rule Optimization)
================================================================================

【概要】
  本スクリプトは、256パターンの占有マスク評価データ (pattern_counts_256.csv) を
  再帰的に探索・収集し、以下の4種類の判定器について個別最適および共通最適を求めます：
    1. 固定8候補      : 最低占有数 1..8 (N_occ < R が可視)
    2. 固定20候補     : 占有数・最大連続非占有数の20代表設定 (R_th, L_th)
    3. 固定36クラスLUT : 回転不変36クラスへの独立最適割当
    4. 固定256パターン : 256ビット完全独立最適割当

【自動集計機能】
  撮影直後のフォルダ (SectorMask_*.raw または pattern_*.png と GT があるフォルダ) で
  まだ pattern_counts_256.csv が未生成の場合は、自動的に高速集計して CSV を生成します。

【出力ファイル】
  - individual_optima.csv      : 全データ × 4方式の個別最適結果 (具体的ルール・内訳・HEX完全マスク付き)
  - common_rules_summary.csv   : 4方式の決定された共通規則の一覧サマリー
  - common_rule_evaluation.csv : 共通規則を全データへ適用した IoU および向上幅
  - individual_gap_analysis.csv: 個別最適からの性能低下幅 (汎化ギャップ) 分析
  - common_rule_8.csv          : 共通8候補の全マスク判定 (256行)
  - common_rule_20.csv         : 共通20候補の全マスク判定 (256行)
  - common_lut_36.csv          : 共通36クラスLUT判定 (36行)
  - common_lut_256.csv         : 共通256パターンLUT判定 (256行)

【実行方法】
  # デフォルト実行 (RawTest または RawTest_Cases または SICESI_Dataset 直下を自動探索)
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py

  # 探索ルート・出力先を明示指定
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py "<探索ルート>" "<出力先フォルダ>"
"""

import os
import sys
import glob
import time
import csv
import numpy as np
from PIL import Image
from scipy.optimize import milp, LinearConstraint, Bounds

# ==============================================================================
# 0. パターンのコンパクト表現・補助関数
# ==============================================================================
def compress_indices(indices, prefix=""):
    """
    連続する数値を 0-7,9,11 のようにレンジ圧縮してカンマ区切りにする
    例: [0, 1, 2, 3, 5, 7, 8, 9] -> "0-3,5,7-9"
    prefix="c" -> "c[0-3,5,7-9]"
    """
    if not indices:
        return "none"
    indices = sorted(indices)
    ranges = []
    start = indices[0]
    prev = start
    for idx in indices[1:]:
        if idx == prev + 1:
            prev = idx
        else:
            if start == prev:
                ranges.append(f"{start}")
            else:
                ranges.append(f"{start}-{prev}")
            start = idx
            prev = idx
    if start == prev:
        ranges.append(f"{start}")
    else:
        ranges.append(f"{start}-{prev}")
    s = ",".join(ranges)
    return f"{prefix}[{s}]" if prefix else s

def z_to_hex(z_256):
    """
    256ビット判定配列 (0または1の長さ256) を 64文字の 16進数文字列 (0x...) に変換
    z[0] が最下位ビット (bit 0), z[255] が最上位ビット (bit 255)
    """
    val = 0
    for i, bit in enumerate(z_256):
        if bit:
            val |= (1 << i)
    return f"0x{val:064X}"

def occ_breakdown_str(z_256, n_occ):
    """
    占有数 0〜8 ごとの可視パターン数を [v0,v1,v2,v3,v4,v5,v6,v7,v8] 形式で返す
    """
    counts = [0] * 9
    for m in range(256):
        if z_256[m]:
            counts[n_occ[m]] += 1
    return f"[{','.join(str(c) for c in counts)}]"

def class_breakdown_str(z_36, lut):
    """
    36クラス判定について、占有数 0〜8 ごとの可視クラス数を [c0,c1,...] 形式で返す
    """
    counts = [0] * 9
    for cid in range(36):
        if z_36[cid]:
            rep = lut['unique_reps'][cid]
            counts[lut['n_occ'][rep]] += 1
    return f"[{','.join(str(c) for c in counts)}]"


# ==============================================================================
# 1. ルックアップテーブル構築 & 特徴計算
# ==============================================================================
def build_256_lookup():
    """
    全256パターンの幾何学的属性・36回転クラス・21特徴のLUTを構築
    """
    n_occ = np.zeros(256, dtype=np.int32)
    l_max = np.zeros(256, dtype=np.int32)
    rot_rep = np.zeros(256, dtype=np.int32)
    
    for m in range(256):
        n_occ[m] = bin(m).count('1')
        b = [(m >> i) & 1 for i in range(8)]
        if n_occ[m] == 0:
            l_max[m] = 8
        elif n_occ[m] == 8:
            l_max[m] = 0
        else:
            b_doubled = b + b
            max_z = 0
            cur_z = 0
            for v in b_doubled:
                if v == 0:
                    cur_z += 1
                    if cur_z > max_z: max_z = cur_z
                else:
                    cur_z = 0
            l_max[m] = min(max_z, 8)
            
        min_shift = m
        cur = m
        for _ in range(7):
            cur = ((cur & 0x01) << 7) | (cur >> 1)
            if cur < min_shift: min_shift = cur
        rot_rep[m] = min_shift
        
    unique_reps = sorted(list(set(rot_rep)))
    rot_cid = np.zeros(256, dtype=np.int32)
    for m in range(256):
        rot_cid[m] = unique_reps.index(rot_rep[m])
        
    return {
        'n_occ': n_occ,
        'l_max': l_max,
        'rot_rep': rot_rep,
        'rot_cid': rot_cid,
        'unique_reps': unique_reps
    }

# 仕様書第6.1節: 20代表設定
RULES_20_CANDIDATES = [
    (1, 8),
    (2, 3), (2, 4), (2, 5), (2, 8),
    (3, 2), (3, 3), (3, 4), (3, 8),
    (4, 1), (4, 2), (4, 3), (4, 8),
    (5, 1), (5, 2), (5, 8),
    (6, 1), (6, 8),
    (7, 8),
    (8, 8)
]


# ==============================================================================
# 2. 未集計撮影データの高速自動集計 (auto_generate_missing_pattern_counts)
# ==============================================================================
def auto_generate_missing_pattern_counts(root_dir, lut):
    """
    root_dir 配下の全撮影ディレクトリ (Pattern256MaskSweep / SectorMaskSweep) を走査し、
    pattern_counts_256.csv がまだ生成されていない条件フォルダがあれば、
    SectorMask_*.raw (または pattern_*.png) と GT から pattern_counts_256.csv を超高速自動集計する。
    """
    sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Pattern256MaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "SectorMaskSweep"), recursive=True))
    sweep_dirs = sorted(list(set(sweep_dirs)))
    
    if not sweep_dirs:
        return
        
    missing_targets = []
    for s_dir in sweep_dirs:
        density_dirs = sorted(glob.glob(os.path.join(s_dir, "density_*pts_mm2")))
        for d_dir in density_dirs:
            for eye in ["Left", "Right"]:
                eye_dir = os.path.join(d_dir, eye)
                if not os.path.exists(eye_dir):
                    continue
                p1 = os.path.join(eye_dir, "Oracle256Analysis", "pattern_counts_256.csv")
                p2 = os.path.join(eye_dir, "pattern_counts_256.csv")
                if not (os.path.exists(p1) or os.path.exists(p2)):
                    missing_targets.append((s_dir, d_dir, eye, eye_dir))
                    
    if not missing_targets:
        return
        
    print(f"[*] 未集計の撮影データ ({len(missing_targets)} 件) を検出しました。超高速自動集計中...")
    generated_count = 0
    t0 = time.time()
    
    for s_dir, d_dir, eye, eye_dir in missing_targets:
        gt_path = os.path.join(s_dir, "GT", eye, f"gt_{eye.lower()}.png")
        vo_path = os.path.join(s_dir, "GT", eye, f"vo_silhouette_{eye.lower()}.png")
        
        if not os.path.exists(gt_path):
            continue
            
        gt_img = np.array(Image.open(gt_path))[:, :, 0]
        H, W = gt_img.shape
        gt_mask = (gt_img > 128)
        
        raw_files = sorted(glob.glob(os.path.join(eye_dir, "SectorMask_*.raw")))
        pattern_pngs = sorted(glob.glob(os.path.join(eye_dir, "pattern_*_mask_*.png")))
        
        occupied_mask = None
        is_evaluated = None
        
        if raw_files:
            latest_raw = raw_files[-1]
            raw_data = np.fromfile(latest_raw, dtype=np.uint32).reshape((H, W))
            occupied_mask = (raw_data & 0xFF).astype(np.uint8)
            is_evaluated = ((raw_data >> 12) & 0x01) == 1
            
            # 向き自動補正 (GTとの重なり最大化)
            best_overlap = -1
            best_orientation = (False, False)
            for fy in [False, True]:
                for fx in [False, True]:
                    test_eval = is_evaluated
                    if fy: test_eval = test_eval[::-1, :]
                    if fx: test_eval = test_eval[:, ::-1]
                    overlap = np.count_nonzero(test_eval & gt_mask)
                    if overlap > best_overlap:
                        best_overlap = overlap
                        best_orientation = (fy, fx)
            fy, fx = best_orientation
            if fy:
                occupied_mask = occupied_mask[::-1, :]
                is_evaluated = is_evaluated[::-1, :]
            if fx:
                occupied_mask = occupied_mask[:, ::-1]
                is_evaluated = is_evaluated[:, ::-1]
        elif len(pattern_pngs) == 256:
            class_imgs = np.zeros((256, H, W), dtype=np.uint8)
            for i, p_path in enumerate(pattern_pngs):
                class_imgs[i] = np.array(Image.open(p_path))[:, :, 0]
            max_v = class_imgs.max(axis=0)
            occupied_mask = class_imgs.argmax(axis=0).astype(np.uint8)
            is_evaluated = (max_v > 0)
        else:
            continue
            
        vo_mask = (np.array(Image.open(vo_path))[:, :, 0] > 128) if os.path.exists(vo_path) else np.ones((H, W), dtype=bool)
        eval_mask = vo_mask & is_evaluated
        
        eval_occ = occupied_mask[eval_mask]
        eval_gt = gt_mask[eval_mask]
        
        # 超高速 bincount カウント
        V_256 = np.bincount(eval_occ[eval_gt], minlength=256)
        O_256 = np.bincount(eval_occ[~eval_gt], minlength=256)
        
        out_dir = os.path.join(eye_dir, "Oracle256Analysis")
        os.makedirs(out_dir, exist_ok=True)
        csv_path = os.path.join(out_dir, "pattern_counts_256.csv")
        
        pattern_rows = []
        for m in range(256):
            v = int(V_256[m])
            o = int(O_256[m])
            tot = v + o
            frac = f"{v / tot:.6f}" if tot > 0 else "N/A"
            min_err = min(v, o)
            status = "unobserved" if tot == 0 else ("visible_only" if o == 0 else ("occluded_only" if v == 0 else "mixed"))
            cid = int(lut['rot_cid'][m])
            rep = int(lut['rot_rep'][m])
            n = int(lut['n_occ'][m])
            l = int(lut['l_max'][m])
            bits = bin(m)[2:].zfill(8)
            
            pattern_rows.append({
                'mask': m,
                'bits_b7_to_b0': bits,
                'occupied_count': n,
                'max_consecutive_zeros': l,
                'rotation_representative': rep,
                'rotation_class_id': cid,
                'total_pixels': tot,
                'gt_visible_count': v,
                'gt_occluded_count': o,
                'gt_visible_fraction': frac,
                'minimum_error_pixels': min_err,
                'status': status
            })
            
        with open(csv_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=list(pattern_rows[0].keys()))
            writer.writeheader()
            writer.writerows(pattern_rows)
            
        generated_count += 1
        
    elapsed = time.time() - t0
    if generated_count > 0:
        print(f"[+] {generated_count} 件の pattern_counts_256.csv を自動集計・保存しました ({elapsed:.2f}秒)。\n")


# ==============================================================================
# 3. データ読込 & 検証 (load_dataset)
# ==============================================================================
def load_dataset(root_dir, lut):
    """
    root_dir 配下から pattern_counts_256.csv を再帰検索し、検証して読み込む。
    未集計の撮影データがある場合は事前に自動集計を実行する。
    """
    # 未集計データの自動集計
    auto_generate_missing_pattern_counts(root_dir, lut)
    
    csv_paths = sorted(glob.glob(os.path.join(root_dir, "**", "pattern_counts_256.csv"), recursive=True))
    if not csv_paths:
        print(f"[!] pattern_counts_256.csv が見つかりませんでした: {root_dir}")
        return []
        
    dataset = []
    print(f"[*] {len(csv_paths)} 件の pattern_counts_256.csv を検出しました。検証中...")
    
    for path in csv_paths:
        norm_path = os.path.normpath(path)
        parts = norm_path.split(os.sep)
        
        # パス構造からメタデータを柔軟に抽出
        # 例1: .../RawTest/1/Pattern256MaskSweep/density_1.0pts_mm2/Left/Oracle256Analysis/... -> RawTest_case1
        # 例2: .../RawTest_Cases/RawTest_case1/Pattern256MaskSweep/... -> RawTest_case1
        # 例3: .../Bouchiba_NoiseOff_ProposeTest/SectorMaskSweep/... -> Bouchiba_NoiseOff_ProposeTest
        case_name = "UnknownCase"
        density_str = "UnknownDensity"
        eye = "UnknownEye"
        
        for idx, p in enumerate(parts):
            if p in ["Pattern256MaskSweep", "SectorMaskSweep"] and idx > 0:
                parent = parts[idx - 1]
                if idx > 1 and "RawTest" in parts[idx - 2]:
                    case_name = f"{parts[idx - 2]}_case{parent}" if parent.isdigit() else f"{parts[idx - 2]}_{parent}"
                elif parent.isdigit():
                    case_name = f"Case_{parent}"
                else:
                    case_name = parent
            elif p.startswith("density_") and "pts_mm2" in p:
                density_str = p.replace("density_", "").replace("pts_mm2", "")
            elif p in ["Left", "Right"]:
                eye = p
                
        data_id = f"{case_name}_{density_str}_{eye}"
        
        rows = []
        with open(path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            rows = list(reader)
            
        if len(rows) != 256:
            print(f"[!] 警告: {path} の行数が 256 行ではありません ({len(rows)} 行)。スキップします。")
            continue
            
        V = np.zeros(256, dtype=np.int64)
        O = np.zeros(256, dtype=np.int64)
        
        valid = True
        for r in rows:
            m = int(r['mask'])
            v = int(r['gt_visible_count'])
            o = int(r['gt_occluded_count'])
            if v < 0 or o < 0:
                print(f"[!] 警告: 負のカウントを検出 ({path}, mask={m})")
                valid = False; break
            if 'total_pixels' in r and int(r['total_pixels']) != v + o:
                print(f"[!] 警告: total_pixels != v + o ({path}, mask={m})")
                valid = False; break
            V[m] = v
            O[m] = o
            
        if not valid:
            continue
            
        G = int(np.sum(V))
        if G == 0:
            print(f"[!] 警告: GT可視画素数が0です ({path})。スキップします。")
            continue
            
        dataset.append({
            'data_id': data_id,
            'case': case_name,
            'density': float(density_str) if density_str != "UnknownDensity" else 0.0,
            'density_str': density_str,
            'eye': eye,
            'file_path': path,
            'V': V,
            'O': O,
            'G': G,
            'total_eval_pixels': int(np.sum(V) + np.sum(O))
        })
        
    print(f"[+] {len(dataset)} 件の有効なデータセットを読み込み・検証しました。\n")
    return dataset


# ==============================================================================
# 4. 共通評価関数 (evaluate_lut)
# ==============================================================================
def evaluate_lut(z, dataset):
    """
    256項目の可視判定 z in {0, 1}^256 を全データセットに適用し、
    各データの (TP, FP, FN, IoU) および平均 IoU, 合算 IoU を計算
    """
    results = []
    total_tp = 0
    total_fp = 0
    total_fn = 0
    
    for d in dataset:
        tp = int(np.dot(d['V'], z))
        fp = int(np.dot(d['O'], z))
        fn = d['G'] - tp
        denom = d['G'] + fp
        iou = tp / denom if denom > 0 else 0.0
        
        results.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'tp': tp,
            'fp': fp,
            'fn': fn,
            'iou': iou
        })
        total_tp += tp
        total_fp += fp
        total_fn += fn
        
    mean_iou = float(np.mean([r['iou'] for r in results]))
    pooled_denom = total_tp + total_fn + total_fp
    pooled_iou = float(total_tp / pooled_denom) if pooled_denom > 0 else 0.0
    
    return {
        'individual': results,
        'mean_iou': mean_iou,
        'pooled_iou': pooled_iou,
        'total_tp': total_tp,
        'total_fp': total_fp,
        'total_fn': total_fn
    }


# ==============================================================================
# 5. 固定8候補 & 固定20候補の最適化
# ==============================================================================
def optimize_fixed_8(dataset, lut):
    """
    固定8候補 (R in 1..8, N_occ < R が可視) の個別最適と共通最適を計算
    """
    cands_z = []
    for R in range(1, 9):
        z = (lut['n_occ'] < R).astype(np.int32)
        cands_z.append((R, z))
        
    # 個別最適
    indiv_opt = []
    for d in dataset:
        best_r = 1
        best_iou = -1.0
        best_res = None
        best_z = None
        for R, z in cands_z:
            tp = int(np.dot(d['V'], z))
            fp = int(np.dot(d['O'], z))
            fn = d['G'] - tp
            denom = d['G'] + fp
            iou = tp / denom if denom > 0 else 0.0
            if iou > best_iou:
                best_iou = iou
                best_r = R
                best_res = {'tp': tp, 'fp': fp, 'fn': fn, 'iou': iou}
                best_z = z
                
        indiv_opt.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'best_rule': f"Occ < {best_r}",
            'rule_summary': f"Threshold R={best_r} (Occ 0..{best_r - 1})",
            'selected_details': f"Occ 0..{best_r - 1} ({int(np.sum(best_z))} / 256 patterns)",
            'hex_mask': z_to_hex(best_z),
            'r': best_r,
            **best_res
        })
        
    # 共通最適 (平均IoU最大化)
    best_common_r = 1
    best_common_mean_iou = -1.0
    best_common_eval = None
    best_common_z = None
    
    for R, z in cands_z:
        ev = evaluate_lut(z, dataset)
        if ev['mean_iou'] > best_common_mean_iou:
            best_common_mean_iou = ev['mean_iou']
            best_common_r = R
            best_common_eval = ev
            best_common_z = z
            
    return {
        'individual': indiv_opt,
        'common_r': best_common_r,
        'common_rule_desc': f"Occ < {best_common_r}",
        'common_summary': f"Threshold R={best_common_r} (Occ 0..{best_common_r - 1})",
        'common_details': f"Occ 0..{best_common_r - 1} ({int(np.sum(best_common_z))} / 256 patterns)",
        'common_z': best_common_z,
        'common_eval': best_common_eval
    }

def optimize_fixed_20(dataset, lut):
    """
    固定20候補 (遮蔽条件: N_occ >= R_th and L_max <= L_th の否定が可視)
    """
    cands_z = []
    for R_th, L_th in RULES_20_CANDIDATES:
        is_occ = (lut['n_occ'] >= R_th) & (lut['l_max'] <= L_th)
        z = (~is_occ).astype(np.int32)
        cands_z.append((R_th, L_th, z))
        
    # 個別最適
    indiv_opt = []
    for d in dataset:
        best_r_th = 1
        best_l_th = 8
        best_iou = -1.0
        best_res = None
        best_z = None
        for R_th, L_th, z in cands_z:
            tp = int(np.dot(d['V'], z))
            fp = int(np.dot(d['O'], z))
            fn = d['G'] - tp
            denom = d['G'] + fp
            iou = tp / denom if denom > 0 else 0.0
            if iou > best_iou:
                best_iou = iou
                best_r_th = R_th
                best_l_th = L_th
                best_res = {'tp': tp, 'fp': fp, 'fn': fn, 'iou': iou}
                best_z = z
                
        rule_name = f"Occ < {best_r_th} or Unocc > {best_l_th}" if best_l_th < 8 else f"Occ < {best_r_th}"
        indiv_opt.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'best_rule': rule_name,
            'rule_summary': f"R_th={best_r_th}, L_th={best_l_th}",
            'selected_details': f"{int(np.sum(best_z))} / 256 patterns (occ: {occ_breakdown_str(best_z, lut['n_occ'])})",
            'hex_mask': z_to_hex(best_z),
            **best_res
        })
        
    # 共通最適 (平均IoU最大化)
    best_common_mean_iou = -1.0
    best_common_eval = None
    best_common_z = None
    best_common_params = (1, 8)
    
    for R_th, L_th, z in cands_z:
        ev = evaluate_lut(z, dataset)
        if ev['mean_iou'] > best_common_mean_iou:
            best_common_mean_iou = ev['mean_iou']
            best_common_eval = ev
            best_common_z = z
            best_common_params = (R_th, L_th)
            
    cr_th, cl_th = best_common_params
    c_rule_name = f"Occ < {cr_th} or Unocc > {cl_th}" if cl_th < 8 else f"Occ < {cr_th}"
    return {
        'individual': indiv_opt,
        'common_rule_desc': c_rule_name,
        'common_summary': f"R_th={cr_th}, L_th={cl_th}",
        'common_details': f"{int(np.sum(best_common_z))} / 256 patterns (occ: {occ_breakdown_str(best_common_z, lut['n_occ'])})",
        'common_params': best_common_params,
        'common_z': best_common_z,
        'common_eval': best_common_eval
    }


# ==============================================================================
# 6. 個別最適LUT & 合算最適LUT (並べ替え探索)
# ==============================================================================
def optimize_single_iou_lut(V_classes, O_classes, G):
    """
    単一データまたは合算カウントに対する並べ替え探索 (O(C log C))
    V_classes, O_classes: shape (C,)
    戻り値: (best_iou, best_z_classes)
    """
    C = len(V_classes)
    totals = V_classes + O_classes
    
    p = np.zeros(C, dtype=np.float64)
    nz = totals > 0
    p[nz] = V_classes[nz] / totals[nz]
    
    sorted_idx = np.argsort(-p)
    
    best_iou = 0.0
    best_k = 0
    cur_tp = 0
    cur_fp = 0
    
    for k in range(1, C + 1):
        idx = sorted_idx[k - 1]
        if totals[idx] == 0:
            break
        cur_tp += V_classes[idx]
        cur_fp += O_classes[idx]
        denom = G + cur_fp
        iou = cur_tp / denom if denom > 0 else 0.0
        if iou > best_iou:
            best_iou = iou
            best_k = k
            
    best_z = np.zeros(C, dtype=np.int32)
    for k in range(best_k):
        best_z[sorted_idx[k]] = 1
        
    return best_iou, best_z

def get_individual_optima_36_and_256(dataset, lut):
    """
    各データに対して 36クラスおよび 256パターンの個別最適を計算
    """
    opt_36 = []
    opt_256 = []
    
    for d in dataset:
        # 36クラス
        V_36 = np.zeros(36, dtype=np.int64)
        O_36 = np.zeros(36, dtype=np.int64)
        for m in range(256):
            cid = lut['rot_cid'][m]
            V_36[cid] += d['V'][m]
            O_36[cid] += d['O'][m]
        iou_36, z_36 = optimize_single_iou_lut(V_36, O_36, d['G'])
        
        z_256_from_36 = np.array([z_36[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        tp_36 = int(np.dot(d['V'], z_256_from_36))
        fp_36 = int(np.dot(d['O'], z_256_from_36))
        fn_36 = d['G'] - tp_36
        
        vis_cids = [cid for cid in range(36) if z_36[cid] == 1]
        opt_36.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'best_rule': f"{len(vis_cids)} / 36 classes",
            'rule_summary': f"occ_classes: {class_breakdown_str(z_36, lut)}",
            'selected_details': f"{compress_indices(vis_cids, prefix='c')} ({int(np.sum(z_256_from_36))} / 256 patterns)",
            'hex_mask': z_to_hex(z_256_from_36),
            'tp': tp_36, 'fp': fp_36, 'fn': fn_36,
            'iou': iou_36,
            'z_36': z_36,
            'z_256': z_256_from_36
        })
        
        # 256パターン
        iou_256, z_256 = optimize_single_iou_lut(d['V'], d['O'], d['G'])
        tp_256 = int(np.dot(d['V'], z_256))
        fp_256 = int(np.dot(d['O'], z_256))
        fn_256 = d['G'] - tp_256
        
        vis_masks = [m for m in range(256) if z_256[m] == 1]
        opt_256.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'best_rule': f"{len(vis_masks)} / 256 patterns",
            'rule_summary': f"occ_patterns: {occ_breakdown_str(z_256, lut['n_occ'])}",
            'selected_details': compress_indices(vis_masks, prefix="m"),
            'hex_mask': z_to_hex(z_256),
            'tp': tp_256, 'fp': fp_256, 'fn': fn_256,
            'iou': iou_256,
            'z_256': z_256
        })
        
    return opt_36, opt_256


# ==============================================================================
# 7. 共通最適化 MILP ソルバー (optimize_mean_iou_milp)
# ==============================================================================
def optimize_mean_iou_milp(counts_V, counts_O, G_list, fallback_z_classes, time_limit_sec=60):
    """
    全データに対する平均 IoU を直接最大化する McCormick 緩和 MILP ソルバー
    """
    start_time = time.time()
    D, C = counts_V.shape
    
    totals_per_class = np.sum(counts_V + counts_O, axis=0)
    unobserved = (totals_per_class == 0)
    active_classes = np.where(~unobserved)[0]
    C_act = len(active_classes)
    
    if C_act == 0:
        return fallback_z_classes, 0.0, "All_Unobserved", 0.0, 0.0
        
    act_counts_V = counts_V[:, active_classes]
    act_counts_O = counts_O[:, active_classes]
    
    y_min = np.zeros(D, dtype=np.float64)
    y_max = np.zeros(D, dtype=np.float64)
    for i in range(D):
        y_min[i] = 1.0 / (G_list[i] + np.sum(act_counts_O[i, :]))
        y_max[i] = 1.0 / G_list[i]
        
    num_vars = C_act + D + D * C_act
    
    c = np.zeros(num_vars, dtype=np.float64)
    for i in range(D):
        for j in range(C_act):
            idx_w = C_act + D + i * C_act + j
            c[idx_w] = - (1.0 / D) * act_counts_V[i, j]
            
    integrality = np.zeros(num_vars, dtype=np.int32)
    integrality[:C_act] = 1
    
    lb = np.zeros(num_vars, dtype=np.float64)
    ub = np.ones(num_vars, dtype=np.float64)
    
    for i in range(D):
        lb[C_act + i] = y_min[i]
        ub[C_act + i] = y_max[i]
        for j in range(C_act):
            idx_w = C_act + D + i * C_act + j
            lb[idx_w] = 0.0
            ub[idx_w] = y_max[i]
            
    bounds = Bounds(lb, ub)
    
    rows = []
    rhs_l = []
    rhs_u = []
    
    # 制約 1: 分母関係式
    for i in range(D):
        row = np.zeros(num_vars, dtype=np.float64)
        row[C_act + i] = G_list[i]
        for j in range(C_act):
            idx_w = C_act + D + i * C_act + j
            row[idx_w] = act_counts_O[i, j]
        rows.append(row)
        rhs_l.append(1.0)
        rhs_u.append(1.0)
        
    # 制約 2: McCormick 4不等式
    for i in range(D):
        y_l = y_min[i]
        y_u = y_max[i]
        for j in range(C_act):
            idx_z = j
            idx_y = C_act + i
            idx_w = C_act + D + i * C_act + j
            
            # (1) w >= y_l * z
            row1 = np.zeros(num_vars, dtype=np.float64)
            row1[idx_w] = 1.0
            row1[idx_z] = -y_l
            rows.append(row1)
            rhs_l.append(0.0)
            rhs_u.append(np.inf)
            
            # (2) w >= y + y_u * z - y_u
            row2 = np.zeros(num_vars, dtype=np.float64)
            row2[idx_w] = 1.0
            row2[idx_y] = -1.0
            row2[idx_z] = -y_u
            rows.append(row2)
            rhs_l.append(-y_u)
            rhs_u.append(np.inf)
            
            # (3) w <= y_u * z
            row3 = np.zeros(num_vars, dtype=np.float64)
            row3[idx_w] = 1.0
            row3[idx_z] = -y_u
            rows.append(row3)
            rhs_l.append(-np.inf)
            rhs_u.append(0.0)
            
            # (4) w <= y + y_l * z - y_l
            row4 = np.zeros(num_vars, dtype=np.float64)
            row4[idx_w] = 1.0
            row4[idx_y] = -1.0
            row4[idx_z] = -y_l
            rows.append(row4)
            rhs_l.append(-np.inf)
            rhs_u.append(-y_l)
            
    A = np.array(rows, dtype=np.float64)
    constraints = LinearConstraint(A, rhs_l, rhs_u)
    
    # 初期解
    z0 = np.zeros(num_vars, dtype=np.float64)
    act_init_z = fallback_z_classes[active_classes]
    z0[:C_act] = act_init_z
    for i in range(D):
        denom_val = G_list[i] + np.dot(act_counts_O[i, :], act_init_z)
        y_val = 1.0 / denom_val if denom_val > 0 else y_min[i]
        z0[C_act + i] = y_val
        for j in range(C_act):
            idx_w = C_act + D + i * C_act + j
            z0[idx_w] = act_init_z[j] * y_val
            
    res = milp(
        c=c,
        integrality=integrality,
        bounds=bounds,
        constraints=constraints,
        options={'time_limit': time_limit_sec, 'mip_rel_gap': 1e-5}
    )
    
    calc_time = time.time() - start_time
    
    if res.success or res.status in [0, 1]:
        z_sol = np.round(res.x[:C_act]).astype(np.int32)
        z_full = np.zeros(C, dtype=np.int32)
        z_full[active_classes] = z_sol
        z_full[unobserved] = fallback_z_classes[unobserved]
        obj_val = -res.fun
        status_msg = "Optimal" if res.status == 0 else f"Feasible({res.message})"
        gap = getattr(res, 'mip_gap', 0.0)
        return z_full, obj_val, status_msg, calc_time, gap
    else:
        print(f"[!] MILP failed (status {res.status}: {res.message}). Falling back.")
        return fallback_z_classes, 0.0, f"Failed_{res.status}", calc_time, 1.0


# ==============================================================================
# 8. 実行エンジン & 出力 (optimize_pattern_rules)
# ==============================================================================
def run_pattern_rules_optimization(dataset_root=None, output_dir=None):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_dataset_dir = os.path.abspath(os.path.join(script_dir, "../../../../../Estimation/SICESI_Dataset"))
    
    if dataset_root is None:
        raw_test_dir = os.path.join(default_dataset_dir, "RawTest")
        raw_test_cases_dir = os.path.join(default_dataset_dir, "RawTest_Cases")
        if os.path.exists(raw_test_dir):
            dataset_root = raw_test_dir
        elif os.path.exists(raw_test_cases_dir):
            dataset_root = raw_test_cases_dir
        else:
            dataset_root = default_dataset_dir
            
    dataset_root = os.path.abspath(dataset_root)
    
    if output_dir is None:
        output_dir = os.path.join(dataset_root, "RuleOptimizationResults")
    os.makedirs(output_dir, exist_ok=True)
    
    lut = build_256_lookup()
    
    print("=" * 80)
    print("【占有パターンCSVから個別最適・共通最適を求める自動解析】")
    print(f"  探索対象ルート: {dataset_root}")
    print(f"  出力先フォルダ: {output_dir}")
    print("=" * 80)
    
    # 1. 全CSVの読み込み (未生成データは自動集計)
    dataset = load_dataset(dataset_root, lut)
    if not dataset:
        print("[!] 解析対象データが存在しませんでした。")
        return
        
    D = len(dataset)
    
    # 2. 固定8候補 & 固定20候補の個別・共通最適
    print("[*] [1/4] 固定8候補の最適化を実行中 (総当たり)...")
    res_8 = optimize_fixed_8(dataset, lut)
    print(f"    -> 共通最良: {res_8['common_rule_desc']} (平均IoU: {res_8['common_eval']['mean_iou']*100:.4f}%)")
    
    print("[*] [2/4] 固定20候補の最適化を実行中 (総当たり)...")
    res_20 = optimize_fixed_20(dataset, lut)
    print(f"    -> 共通最良: {res_20['common_rule_desc']} (平均IoU: {res_20['common_eval']['mean_iou']*100:.4f}%)")
    
    # 3. 36クラス & 256パターンの個別最適 (並べ替え探索)
    print("[*] [3/4] 36クラス & 256パターンの個別最適を計算中 (並べ替え探索)...")
    indiv_36, indiv_256 = get_individual_optima_36_and_256(dataset, lut)
    
    # 4. 共通最適LUTの MILP 計算 (36クラス & 256パターン)
    print("[*] [4/4] 共通最適LUTの MILP 計算中 (平均IoU直接最大化)...")
    
    fallback_z_256 = res_8['common_z']
    fallback_z_36 = np.zeros(36, dtype=np.int32)
    for cid in range(36):
        rep = lut['unique_reps'][cid]
        fallback_z_36[cid] = fallback_z_256[rep]
        
    counts_V_36 = np.zeros((D, 36), dtype=np.int64)
    counts_O_36 = np.zeros((D, 36), dtype=np.int64)
    G_list = np.array([d['G'] for d in dataset], dtype=np.int64)
    for i, d in enumerate(dataset):
        for m in range(256):
            cid = lut['rot_cid'][m]
            counts_V_36[i, cid] += d['V'][m]
            counts_O_36[i, cid] += d['O'][m]
            
    print("    - 36クラス共通最適 MILP を求解中...")
    z_common_36, milp_obj_36, status_36, time_36, gap_36 = optimize_mean_iou_milp(
        counts_V_36, counts_O_36, G_list, fallback_z_36, time_limit_sec=60
    )
    z_common_36_as_256 = np.array([z_common_36[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
    eval_common_36 = evaluate_lut(z_common_36_as_256, dataset)
    vis_c_common = [c for c in range(36) if z_common_36[c] == 1]
    print(f"      -> 完了 ({time_36:.2f}秒, {status_36}): 平均IoU = {eval_common_36['mean_iou']*100:.4f}% (可視クラス: {len(vis_c_common)} / 36)")
    
    print("    - 256パターン共通最適 MILP を求解中...")
    counts_V_256 = np.array([d['V'] for d in dataset], dtype=np.int64)
    counts_O_256 = np.array([d['O'] for d in dataset], dtype=np.int64)
    z_common_256, milp_obj_256, status_256, time_256, gap_256 = optimize_mean_iou_milp(
        counts_V_256, counts_O_256, G_list, fallback_z_256, time_limit_sec=60
    )
    eval_common_256 = evaluate_lut(z_common_256, dataset)
    vis_m_common = [m for m in range(256) if z_common_256[m] == 1]
    print(f"      -> 完了 ({time_256:.2f}秒, {status_256}): 平均IoU = {eval_common_256['mean_iou']*100:.4f}% (可視パターン: {len(vis_m_common)} / 256)")
    
    # ==========================================================================
    # 9. CSV ファイル出力
    # ==========================================================================
    
    # 9.1 共通規則サマリー (common_rules_summary.csv)
    m8 = res_8['common_eval']['mean_iou']
    m20 = res_20['common_eval']['mean_iou']
    m36 = eval_common_36['mean_iou']
    m256 = eval_common_256['mean_iou']
    
    summary_rows = [
        {
            'method': 'Fixed_8_Candidates',
            'mean_iou': f"{m8:.6f}",
            'mean_iou_pct': f"{m8*100:.4f}%",
            'visible_patterns': f"{int(np.sum(res_8['common_z']))} / 256",
            'best_rule': res_8['common_rule_desc'],
            'rule_summary': res_8['common_summary'],
            'selected_details': res_8['common_details'],
            'hex_mask': z_to_hex(res_8['common_z'])
        },
        {
            'method': 'Fixed_20_Candidates',
            'mean_iou': f"{m20:.6f}",
            'mean_iou_pct': f"{m20*100:.4f}%",
            'visible_patterns': f"{int(np.sum(res_20['common_z']))} / 256",
            'best_rule': res_20['common_rule_desc'],
            'rule_summary': res_20['common_summary'],
            'selected_details': res_20['common_details'],
            'hex_mask': z_to_hex(res_20['common_z'])
        },
        {
            'method': 'Oracle_36_RotationLUT',
            'mean_iou': f"{m36:.6f}",
            'mean_iou_pct': f"{m36*100:.4f}%",
            'visible_patterns': f"{int(np.sum(z_common_36_as_256))} / 256",
            'best_rule': f"{len(vis_c_common)} / 36 classes",
            'rule_summary': f"occ_classes: {class_breakdown_str(z_common_36, lut)}",
            'selected_details': f"{compress_indices(vis_c_common, prefix='c')} ({int(np.sum(z_common_36_as_256))} / 256 patterns)",
            'hex_mask': z_to_hex(z_common_36_as_256)
        },
        {
            'method': 'Oracle_256_ExactLUT',
            'mean_iou': f"{m256:.6f}",
            'mean_iou_pct': f"{m256*100:.4f}%",
            'visible_patterns': f"{len(vis_m_common)} / 256",
            'best_rule': f"{len(vis_m_common)} / 256 patterns",
            'rule_summary': f"occ_patterns: {occ_breakdown_str(z_common_256, lut['n_occ'])}",
            'selected_details': compress_indices(vis_m_common, prefix="m"),
            'hex_mask': z_to_hex(z_common_256)
        }
    ]
    csv_summary = os.path.join(output_dir, "common_rules_summary.csv")
    with open(csv_summary, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=list(summary_rows[0].keys()))
        writer.writeheader()
        writer.writerows(summary_rows)
    print(f"\n[+] 共通規則サマリー (common_rules_summary.csv) を保存しました: {csv_summary}")
    
    # 9.2 個別最適一覧 (individual_optima.csv)
    indiv_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        case = d['case']
        dens = d['density_str']
        eye = d['eye']
        
        # 8候補
        r8 = res_8['individual'][i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Fixed_8_Candidates',
            'best_rule': r8['best_rule'],
            'rule_summary': r8['rule_summary'],
            'selected_details': r8['selected_details'],
            'hex_mask': r8['hex_mask'],
            'tp': r8['tp'], 'fp': r8['fp'], 'fn': r8['fn'],
            'iou': f"{r8['iou']:.6f}", 'iou_pct': f"{r8['iou']*100:.4f}%"
        })
        # 20候補
        r20 = res_20['individual'][i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Fixed_20_Candidates',
            'best_rule': r20['best_rule'],
            'rule_summary': r20['rule_summary'],
            'selected_details': r20['selected_details'],
            'hex_mask': r20['hex_mask'],
            'tp': r20['tp'], 'fp': r20['fp'], 'fn': r20['fn'],
            'iou': f"{r20['iou']:.6f}", 'iou_pct': f"{r20['iou']*100:.4f}%"
        })
        # 36クラス
        r36 = indiv_36[i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Oracle_36_RotationLUT',
            'best_rule': r36['best_rule'],
            'rule_summary': r36['rule_summary'],
            'selected_details': r36['selected_details'],
            'hex_mask': r36['hex_mask'],
            'tp': r36['tp'], 'fp': r36['fp'], 'fn': r36['fn'],
            'iou': f"{r36['iou']:.6f}", 'iou_pct': f"{r36['iou']*100:.4f}%"
        })
        # 256パターン
        r256 = indiv_256[i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Oracle_256_ExactLUT',
            'best_rule': r256['best_rule'],
            'rule_summary': r256['rule_summary'],
            'selected_details': r256['selected_details'],
            'hex_mask': r256['hex_mask'],
            'tp': r256['tp'], 'fp': r256['fp'], 'fn': r256['fn'],
            'iou': f"{r256['iou']:.6f}", 'iou_pct': f"{r256['iou']*100:.4f}%"
        })
        
    csv_indiv = os.path.join(output_dir, "individual_optima.csv")
    with open(csv_indiv, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=list(indiv_rows[0].keys()))
        writer.writeheader()
        writer.writerows(indiv_rows)
    print(f"[+] 個別最適一覧 (individual_optima.csv) を保存しました: {csv_indiv}")
    
    # 9.3 共通規則ファイル (common_rule_*.csv)
    # A. common_rule_8.csv
    csv_rule_8 = os.path.join(output_dir, "common_rule_8.csv")
    with open(csv_rule_8, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(["mask", "bits_b7_to_b0", "occupied_count", "decision", "label"])
        for m in range(256):
            dec = res_8['common_z'][m]
            writer.writerow([m, bin(m)[2:].zfill(8), lut['n_occ'][m], dec, "VISIBLE" if dec == 1 else "OCCLUDED"])
            
    # B. common_rule_20.csv
    csv_rule_20 = os.path.join(output_dir, "common_rule_20.csv")
    with open(csv_rule_20, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(["mask", "bits_b7_to_b0", "occupied_count", "max_consecutive_zeros", "decision", "label"])
        for m in range(256):
            dec = res_20['common_z'][m]
            writer.writerow([m, bin(m)[2:].zfill(8), lut['n_occ'][m], lut['l_max'][m], dec, "VISIBLE" if dec == 1 else "OCCLUDED"])
            
    # C. common_lut_36.csv
    csv_lut_36 = os.path.join(output_dir, "common_lut_36.csv")
    tot_obs_36 = np.sum(counts_V_36 + counts_O_36, axis=0)
    with open(csv_lut_36, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(["rotation_class_id", "rotation_representative", "representative_bits", "occupied_count", "decision", "label", "total_observed_pixels", "is_fallback"])
        for cid in range(36):
            rep = lut['unique_reps'][cid]
            tot_obs = int(tot_obs_36[cid])
            dec = z_common_36[cid]
            fb = "YES" if tot_obs == 0 else "NO"
            writer.writerow([cid, rep, bin(rep)[2:].zfill(8), lut['n_occ'][rep], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs, fb])
            
    # D. common_lut_256.csv
    csv_lut_256 = os.path.join(output_dir, "common_lut_256.csv")
    tot_obs_256 = np.sum(counts_V_256 + counts_O_256, axis=0)
    with open(csv_lut_256, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(["mask", "bits_b7_to_b0", "occupied_count", "rotation_class_id", "decision", "label", "total_observed_pixels", "is_fallback"])
        for m in range(256):
            tot_obs = int(tot_obs_256[m])
            dec = z_common_256[m]
            fb = "YES" if tot_obs == 0 else "NO"
            writer.writerow([m, bin(m)[2:].zfill(8), lut['n_occ'][m], lut['rot_cid'][m], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs, fb])
            
    print(f"[+] 共通規則・共通LUTファイル (4件) を保存しました。")
    
    # 9.4 共通規則を各データへ適用した結果 (common_rule_evaluation.csv)
    eval_8 = res_8['common_eval']
    eval_20 = res_20['common_eval']
    
    comp_eval_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        j8 = eval_8['individual'][i]['iou']
        j20 = eval_20['individual'][i]['iou']
        j36 = eval_common_36['individual'][i]['iou']
        j256 = eval_common_256['individual'][i]['iou']
        
        comp_eval_rows.append({
            'data_id': did,
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'common_8_iou': f"{j8:.6f}",
            'common_20_iou': f"{j20:.6f}",
            'common_36_iou': f"{j36:.6f}",
            'common_256_iou': f"{j256:.6f}",
            'diff_20_vs_8': f"{(j20 - j8)*100:+.4f}%pt",
            'diff_36_vs_8': f"{(j36 - j8)*100:+.4f}%pt",
            'diff_256_vs_8': f"{(j256 - j8)*100:+.4f}%pt",
            'diff_256_vs_36': f"{(j256 - j36)*100:+.4f}%pt"
        })
        
    # 平均行の追加
    comp_eval_rows.append({
        'data_id': '=== MEAN_IOU ===',
        'case': 'ALL',
        'density': 'ALL',
        'eye': 'ALL',
        'common_8_iou': f"{m8:.6f}",
        'common_20_iou': f"{m20:.6f}",
        'common_36_iou': f"{m36:.6f}",
        'common_256_iou': f"{m256:.6f}",
        'diff_20_vs_8': f"{(m20 - m8)*100:+.4f}%pt",
        'diff_36_vs_8': f"{(m36 - m8)*100:+.4f}%pt",
        'diff_256_vs_8': f"{(m256 - m8)*100:+.4f}%pt",
        'diff_256_vs_36': f"{(m256 - m36)*100:+.4f}%pt"
    })
    
    csv_comp_eval = os.path.join(output_dir, "common_rule_evaluation.csv")
    with open(csv_comp_eval, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=list(comp_eval_rows[0].keys()))
        writer.writeheader()
        writer.writerows(comp_eval_rows)
    print(f"[+] 共通規則評価一覧 (common_rule_evaluation.csv) を保存しました: {csv_comp_eval}")
    
    # 9.5 個別最適からの低下 (individual_gap_analysis.csv)
    gap_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        opt8 = res_8['individual'][i]['iou']
        opt20 = res_20['individual'][i]['iou']
        opt36 = indiv_36[i]['iou']
        opt256 = indiv_256[i]['iou']
        
        com8 = eval_8['individual'][i]['iou']
        com20 = eval_20['individual'][i]['iou']
        com36 = eval_common_36['individual'][i]['iou']
        com256 = eval_common_256['individual'][i]['iou']
        
        gap_rows.append({
            'data_id': did,
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'opt_8_iou': f"{opt8:.6f}",
            'common_8_iou': f"{com8:.6f}",
            'gap_8': f"{(opt8 - com8)*100:.4f}%pt",
            'opt_20_iou': f"{opt20:.6f}",
            'common_20_iou': f"{com20:.6f}",
            'gap_20': f"{(opt20 - com20)*100:.4f}%pt",
            'opt_36_iou': f"{opt36:.6f}",
            'common_36_iou': f"{com36:.6f}",
            'gap_36': f"{(opt36 - com36)*100:.4f}%pt",
            'opt_256_iou': f"{opt256:.6f}",
            'common_256_iou': f"{com256:.6f}",
            'gap_256': f"{(opt256 - com256)*100:.4f}%pt"
        })
        
    csv_gap = os.path.join(output_dir, "individual_gap_analysis.csv")
    with open(csv_gap, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=list(gap_rows[0].keys()))
        writer.writeheader()
        writer.writerows(gap_rows)
    print(f"[+] 個別最適からの低下分析 (individual_gap_analysis.csv) を保存しました: {csv_gap}")
    
    # ==========================================================================
    # 10. 自動検証 & サマリー表示 (仕様書第13節)
    # ==========================================================================
    print("\n" + "=" * 80)
    print("【方式間の包含関係 & 数学的整合性チェック】")
    print(f"  共通最適 平均IoU の不等式: J*_8 <= J*_20 <= J*_36 <= J*_256")
    print(f"    {m8*100:.4f}% <= {m20*100:.4f}% <= {m36*100:.4f}% <= {m256*100:.4f}%")
    ineq_ok = (m8 <= m20 + 1e-6) and (m20 <= m36 + 1e-6) and (m36 <= m256 + 1e-6)
    print(f"    -> 整合性判定: {'完全成立 (PASS!!)' if ineq_ok else '不成立 (FAIL)'}")
    
    # 回転不変性テスト
    rot_invariant = True
    for cid in range(36):
        members = [m for m in range(256) if lut['rot_cid'][m] == cid]
        decisions = [z_common_36_as_256[m] for m in members]
        if len(set(decisions)) > 1:
            rot_invariant = False
            break
    print(f"  共通36クラスLUTの回転不変性: {'完全担保 (PASS!!)' if rot_invariant else '破綻 (FAIL)'}")
    print("=" * 80)

if __name__ == "__main__":
    search_root = None
    if len(sys.argv) > 1:
        search_root = sys.argv[1]
        
    out_dir = None
    if len(sys.argv) > 2:
        out_dir = sys.argv[2]
        
    run_pattern_rules_optimization(search_root, out_dir)
