"""
================================================================================
占有パターンCSVから個別最適・共通最適を求める自動解析スクリプト
(Feature: SICESI - Advanced Pattern Rule Optimization & Objective Evaluation)
================================================================================

【概要】
  本スクリプトは、256パターンの占有マスク評価データ (pattern_counts_256.csv) を
  再帰的に探索・収集し、以下の評価指標および目的関数に基づく自動解析を行います：

  【評価指標体系】
    1. 追加遮蔽なし基準 (No Extra Occlusion Baseline): J_no_occ = V / (V + O)
    2. 候補領域内の誤画素率: e = (FN + FP) / M  (M = V + O)
    3. 正味の誤画素削減率 (Net Error Reduction Rate): Δe = (O - FP - FN) / M
    4. 1-IoU 不一致率の相対低減率 (Relative Error Reduction Rate):
       r_i = (J_i(h) - J_i_base) / (1 - J_i_base)
       ※ 基準 J_i_base は「共通占有数判定 (固定8候補の共通最適)」

  【2つの最適化目的関数 (論文・学術比較用)】
    - 目的関数 A: 平均 IoU 最大化 (Unweighted Mean IoU Optimal)
    - 目的関数 B: 1-IoU 平均相対低減率最大化 (Relative Reduction Optimal)
      (重み w_i = 1 / (1 - J_i_base) による重み付き MILP 求解)

  【判定器の種類】
    1. 固定8候補      : 最低占有数 1..8 (N_occ < R が可視)
    2. 固定20候補     : 占有数・最大連続非占有数の20代表設定 (R_th, L_th)
    3. 固定36クラスLUT : 回転不変36クラスへの独立最適割当
    4. 固定256パターン : 256ビット完全独立最適割当

【出力ファイル】
  - common_rules_summary.csv   : 目的関数A・B × 4方式の決定共通規則サマリー表
  - objective_comparison.csv   : 目的関数A (平均IoU) と 目的関数B (相対低減率) の直接比較表
  - common_rule_evaluation.csv : 全データに対する各手法の評価 (IoU, 相対低減率, 正味誤画素削減率)
  - individual_optima.csv      : 全データ × 4方式 + 追加遮蔽なし の個別最適一覧
  - individual_gap_analysis.csv: 個別最適からの性能低下幅 (汎化ギャップ) 分析
  - common_rule_8.csv, common_rule_20.csv, common_lut_36_*.csv, common_lut_256_*.csv

【実行方法】
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py
"""

import os
import sys
import glob
import time
import csv
import re
import numpy as np
from PIL import Image
from scipy.optimize import milp, LinearConstraint, Bounds

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

# ==============================================================================
# 0. パターンのコンパクト表現・補助関数
# ==============================================================================
def safe_write_csv(csv_path, rows, fieldnames):
    """Excel等で開かれていてPermissionErrorになっても別名で確実に保存する"""
    if not rows:
        return
    try:
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(rows)
    except PermissionError:
        base, ext = os.path.splitext(csv_path)
        alt_path = f"{base}_new{ext}"
        print(f"[!] 警告: {csv_path} への書き込み権限がありません (Excel等で開かれている可能性があります)。{alt_path} に保存します。")
        with open(alt_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(rows)

def safe_write_csv_raw(csv_path, header, rows):
    """Excel等で開かれていてPermissionErrorになっても別名で確実に保存する (raw writer用)"""
    try:
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.writer(f)
            writer.writerow(header)
            writer.writerows(rows)
    except PermissionError:
        base, ext = os.path.splitext(csv_path)
        alt_path = f"{base}_new{ext}"
        print(f"[!] 警告: {csv_path} への書き込み権限がありません (Excel等で開かれている可能性があります)。{alt_path} に保存します。")
        with open(alt_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.writer(f)
            writer.writerow(header)
            writer.writerows(rows)
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

EXCLUDED_DIR_PATTERNS = ["Bouchiba_", "RuleOptimizationResults", "DiffMaps"]

def load_mono_image(path):
    img = np.array(Image.open(path))
    if img.ndim == 3:
        return img[:, :, 0]
    return img

def find_file_in_ancestors(start_dir, target_rel_paths, max_levels=10):
    """祖先ディレクトリを遡って最初に存在するファイルパスを返す"""
    cur = os.path.abspath(start_dir)
    for _ in range(max_levels):
        for rel in target_rel_paths:
            candidate = os.path.join(cur, rel)
            if os.path.exists(candidate):
                return candidate
        parent = os.path.dirname(cur)
        if parent == cur:
            break
        cur = parent
    return None

def score_visibility_rule(z, d, empty_iou=1.0):
    """
    統一可視IoUスコア計算関数
    z: 256次元の可視判定ベクトル (1:可視, 0:遮蔽)
    d: データセット要素 (V, O, direct_gt_visible, direct_gt_occluded)
    """
    tp = int(np.dot(d['V'], z))
    fp = int(np.dot(d['O'], z))
    fn = int(np.dot(d['V'], 1 - z)) + int(d.get('direct_gt_visible', 0))
    denom = tp + fp + fn
    if denom == 0:
        iou = float(empty_iou)
    else:
        iou = float(tp / denom)
    return tp, fp, fn, denom, iou

# ==============================================================================
# 2. 未集計撮影データの高速自動集計 (auto_generate_missing_pattern_counts)
# ==============================================================================
def auto_generate_missing_pattern_counts(root_dir, lut):
    """
    root_dir 配下の全撮影ディレクトリを走査し、
    pattern_counts_256.csv が未生成の場合に生バッファ・GTから領域 R, E, D を厳密分離して自動集計する。
    """
    sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Sector8MaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "Pattern256MaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "SectorMaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "StageDiagnosis"), recursive=True))
    if os.path.basename(root_dir) in ["Sector8MaskSweep", "Pattern256MaskSweep", "SectorMaskSweep", "StageDiagnosis"]:
        sweep_dirs.append(root_dir)
    sweep_dirs = sorted(list(set(sweep_dirs)))
    
    # 不要なアーカイブ・一時フォルダを除外
    sweep_dirs = [s for s in sweep_dirs if not any(ex in s for ex in EXCLUDED_DIR_PATTERNS)]
    
    if not sweep_dirs:
        return
        
    missing_targets = []
    for s_dir in sweep_dirs:
        if os.path.basename(s_dir) == "StageDiagnosis":
            p1 = os.path.join(s_dir, "Oracle256Analysis", "pattern_counts_256.csv")
            p2 = os.path.join(s_dir, "pattern_counts_256.csv")
            if not (os.path.exists(p1) or os.path.exists(p2)):
                missing_targets.append((s_dir, s_dir, "Left", s_dir))
            continue

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
        
    print(f"[*] 未集計の撮影データ ({len(missing_targets)} 件) を検出しました。厳密集計中...")
    generated_count = 0
    t0 = time.time()
    
    for s_dir, d_dir, eye, eye_dir in missing_targets:
        vo_rel_candidates = [
            "vo_silhouette_pre_correction.png",
            f"vo_silhouette_{eye.lower()}.png",
            os.path.join("GT", eye, f"vo_silhouette_{eye.lower()}.png"),
            os.path.join("GT", f"vo_silhouette_{eye.lower()}.png"),
        ]
        vo_path = find_file_in_ancestors(eye_dir, vo_rel_candidates)
        if not vo_path:
            continue
            
        vo_img = load_mono_image(vo_path)
        R = (vo_img > 128)
        H, W = R.shape
        total_vo = int(np.count_nonzero(R))
        if total_vo == 0:
            continue

        # Ground Truth (Mesh Depth GT のみを受け入れ、陰影付きカラーGTフォールバックは排除)
        gt_rel_candidates = [
            f"gt_depth_occluded_mask_{eye.lower()}.png",
            os.path.join("GT", eye, f"gt_depth_occluded_mask_{eye.lower()}.png"),
            os.path.join("GT", f"gt_depth_occluded_mask_{eye.lower()}.png"),
        ]
        gt_path = find_file_in_ancestors(eye_dir, gt_rel_candidates)
        if not gt_path:
            print(f"[!] 警告: {eye_dir} の Mesh Depth GT が見つからないため集計をスキップします。")
            continue

        gt_occ = (load_mono_image(gt_path) > 128) & R
        gt_vis = R & (~gt_occ)
        
        # マスク・生バッファ探索
        raw_files = sorted(glob.glob(os.path.join(eye_dir, "A_raw_uint32.bin")) + glob.glob(os.path.join(eye_dir, "SectorMask_*.raw")))
        origin_bin_path = os.path.join(eye_dir, "origin_type_raw_uint32.bin")
        eval_png_path = os.path.join(eye_dir, "evaluated_mask_pre_correction.png")
        origin_png_path = os.path.join(eye_dir, "origin_type_map_direct.png")

        occupied_mask = None
        is_evaluated = None
        origin_type_map = None

        if raw_files:
            latest_raw = raw_files[-1]
            raw_data = np.fromfile(latest_raw, dtype=np.uint32)
            if raw_data.size == H * W:
                raw_2d = np.flipud(raw_data.reshape((H, W)))
                occupied_mask = (raw_2d & 0xFF).astype(np.uint8)
                is_evaluated = ((raw_2d >> 12) & 0x01) == 1

        if os.path.exists(origin_bin_path):
            orig_raw = np.fromfile(origin_bin_path, dtype=np.uint32)
            if orig_raw.size == H * W:
                origin_type_map = np.flipud(orig_raw.reshape((H, W)))

        # PNG からの補完
        if is_evaluated is None and os.path.exists(eval_png_path):
            is_evaluated = (load_mono_image(eval_png_path) > 128)
        if origin_type_map is None and os.path.exists(origin_png_path):
            origin_type_map = load_mono_image(origin_png_path)

        if occupied_mask is None:
            # 個別8セクターマスク探索
            sector_pngs = sorted(glob.glob(os.path.join(eye_dir, f"sector_*_mask_{eye.lower()}.png")) + glob.glob(os.path.join(eye_dir, "sector_*_mask.png")))
            sector_map = {}
            for p in sector_pngs:
                fname = os.path.basename(p)
                m_match = re.search(r"sector_(\d+)_mask", fname)
                if m_match:
                    sec_id = int(m_match.group(1))
                    if 0 <= sec_id < 8:
                        sector_map[sec_id] = p
            if len(sector_map) == 8:
                occupied_mask = np.zeros((H, W), dtype=np.uint8)
                for sec_id in range(8):
                    sec_bit = (load_mono_image(sector_map[sec_id]) > 128)
                    occupied_mask |= (sec_bit.astype(np.uint8) << sec_id)

        if occupied_mask is None:
            unified_candidates = [
                os.path.join(eye_dir, f"sector_mask_{eye.lower()}.png"),
                os.path.join(eye_dir, "sector_mask.png")
            ]
            u_p = next((c for c in unified_candidates if os.path.exists(c)), None)
            if u_p:
                occupied_mask = load_mono_image(u_p)

        if occupied_mask is None or is_evaluated is None:
            print(f"[!] 警告: {eye_dir} のマスクまたは評価フラグが取得できないためスキップします。")
            continue

        # 領域の厳密分離: R, E, D, D_ghost
        E = R & is_evaluated

        if origin_type_map is not None:
            # D: R かつ 未評価 かつ 最前面が物理点群 (origin == 0) → 直接遮蔽
            D = R & (~E) & (origin_type_map == 0)
            # D_ghost: R かつ 未評価 かつ 非点群 (origin != 0)
            # → VO 内に残留した初期統合ゴースト画素。遮蔽判定には含めない
            D_ghost = R & (~E) & (origin_type_map != 0)
            d_ghost_count = int(np.count_nonzero(D_ghost))
            if d_ghost_count > 0:
                print(f"  [info] D_ghost (未評価非点群): {d_ghost_count:,} px ← VO内ゴースト(黒、遮蔽判定外)")
        else:
            D = R & (~E)
            D_ghost = np.zeros_like(R)

        # E 内でのパターン別集計
        eval_occ = occupied_mask[E]
        eval_gt_vis = gt_vis[E]
        V_256 = np.bincount(eval_occ[eval_gt_vis], minlength=256)
        O_256 = np.bincount(eval_occ[~eval_gt_vis], minlength=256)

        # D 内での固定項集計 (物理点群直接遮蔽のみ。D_ghost は含まない)
        direct_gt_vis = gt_vis[D]
        direct_gt_visible = int(np.count_nonzero(direct_gt_vis))
        direct_gt_occluded = int(np.count_nonzero(~direct_gt_vis))


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
            
        meta_comment = (
            f"# schema_version=2,"
            f"direct_gt_visible={direct_gt_visible},"
            f"direct_gt_occluded={direct_gt_occluded},"
            f"total_eval_pixels={int(np.count_nonzero(E))},"
            f"direct_occluded_pixels={int(np.count_nonzero(D))},"
            f"total_vo_pixels={total_vo}\n"
        )
        
        with open(csv_path, 'w', newline='', encoding='utf-8') as f:
            f.write(meta_comment)
            writer = csv.DictWriter(f, fieldnames=list(pattern_rows[0].keys()))
            writer.writeheader()
            writer.writerows(pattern_rows)
            
        generated_count += 1
        
    elapsed = time.time() - t0
    if generated_count > 0:
        print(f"[+] {generated_count} 件の pattern_counts_256.csv を厳密自動集計・保存しました ({elapsed:.2f}秒)。\n")


# ==============================================================================
# 3. データ読込 & 検証 (load_dataset)
# ==============================================================================
def load_dataset(root_dir, lut):
    """
    root_dir 配下から pattern_counts_256.csv を再帰検索し、検証して読み込む。
    未集計データがある場合は自動集計を実行する。
    """
    auto_generate_missing_pattern_counts(root_dir, lut)
    
    csv_paths = sorted(glob.glob(os.path.join(root_dir, "**", "pattern_counts_256.csv"), recursive=True))
    csv_paths = [p for p in csv_paths if not any(ex in p for ex in EXCLUDED_DIR_PATTERNS)]
    if not csv_paths:
        print(f"[!] pattern_counts_256.csv が見つかりませんでした: {root_dir}")
        return []
        
    dataset = []
    print(f"[*] {len(csv_paths)} 件の pattern_counts_256.csv を検出しました。検証中...")
    
    for path in csv_paths:
        norm_path = os.path.normpath(path)
        parts = norm_path.split(os.sep)
        
        case_name = "UnknownCase"
        density_str = "UnknownDensity"
        eye = "UnknownEye"
        
        for idx, p in enumerate(parts):
            if p in ["Sector8MaskSweep", "Pattern256MaskSweep", "SectorMaskSweep", "StageDiagnosis"] and idx > 0:
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
        
        meta = {}
        rows = []
        data_lines = []
        with open(path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
            
        for line in lines:
            if line.startswith('#'):
                parts_comment = line.lstrip('#').strip().split(',')
                for c_item in parts_comment:
                    if '=' in c_item:
                        k, v = c_item.split('=', 1)
                        meta[k.strip()] = v.strip()
            else:
                data_lines.append(line)
                
        reader = csv.DictReader(data_lines)
        rows = list(reader)
            
        if len(rows) != 256:
            continue
            
        V = np.zeros(256, dtype=np.int64)
        O = np.zeros(256, dtype=np.int64)
        
        valid = True
        for r in rows:
            m = int(r['mask'])
            v = int(r['gt_visible_count'])
            o = int(r['gt_occluded_count'])
            if v < 0 or o < 0:
                valid = False; break
            V[m] = v
            O[m] = o
            
        if not valid:
            continue
            
        direct_gt_visible = int(meta.get('direct_gt_visible', 0))
        direct_gt_occluded = int(meta.get('direct_gt_occluded', 0))
        schema_version = int(meta.get('schema_version', 1))

        G_sector = int(np.sum(V))
        G = G_sector + direct_gt_visible
        if G == 0:
            continue
            
        total_eval_pixels = int(np.sum(V) + np.sum(O))
        direct_occluded_pixels = direct_gt_visible + direct_gt_occluded
        vo_pixels = total_eval_pixels + direct_occluded_pixels

        item_d = {
            'data_id': data_id,
            'case': case_name,
            'density': float(density_str) if density_str != "UnknownDensity" else 0.0,
            'density_str': density_str,
            'eye': eye,
            'file_path': path,
            'V': V,
            'O': O,
            'direct_gt_visible': direct_gt_visible,
            'direct_gt_occluded': direct_gt_occluded,
            'G_sector': G_sector,
            'G': G,
            'total_eval_pixels': total_eval_pixels,
            'direct_occluded_pixels': direct_occluded_pixels,
            'vo_pixels': vo_pixels,
            'schema_version': schema_version
        }

        # 追加遮蔽なし基準 (全パターン可視 z = 1)
        z_all_vis = np.ones(256, dtype=np.int32)
        _, _, _, _, no_occ_iou = score_visibility_rule(z_all_vis, item_d)
        item_d['no_occ_iou'] = no_occ_iou
        
        # 旧版で画面全体の黒背景が混入した古い壊れたデータを検出して除外
        if schema_version < 2 and no_occ_iou < 0.4 and O[0] > G:
            print(f"[!] 警告: {path} はVOシルエット未適用による背景黒混入データと判定されたためスキップします (no_occ_iou: {no_occ_iou*100:.1f}%, O[0]: {O[0]:,})")
            continue
        
        dataset.append(item_d)
        
    print(f"[+] {len(dataset)} 件の有効なデータセットを読み込み・検証しました。\n")
    return dataset


# ==============================================================================
# 4. 拡張共通評価関数 (evaluate_lut_extended)
# ==============================================================================
def evaluate_lut_extended(z, dataset, base_ious=None):
    """
    256項目の可視判定 z in {0, 1}^256 を全データセットに適用し、
    各データの TP, FP, FN, IoU、候補領域内の誤画素率 e、正味誤画素削減率 Δe、
    およびベースラインに対する 1-IoU 相対低減率 r を計算。
    
    base_ious: shape (D,) の各ケースの基準IoU (Noneの場合は相対低減率計算をスキップ)
    """
    results = []
    total_tp = 0
    total_fp = 0
    total_fn = 0
    total_M = 0
    
    for i, d in enumerate(dataset):
        tp, fp, fn, denom, iou = score_visibility_rule(z, d)
        
        M = d['total_eval_pixels']
        O_total = int(np.sum(d['O']))
        # 候補領域内の誤画素率 e = (FN + FP) / M
        err_rate = (fn + fp) / M if M > 0 else 0.0
        # 正味の誤画素削減率 Δe = (O - FP - FN) / M
        net_err_reduction = (O_total - fp - fn) / M if M > 0 else 0.0
        
        # 1-IoU 相対低減率
        rel_reduction = 0.0
        iou_diff = 0.0
        if base_ious is not None:
            b_iou = base_ious[i]
            iou_diff = iou - b_iou
            denom_r = max(1.0 - b_iou, 1e-6)
            rel_reduction = (iou - b_iou) / denom_r
            
        results.append({
            'data_id': d['data_id'],
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'tp': tp,
            'fp': fp,
            'fn': fn,
            'iou': iou,
            'err_rate': err_rate,
            'net_err_reduction': net_err_reduction,
            'rel_reduction': rel_reduction,
            'iou_diff': iou_diff,
            'no_occ_iou': d['no_occ_iou']
        })
        total_tp += tp
        total_fp += fp
        total_fn += fn
        total_M += M
        
    mean_iou = float(np.mean([r['iou'] for r in results]))
    mean_net_err = float(np.mean([r['net_err_reduction'] for r in results]))
    
    mean_rel_red = float(np.mean([r['rel_reduction'] for r in results])) if base_ious is not None else 0.0
    min_rel_red = float(np.min([r['rel_reduction'] for r in results])) if base_ious is not None else 0.0
    worst_iou_drop = float(np.min([r['iou_diff'] for r in results])) if base_ious is not None else 0.0
    
    pooled_denom = total_tp + total_fn + total_fp
    pooled_iou = float(total_tp / pooled_denom) if pooled_denom > 0 else 0.0
    
    return {
        'individual': results,
        'mean_iou': mean_iou,
        'mean_net_err_reduction': mean_net_err,
        'mean_rel_reduction': mean_rel_red,
        'worst_rel_drop': min_rel_red,
        'worst_iou_drop': worst_iou_drop,
        'pooled_iou': pooled_iou,
        'total_tp': total_tp,
        'total_fp': total_fp,
        'total_fn': total_fn
    }


# ==============================================================================
# 5. 固定8候補 & 固定20候補の最適化 (目的関数A & B)
# ==============================================================================
def optimize_fixed_8(dataset, lut, base_ious=None, weights=None):
    """
    固定8候補の個別最適と共通最適 (重みなし/重み付き) を計算
    weights が指定された場合は重み付き平均IoU (相対低減率) を最大化
    """
    cands_z = []
    for R in range(1, 9):
        z = (lut['n_occ'] < R).astype(np.int32)
        cands_z.append((R, z))
        
    D = len(dataset)
    if weights is None:
        norm_w = np.ones(D) / D
    else:
        norm_w = weights / np.sum(weights)
        
    # 個別最適
    indiv_opt = []
    for i, d in enumerate(dataset):
        best_r = 1
        best_score = -1.0
        best_res = None
        best_z = None
        for R, z in cands_z:
            tp, fp, fn, denom, iou = score_visibility_rule(z, d)
            
            # 個別最適は常にそのデータの IoU 最大化
            if iou > best_score:
                best_score = iou
                best_r = R
                best_z = z
                M = d['total_eval_pixels']
                O_tot = int(np.sum(d['O']))
                net_err = (O_tot - fp - fn) / M if M > 0 else 0.0
                rel_red = (iou - base_ious[i]) / max(1.0 - base_ious[i], 1e-6) if base_ious is not None else 0.0
                best_res = {'tp': tp, 'fp': fp, 'fn': fn, 'iou': iou, 'net_err_reduction': net_err, 'rel_reduction': rel_red}
                
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
        
    # 共通最適
    cands_eval = []
    for R, z in cands_z:
        ev = evaluate_lut_extended(z, dataset, base_ious)
        # 目的関数値: 重み付き平均 IoU
        obj_val = float(np.sum([ev['individual'][i]['iou'] * norm_w[i] for i in range(D)]))
        cands_eval.append({
            'r': R,
            'rule_desc': f"Occ < {R}",
            'summary': f"Threshold R={R} (Occ 0..{R - 1})",
            'details': f"Occ 0..{R - 1} ({int(np.sum(z))} / 256 patterns)",
            'hex_mask': z_to_hex(z),
            'obj_score': obj_val,
            'mean_iou': ev['mean_iou'],
            'mean_rel_reduction': ev['mean_rel_reduction'],
            'mean_net_err_reduction': ev['mean_net_err_reduction'],
            'worst_iou_drop': ev['worst_iou_drop'],
            'worst_rel_drop': ev['worst_rel_drop'],
            'z': z,
            'eval': ev
        })
        
    cands_eval.sort(key=lambda x: x['obj_score'], reverse=True)
    for rank, cand in enumerate(cands_eval, 1):
        cand['rank'] = rank
        
    best_cand = cands_eval[0]
    top5_cands = cands_eval[:5]
            
    return {
        'individual': indiv_opt,
        'common_r': best_cand['r'],
        'common_rule_desc': best_cand['rule_desc'],
        'common_summary': best_cand['summary'],
        'common_details': best_cand['details'],
        'common_z': best_cand['z'],
        'common_eval': best_cand['eval'],
        'top5': top5_cands,
        'all_cands': cands_eval
    }

def optimize_fixed_20(dataset, lut, base_ious=None, weights=None):
    """
    固定20候補の個別最適と共通最適 (重みなし/重み付き) を計算
    """
    cands_z = []
    for R_th, L_th in RULES_20_CANDIDATES:
        is_occ = (lut['n_occ'] >= R_th) & (lut['l_max'] <= L_th)
        z = (~is_occ).astype(np.int32)
        cands_z.append((R_th, L_th, z))
        
    D = len(dataset)
    if weights is None:
        norm_w = np.ones(D) / D
    else:
        norm_w = weights / np.sum(weights)
        
    # 個別最適
    indiv_opt = []
    for i, d in enumerate(dataset):
        best_r_th = 1
        best_l_th = 8
        best_score = -1.0
        best_res = None
        best_z = None
        for R_th, L_th, z in cands_z:
            tp, fp, fn, denom, iou = score_visibility_rule(z, d)
            if iou > best_score:
                best_score = iou
                best_r_th = R_th
                best_l_th = L_th
                best_z = z
                M = d['total_eval_pixels']
                O_tot = int(np.sum(d['O']))
                net_err = (O_tot - fp - fn) / M if M > 0 else 0.0
                rel_red = (iou - base_ious[i]) / max(1.0 - base_ious[i], 1e-6) if base_ious is not None else 0.0
                best_res = {'tp': tp, 'fp': fp, 'fn': fn, 'iou': iou, 'net_err_reduction': net_err, 'rel_reduction': rel_red}
                
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
        
    # 共通最適
    cands_eval = []
    for R_th, L_th, z in cands_z:
        ev = evaluate_lut_extended(z, dataset, base_ious)
        obj_val = float(np.sum([ev['individual'][i]['iou'] * norm_w[i] for i in range(D)]))
        rule_name = f"Occ < {R_th} or Unocc > {L_th}" if L_th < 8 else f"Occ < {R_th}"
        cands_eval.append({
            'r_th': R_th,
            'l_th': L_th,
            'rule_desc': rule_name,
            'summary': f"R_th={R_th}, L_th={L_th}",
            'details': f"{int(np.sum(z))} / 256 patterns (occ: {occ_breakdown_str(z, lut['n_occ'])})",
            'hex_mask': z_to_hex(z),
            'obj_score': obj_val,
            'mean_iou': ev['mean_iou'],
            'mean_rel_reduction': ev['mean_rel_reduction'],
            'mean_net_err_reduction': ev['mean_net_err_reduction'],
            'worst_iou_drop': ev['worst_iou_drop'],
            'worst_rel_drop': ev['worst_rel_drop'],
            'z': z,
            'eval': ev
        })
        
    cands_eval.sort(key=lambda x: x['obj_score'], reverse=True)
    for rank, cand in enumerate(cands_eval, 1):
        cand['rank'] = rank
        
    best_cand = cands_eval[0]
    top5_cands = cands_eval[:5]
    
    return {
        'individual': indiv_opt,
        'common_rule_desc': best_cand['rule_desc'],
        'common_summary': best_cand['summary'],
        'common_details': best_cand['details'],
        'common_params': (best_cand['r_th'], best_cand['l_th']),
        'common_z': best_cand['z'],
        'common_eval': best_cand['eval'],
        'top5': top5_cands,
        'all_cands': cands_eval
    }


# ==============================================================================
# 6. 個別最適LUT (並べ替え探索)
# ==============================================================================
def optimize_single_iou_lut(V_classes, O_classes, G):
    """
    単一データに対する並べ替え探索 (O(C log C))
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

def get_individual_optima_36_and_256(dataset, lut, base_ious=None):
    """
    各データに対して 36クラスおよび 256パターンの個別最適を計算
    """
    opt_36 = []
    opt_256 = []
    
    for i, d in enumerate(dataset):
        M = d['total_eval_pixels']
        O_tot = int(np.sum(d['O']))
        b_iou = base_ious[i] if base_ious is not None else 0.0
        
        # 36クラス
        V_36 = np.zeros(36, dtype=np.int64)
        O_36 = np.zeros(36, dtype=np.int64)
        for m in range(256):
            cid = lut['rot_cid'][m]
            V_36[cid] += d['V'][m]
            O_36[cid] += d['O'][m]
        iou_36, z_36 = optimize_single_iou_lut(V_36, O_36, d['G'])
        
        z_256_from_36 = np.array([z_36[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        tp_36, fp_36, fn_36, denom_36, iou_36 = score_visibility_rule(z_256_from_36, d)
        net_err_36 = (O_tot - fp_36 - fn_36) / M if M > 0 else 0.0
        rel_red_36 = (iou_36 - b_iou) / max(1.0 - b_iou, 1e-6) if base_ious is not None else 0.0
        
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
            'net_err_reduction': net_err_36,
            'rel_reduction': rel_red_36,
            'z_36': z_36,
            'z_256': z_256_from_36
        })
        
        # 256パターン
        iou_256, z_256 = optimize_single_iou_lut(d['V'], d['O'], d['G'])
        tp_256, fp_256, fn_256, denom_256, iou_256 = score_visibility_rule(z_256, d)
        net_err_256 = (O_tot - fp_256 - fn_256) / M if M > 0 else 0.0
        rel_red_256 = (iou_256 - b_iou) / max(1.0 - b_iou, 1e-6) if base_ious is not None else 0.0
        
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
            'net_err_reduction': net_err_256,
            'rel_reduction': rel_red_256,
            'z_256': z_256
        })
        
    return opt_36, opt_256


# ==============================================================================
# 7. 一般化重み付き共通最適化 MILP ソルバー (optimize_weighted_iou_milp)
# ==============================================================================
def optimize_weighted_iou_milp(counts_V, counts_O, G_list, fallback_z_classes, weights=None, time_limit_sec=60):
    """
    McCormick 緩和 MILP ソルバー (等重み平均IoU または 重み付き平均IoU/相対低減率を直接最大化)
    weights: shape (D,) の各ケースの重要度重み。None の場合は等重み (1/D)
    """
    start_time = time.time()
    D, C = counts_V.shape
    
    if weights is None:
        norm_w = np.ones(D, dtype=np.float64) / D
    else:
        norm_w = np.array(weights, dtype=np.float64) / np.sum(weights)
        
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
    
    # 目的関数: max sum_i norm_w[i] * y[i] * (sum_j V[i,j] * z[j])
    c = np.zeros(num_vars, dtype=np.float64)
    for i in range(D):
        for j in range(C_act):
            idx_w = C_act + D + i * C_act + j
            c[idx_w] = - norm_w[i] * act_counts_V[i, j]
            
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
# 8. 実行エンジン & 出力 (run_pattern_rules_optimization)
# ==============================================================================
def run_pattern_rules_optimization(dataset_root=None, output_dir=None):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_dataset_dir = os.path.abspath(os.path.join(script_dir, "../../../../../Estimation/SICESI_Dataset"))
    
    if dataset_root is None:
        rawtest_dir = os.path.join(default_dataset_dir, "RawTest")
        if os.path.exists(rawtest_dir):
            dataset_root = rawtest_dir
        elif os.path.exists(default_dataset_dir):
            dataset_root = default_dataset_dir
        else:
            dataset_root = script_dir
    else:
        # SICESI_Dataset 直下が渡された場合でも RawTest が存在すれば自動で RawTest を探索対象とする
        rawtest_cand = os.path.join(dataset_root, "RawTest")
        if os.path.exists(rawtest_cand):
            dataset_root = rawtest_cand
            
    dataset_root = os.path.abspath(dataset_root)
    
    if output_dir is None:
        # 親が SICESI_Dataset の場合はその直下の RuleOptimizationResults に配置
        parent_dir = os.path.dirname(dataset_root)
        if os.path.basename(parent_dir) == "SICESI_Dataset":
            output_dir = os.path.join(parent_dir, "RuleOptimizationResults")
        else:
            output_dir = os.path.join(dataset_root, "RuleOptimizationResults")
    os.makedirs(output_dir, exist_ok=True)
    
    lut = build_256_lookup()
    
    print("=" * 80)
    print("【占有パターンCSVから個別最適・共通最適を求める自動解析】")
    print(f"  探索対象ルート: {dataset_root}")
    print(f"  出力先フォルダ: {output_dir}")
    print("=" * 80)
    
    # 1. 全CSVの読み込み
    dataset = load_dataset(dataset_root, lut)
    if not dataset:
        print("[!] 解析対象データが存在しませんでした。")
        return
        
    D = len(dataset)
    G_list = np.array([d['G'] for d in dataset], dtype=np.int64)
    
    # カウント行列の事前作成
    counts_V_36 = np.zeros((D, 36), dtype=np.int64)
    counts_O_36 = np.zeros((D, 36), dtype=np.int64)
    for i, d in enumerate(dataset):
        for m in range(256):
            cid = lut['rot_cid'][m]
            counts_V_36[i, cid] += d['V'][m]
            counts_O_36[i, cid] += d['O'][m]
    counts_V_256 = np.array([d['V'] for d in dataset], dtype=np.int64)
    counts_O_256 = np.array([d['O'] for d in dataset], dtype=np.int64)
    
    # --------------------------------------------------------------------------
    # ステップ 1: 基準従来法 (固定8候補の共通最適) の決定
    # --------------------------------------------------------------------------
    print("[*] [ステップ 1/5] 基準従来法 (固定8候補 共通占有数ルール) の決定中...")
    res_8_base = optimize_fixed_8(dataset, lut)
    base_ious = np.array([r['iou'] for r in res_8_base['common_eval']['individual']], dtype=np.float64)
    print(f"    -> 基準規則 (第1位): {res_8_base['common_rule_desc']} (平均IoU: {res_8_base['common_eval']['mean_iou']*100:.4f}%)")
    print("    -> 【固定8候補 上位5位ランキング】")
    for cand in res_8_base['top5']:
        print(f"       第{cand['rank']}位: {cand['rule_desc']:<25} | 平均IoU: {cand['mean_iou']*100:.4f}% | 可視: {int(np.sum(cand['z'])):>3}/256 | 最悪低下: {cand['worst_iou_drop']*100:+.2f}%pt")
    
    # 相対低減率最大化用の重み w_i = 1 / max(1 - J_base, 1e-4)
    weights_rel = 1.0 / np.maximum(1.0 - base_ious, 1e-4)
    print(f"    -> 相対低減率 重み w_i 分布: min={weights_rel.min():.2f}, mean={weights_rel.mean():.2f}, max={weights_rel.max():.2f}")
    
    # 基準IoUをセットして再評価
    res_8_base = optimize_fixed_8(dataset, lut, base_ious=base_ious)
    
    # --------------------------------------------------------------------------
    # ステップ 2: 目的関数 A (平均IoU最大化: Unweighted) による最適化
    # --------------------------------------------------------------------------
    print("\n" + "-" * 80)
    print("[*] [ステップ 2/5] 【目的関数 A: 平均IoU最大化 (Unweighted)】の最適化を実行中...")
    print("-" * 80)
    
    # 2.1 固定20候補
    res_20_A = optimize_fixed_20(dataset, lut, base_ious=base_ious)
    print(f"  [20候補 A] 最良 (第1位): {res_20_A['common_rule_desc']} (平均IoU: {res_20_A['common_eval']['mean_iou']*100:.4f}%, 平均相対低減: {res_20_A['common_eval']['mean_rel_reduction']*100:+.2f}%)")
    print(f"    -> 規則詳細: {res_20_A['common_summary']} | 可視パターン: {res_20_A['common_details']}")
    print("    -> 【固定20候補 上位5位ランキング (平均IoU順)】")
    for cand in res_20_A['top5']:
        print(f"       第{cand['rank']}位: {cand['rule_desc']:<25} | 平均IoU: {cand['mean_iou']*100:.4f}% | 相対低減: {cand['mean_rel_reduction']*100:+.2f}% | 可視: {int(np.sum(cand['z'])):>3}/256 | 最悪低下: {cand['worst_iou_drop']*100:+.2f}%pt")
    
    # 2.2 36クラスLUT (MILP A)
    fallback_z_256_A = res_20_A['common_z']
    fallback_z_36_A = np.zeros(36, dtype=np.int32)
    for cid in range(36):
        rep = lut['unique_reps'][cid]
        fallback_z_36_A[cid] = fallback_z_256_A[rep]
        
    print("  [36クラス A] MILP 求解中 (平均IoU最大化)...")
    z_36_A, _, status_36_A, time_36_A, _ = optimize_weighted_iou_milp(
        counts_V_36, counts_O_36, G_list, fallback_z_36_A, weights=None, time_limit_sec=60
    )
    z_36_A_as_256 = np.array([z_36_A[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
    eval_36_A = evaluate_lut_extended(z_36_A_as_256, dataset, base_ious)
    # 包含関係保証: 36クラスは20候補を包含するため、平均IoUが下回った場合は20候補ルールを採用
    if eval_36_A['mean_iou'] < res_20_A['common_eval']['mean_iou']:
        print(f"    -> [包含関係補正] 36クラス A の MILP 解 ({eval_36_A['mean_iou']*100:.4f}%) が固定20候補 ({res_20_A['common_eval']['mean_iou']*100:.4f}%) を下回ったため、包含規則を採用")
        z_36_A = fallback_z_36_A.copy()
        z_36_A_as_256 = np.array([z_36_A[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        eval_36_A = evaluate_lut_extended(z_36_A_as_256, dataset, base_ious)

    vis_c_A = [c for c in range(36) if z_36_A[c] == 1]
    print(f"    -> 完了 ({time_36_A:.2f}秒, {status_36_A}): 平均IoU = {eval_36_A['mean_iou']*100:.4f}%, 平均相対低減 = {eval_36_A['mean_rel_reduction']*100:+.2f}% (可視: {len(vis_c_A)}/36 クラス, 最悪悪化: {eval_36_A['worst_rel_drop']*100:+.2f}%)")
    print(f"    -> クラス詳細: {compress_indices(vis_c_A, prefix='c')} | 各占有数の可視クラス数: {class_breakdown_str(z_36_A, lut)}")
    
    # 2.3 256パターンLUT (MILP A)
    print("  [256パターン A] MILP 求解中 (平均IoU最大化)...")
    z_256_A, _, status_256_A, time_256_A, _ = optimize_weighted_iou_milp(
        counts_V_256, counts_O_256, G_list, z_36_A_as_256, weights=None, time_limit_sec=60
    )
    eval_256_A = evaluate_lut_extended(z_256_A, dataset, base_ious)
    # 包含関係保証: 256パターンは36クラスを包含するため、平均IoUが下回った場合は36クラスルールを採用
    if eval_256_A['mean_iou'] < eval_36_A['mean_iou']:
        print(f"    -> [包含関係補正] 256パターン A の MILP 解 ({eval_256_A['mean_iou']*100:.4f}%) が36クラス ({eval_36_A['mean_iou']*100:.4f}%) を下回ったため、包含規則を採用")
        z_256_A = z_36_A_as_256.copy()
        eval_256_A = evaluate_lut_extended(z_256_A, dataset, base_ious)

    vis_m_A = [m for m in range(256) if z_256_A[m] == 1]
    print(f"    -> 完了 ({time_256_A:.2f}秒, {status_256_A}): 平均IoU = {eval_256_A['mean_iou']*100:.4f}%, 平均相対低減 = {eval_256_A['mean_rel_reduction']*100:+.2f}% (可視: {len(vis_m_A)}/256 パターン, 最悪悪化: {eval_256_A['worst_rel_drop']*100:+.2f}%)")
    print(f"    -> 各占有数の可視パターン数: {occ_breakdown_str(z_256_A, lut['n_occ'])}")
    print(f"    -> パターンHEX: {z_to_hex(z_256_A)}")
    
    # --------------------------------------------------------------------------
    # ステップ 3: 目的関数 B (1-IoU 相対低減率最大化: Weighted) による最適化
    # --------------------------------------------------------------------------
    print("\n" + "-" * 80)
    print("[*] [ステップ 3/5] 【目的関数 B: 1-IoU 相対低減率最大化 (Weighted)】の最適化を実行中...")
    print("-" * 80)
    
    # 3.1 固定8候補 B
    res_8_B = optimize_fixed_8(dataset, lut, base_ious=base_ious, weights=weights_rel)
    print(f"  [8候補 B] 最良 (第1位): {res_8_B['common_rule_desc']} (平均相対低減: {res_8_B['common_eval']['mean_rel_reduction']*100:+.2f}%, 平均IoU: {res_8_B['common_eval']['mean_iou']*100:.4f}%)")
    print("    -> 【固定8候補 上位5位ランキング (相対低減率順)】")
    for cand in res_8_B['top5']:
        print(f"       第{cand['rank']}位: {cand['rule_desc']:<25} | 相対低減: {cand['mean_rel_reduction']*100:+.2f}% | 平均IoU: {cand['mean_iou']*100:.4f}% | 可視: {int(np.sum(cand['z'])):>3}/256 | 最悪低下: {cand['worst_iou_drop']*100:+.2f}%pt")
    
    # 3.2 固定20候補 B
    res_20_B = optimize_fixed_20(dataset, lut, base_ious=base_ious, weights=weights_rel)
    print(f"  [20候補 B] 最良 (第1位): {res_20_B['common_rule_desc']} (平均相対低減: {res_20_B['common_eval']['mean_rel_reduction']*100:+.2f}%, 平均IoU: {res_20_B['common_eval']['mean_iou']*100:.4f}%)")
    print(f"    -> 規則詳細: {res_20_B['common_summary']} | 可視パターン: {res_20_B['common_details']}")
    print("    -> 【固定20候補 上位5位ランキング (相対低減率順)】")
    for cand in res_20_B['top5']:
        print(f"       第{cand['rank']}位: {cand['rule_desc']:<25} | 相対低減: {cand['mean_rel_reduction']*100:+.2f}% | 平均IoU: {cand['mean_iou']*100:.4f}% | 可視: {int(np.sum(cand['z'])):>3}/256 | 最悪低下: {cand['worst_iou_drop']*100:+.2f}%pt")
    
    # 3.3 36クラスLUT (MILP B)
    fallback_z_256_B = res_20_B['common_z']
    fallback_z_36_B = np.zeros(36, dtype=np.int32)
    for cid in range(36):
        rep = lut['unique_reps'][cid]
        fallback_z_36_B[cid] = fallback_z_256_B[rep]

    print("  [36クラス B] MILP 求解中 (相対低減率最大化)...")
    z_36_B, _, status_36_B, time_36_B, _ = optimize_weighted_iou_milp(
        counts_V_36, counts_O_36, G_list, fallback_z_36_B, weights=weights_rel, time_limit_sec=60
    )
    z_36_B_as_256 = np.array([z_36_B[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
    eval_36_B = evaluate_lut_extended(z_36_B_as_256, dataset, base_ious)
    # 包含関係保証: 36クラスは20候補を包含
    if eval_36_B['mean_rel_reduction'] < res_20_B['common_eval']['mean_rel_reduction']:
        print(f"    -> [包含関係補正] 36クラス B の MILP 解 ({eval_36_B['mean_rel_reduction']*100:+.2f}%) が固定20候補 ({res_20_B['common_eval']['mean_rel_reduction']*100:+.2f}%) を下回ったため、包含規則を採用")
        z_36_B = fallback_z_36_B.copy()
        z_36_B_as_256 = np.array([z_36_B[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        eval_36_B = evaluate_lut_extended(z_36_B_as_256, dataset, base_ious)

    vis_c_B = [c for c in range(36) if z_36_B[c] == 1]
    print(f"    -> 完了 ({time_36_B:.2f}秒, {status_36_B}): 平均相対低減 = {eval_36_B['mean_rel_reduction']*100:+.2f}%, 平均IoU = {eval_36_B['mean_iou']*100:.4f}% (可視: {len(vis_c_B)}/36 クラス, 最悪悪化: {eval_36_B['worst_rel_drop']*100:+.2f}%)")
    print(f"    -> クラス詳細: {compress_indices(vis_c_B, prefix='c')} | 各占有数の可視クラス数: {class_breakdown_str(z_36_B, lut)}")
    
    # 3.4 256パターンLUT (MILP B)
    print("  [256パターン B] MILP 求解中 (相対低減率最大化)...")
    z_256_B, _, status_256_B, time_256_B, _ = optimize_weighted_iou_milp(
        counts_V_256, counts_O_256, G_list, z_36_B_as_256, weights=weights_rel, time_limit_sec=60
    )
    eval_256_B = evaluate_lut_extended(z_256_B, dataset, base_ious)
    # 包含関係保証: 256パターンは36クラスを包含
    if eval_256_B['mean_rel_reduction'] < eval_36_B['mean_rel_reduction']:
        print(f"    -> [包含関係補正] 256パターン B の MILP 解 ({eval_256_B['mean_rel_reduction']*100:+.2f}%) が36クラス ({eval_36_B['mean_rel_reduction']*100:+.2f}%) を下回ったため、包含規則を採用")
        z_256_B = z_36_B_as_256.copy()
        eval_256_B = evaluate_lut_extended(z_256_B, dataset, base_ious)

    vis_m_B = [m for m in range(256) if z_256_B[m] == 1]
    print(f"    -> 完了 ({time_256_B:.2f}秒, {status_256_B}): 平均相対低減 = {eval_256_B['mean_rel_reduction']*100:+.2f}%, 平均IoU = {eval_256_B['mean_iou']*100:.4f}% (可視: {len(vis_m_B)}/256 パターン, 最悪悪化: {eval_256_B['worst_rel_drop']*100:+.2f}%)")
    print(f"    -> 各占有数の可視パターン数: {occ_breakdown_str(z_256_B, lut['n_occ'])}")
    print(f"    -> パターンHEX: {z_to_hex(z_256_B)}")
    
    # --------------------------------------------------------------------------
    # ステップ 4: 個別最適 (36クラス & 256パターン)
    # --------------------------------------------------------------------------
    print("\n[*] [ステップ 4/5] 個別最適 (36クラス & 256パターン) の計算中...")
    indiv_36, indiv_256 = get_individual_optima_36_and_256(dataset, lut, base_ious=base_ious)
    
    # --------------------------------------------------------------------------
    # ステップ 5: CSV ファイル群の出力
    # --------------------------------------------------------------------------
    print("\n[*] [ステップ 5/5] CSV ファイル群の保存中...")
    
    # 5.1 共通規則サマリー (common_rules_summary.csv)
    summary_rows = [
        # 目的関数 A
        {
            'objective': 'Mean_IoU_Maximized',
            'method': 'Fixed_8_Candidates',
            'mean_iou': f"{res_8_base['common_eval']['mean_iou']:.6f}",
            'mean_iou_pct': f"{res_8_base['common_eval']['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{res_8_base['common_eval']['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{res_8_base['common_eval']['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{res_8_base['common_eval']['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{res_8_base['common_eval']['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(res_8_base['common_z']))} / 256",
            'best_rule': res_8_base['common_rule_desc'],
            'rule_summary': res_8_base['common_summary'],
            'selected_details': res_8_base['common_details'],
            'hex_mask': z_to_hex(res_8_base['common_z'])
        },
        {
            'objective': 'Mean_IoU_Maximized',
            'method': 'Fixed_20_Candidates',
            'mean_iou': f"{res_20_A['common_eval']['mean_iou']:.6f}",
            'mean_iou_pct': f"{res_20_A['common_eval']['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{res_20_A['common_eval']['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{res_20_A['common_eval']['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{res_20_A['common_eval']['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{res_20_A['common_eval']['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(res_20_A['common_z']))} / 256",
            'best_rule': res_20_A['common_rule_desc'],
            'rule_summary': res_20_A['common_summary'],
            'selected_details': res_20_A['common_details'],
            'hex_mask': z_to_hex(res_20_A['common_z'])
        },
        {
            'objective': 'Mean_IoU_Maximized',
            'method': 'Oracle_36_RotationLUT',
            'mean_iou': f"{eval_36_A['mean_iou']:.6f}",
            'mean_iou_pct': f"{eval_36_A['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{eval_36_A['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{eval_36_A['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{eval_36_A['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{eval_36_A['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(z_36_A_as_256))} / 256",
            'best_rule': f"{len(vis_c_A)} / 36 classes",
            'rule_summary': f"occ_classes: {class_breakdown_str(z_36_A, lut)}",
            'selected_details': f"{compress_indices(vis_c_A, prefix='c')} ({int(np.sum(z_36_A_as_256))} / 256 patterns)",
            'hex_mask': z_to_hex(z_36_A_as_256)
        },
        {
            'objective': 'Mean_IoU_Maximized',
            'method': 'Oracle_256_ExactLUT',
            'mean_iou': f"{eval_256_A['mean_iou']:.6f}",
            'mean_iou_pct': f"{eval_256_A['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{eval_256_A['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{eval_256_A['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{eval_256_A['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{eval_256_A['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{len(vis_m_A)} / 256",
            'best_rule': f"{len(vis_m_A)} / 256 patterns",
            'rule_summary': f"occ_patterns: {occ_breakdown_str(z_256_A, lut['n_occ'])}",
            'selected_details': compress_indices(vis_m_A, prefix="m"),
            'hex_mask': z_to_hex(z_256_A)
        },
        # 目的関数 B
        {
            'objective': 'Relative_Reduction_Maximized',
            'method': 'Fixed_8_Candidates',
            'mean_iou': f"{res_8_B['common_eval']['mean_iou']:.6f}",
            'mean_iou_pct': f"{res_8_B['common_eval']['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{res_8_B['common_eval']['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{res_8_B['common_eval']['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{res_8_B['common_eval']['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{res_8_B['common_eval']['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(res_8_B['common_z']))} / 256",
            'best_rule': res_8_B['common_rule_desc'],
            'rule_summary': res_8_B['common_summary'],
            'selected_details': res_8_B['common_details'],
            'hex_mask': z_to_hex(res_8_B['common_z'])
        },
        {
            'objective': 'Relative_Reduction_Maximized',
            'method': 'Fixed_20_Candidates',
            'mean_iou': f"{res_20_B['common_eval']['mean_iou']:.6f}",
            'mean_iou_pct': f"{res_20_B['common_eval']['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{res_20_B['common_eval']['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{res_20_B['common_eval']['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{res_20_B['common_eval']['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{res_20_B['common_eval']['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(res_20_B['common_z']))} / 256",
            'best_rule': res_20_B['common_rule_desc'],
            'rule_summary': res_20_B['common_summary'],
            'selected_details': res_20_B['common_details'],
            'hex_mask': z_to_hex(res_20_B['common_z'])
        },
        {
            'objective': 'Relative_Reduction_Maximized',
            'method': 'Oracle_36_RotationLUT',
            'mean_iou': f"{eval_36_B['mean_iou']:.6f}",
            'mean_iou_pct': f"{eval_36_B['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{eval_36_B['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{eval_36_B['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{eval_36_B['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{eval_36_B['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(z_36_B_as_256))} / 256",
            'best_rule': f"{len(vis_c_B)} / 36 classes",
            'rule_summary': f"occ_classes: {class_breakdown_str(z_36_B, lut)}",
            'selected_details': f"{compress_indices(vis_c_B, prefix='c')} ({int(np.sum(z_36_B_as_256))} / 256 patterns)",
            'hex_mask': z_to_hex(z_36_B_as_256)
        },
        {
            'objective': 'Relative_Reduction_Maximized',
            'method': 'Oracle_256_ExactLUT',
            'mean_iou': f"{eval_256_B['mean_iou']:.6f}",
            'mean_iou_pct': f"{eval_256_B['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{eval_256_B['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{eval_256_B['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{eval_256_B['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{eval_256_B['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{len(vis_m_B)} / 256",
            'best_rule': f"{len(vis_m_B)} / 256 patterns",
            'rule_summary': f"occ_patterns: {occ_breakdown_str(z_256_B, lut['n_occ'])}",
            'selected_details': compress_indices(vis_m_B, prefix="m"),
            'hex_mask': z_to_hex(z_256_B)
        }
    ]
    csv_summary = os.path.join(output_dir, "common_rules_summary.csv")
    safe_write_csv(csv_summary, summary_rows, list(summary_rows[0].keys()))
    print(f"[+] 共通規則サマリー (common_rules_summary.csv) を保存しました: {csv_summary}")
    
    # 5.1.5 上位5候補ランキング詳細比較表 (top5_candidate_rules.csv) [新規]
    top5_rows = []
    # 目的関数A (平均IoU最大化)
    for cand in res_8_base['top5']:
        top5_rows.append({
            'objective': 'Mean_IoU_Maximized (Obj A)',
            'method': 'Fixed_8_Candidates',
            'rank': cand['rank'],
            'rule_name': cand['rule_desc'],
            'rule_summary': cand['summary'],
            'mean_iou': f"{cand['mean_iou']:.6f}",
            'mean_iou_pct': f"{cand['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{cand['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{cand['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{cand['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{cand['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(cand['z']))} / 256",
            'selected_details': cand['details'],
            'hex_mask': cand['hex_mask']
        })
    for cand in res_20_A['top5']:
        top5_rows.append({
            'objective': 'Mean_IoU_Maximized (Obj A)',
            'method': 'Fixed_20_Candidates',
            'rank': cand['rank'],
            'rule_name': cand['rule_desc'],
            'rule_summary': cand['summary'],
            'mean_iou': f"{cand['mean_iou']:.6f}",
            'mean_iou_pct': f"{cand['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{cand['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{cand['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{cand['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{cand['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(cand['z']))} / 256",
            'selected_details': cand['details'],
            'hex_mask': cand['hex_mask']
        })
    # 目的関数B (相対低減率最大化)
    for cand in res_8_B['top5']:
        top5_rows.append({
            'objective': 'Relative_Reduction_Maximized (Obj B)',
            'method': 'Fixed_8_Candidates',
            'rank': cand['rank'],
            'rule_name': cand['rule_desc'],
            'rule_summary': cand['summary'],
            'mean_iou': f"{cand['mean_iou']:.6f}",
            'mean_iou_pct': f"{cand['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{cand['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{cand['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{cand['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{cand['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(cand['z']))} / 256",
            'selected_details': cand['details'],
            'hex_mask': cand['hex_mask']
        })
    for cand in res_20_B['top5']:
        top5_rows.append({
            'objective': 'Relative_Reduction_Maximized (Obj B)',
            'method': 'Fixed_20_Candidates',
            'rank': cand['rank'],
            'rule_name': cand['rule_desc'],
            'rule_summary': cand['summary'],
            'mean_iou': f"{cand['mean_iou']:.6f}",
            'mean_iou_pct': f"{cand['mean_iou']*100:.4f}%",
            'mean_rel_reduction': f"{cand['mean_rel_reduction']*100:+.2f}%",
            'mean_net_error_reduc': f"{cand['mean_net_err_reduction']*100:+.2f}%",
            'worst_iou_drop': f"{cand['worst_iou_drop']*100:+.2f}%pt",
            'worst_rel_drop': f"{cand['worst_rel_drop']*100:+.2f}%",
            'visible_patterns': f"{int(np.sum(cand['z']))} / 256",
            'selected_details': cand['details'],
            'hex_mask': cand['hex_mask']
        })
    csv_top5 = os.path.join(output_dir, "top5_candidate_rules.csv")
    safe_write_csv(csv_top5, top5_rows, list(top5_rows[0].keys()))
    print(f"[+] 上位5候補ランキング詳細 (top5_candidate_rules.csv) を保存しました: {csv_top5}")
    
    # 5.2 目的関数A vs B 直接比較表 (objective_comparison.csv) [新規]
    comp_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        no_occ = d['no_occ_iou']
        b8 = base_ious[i]
        
        j36_A = eval_36_A['individual'][i]['iou']
        r36_A = eval_36_A['individual'][i]['rel_reduction']
        net36_A = eval_36_A['individual'][i]['net_err_reduction']
        
        j36_B = eval_36_B['individual'][i]['iou']
        r36_B = eval_36_B['individual'][i]['rel_reduction']
        net36_B = eval_36_B['individual'][i]['net_err_reduction']
        
        j256_A = eval_256_A['individual'][i]['iou']
        j256_B = eval_256_B['individual'][i]['iou']
        
        comp_rows.append({
            'data_id': did,
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'no_extra_occ_iou': f"{no_occ:.6f}",
            'base_8_iou': f"{b8:.6f}",
            'objA_36_iou': f"{j36_A:.6f}",
            'objA_36_rel_red': f"{r36_A*100:+.2f}%",
            'objA_36_net_err': f"{net36_A*100:+.2f}%",
            'objB_36_iou': f"{j36_B:.6f}",
            'objB_36_rel_red': f"{r36_B*100:+.2f}%",
            'objB_36_net_err': f"{net36_B*100:+.2f}%",
            'diff_B_vs_A_36_iou': f"{(j36_B - j36_A)*100:+.2f}%pt",
            'diff_B_vs_A_36_rel': f"{(r36_B - r36_A)*100:+.2f}%pt",
            'objA_256_iou': f"{j256_A:.6f}",
            'objB_256_iou': f"{j256_B:.6f}",
            'diff_B_vs_A_256_iou': f"{(j256_B - j256_A)*100:+.2f}%pt"
        })
        
    # 平均行
    comp_rows.append({
        'data_id': '=== MEAN ===',
        'case': 'ALL', 'density': 'ALL', 'eye': 'ALL',
        'no_extra_occ_iou': f"{float(np.mean([d['no_occ_iou'] for d in dataset])):.6f}",
        'base_8_iou': f"{float(np.mean(base_ious)):.6f}",
        'objA_36_iou': f"{eval_36_A['mean_iou']:.6f}",
        'objA_36_rel_red': f"{eval_36_A['mean_rel_reduction']*100:+.2f}%",
        'objA_36_net_err': f"{eval_36_A['mean_net_err_reduction']*100:+.2f}%",
        'objB_36_iou': f"{eval_36_B['mean_iou']:.6f}",
        'objB_36_rel_red': f"{eval_36_B['mean_rel_reduction']*100:+.2f}%",
        'objB_36_net_err': f"{eval_36_B['mean_net_err_reduction']*100:+.2f}%",
        'diff_B_vs_A_36_iou': f"{(eval_36_B['mean_iou'] - eval_36_A['mean_iou'])*100:+.2f}%pt",
        'diff_B_vs_A_36_rel': f"{(eval_36_B['mean_rel_reduction'] - eval_36_A['mean_rel_reduction'])*100:+.2f}%pt",
        'objA_256_iou': f"{eval_256_A['mean_iou']:.6f}",
        'objB_256_iou': f"{eval_256_B['mean_iou']:.6f}",
        'diff_B_vs_A_256_iou': f"{(eval_256_B['mean_iou'] - eval_256_A['mean_iou'])*100:+.2f}%pt"
    })
    
    csv_comp = os.path.join(output_dir, "objective_comparison.csv")
    safe_write_csv(csv_comp, comp_rows, list(comp_rows[0].keys()))
    print(f"[+] 目的関数A vs B 直接比較表 (objective_comparison.csv) を保存しました: {csv_comp}")
    
    # 5.3 全手法評価一覧 (common_rule_evaluation.csv)
    eval_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        no_occ = d['no_occ_iou']
        b8 = base_ious[i]
        
        j8 = res_8_base['common_eval']['individual'][i]['iou']
        j20 = res_20_A['common_eval']['individual'][i]['iou']
        j36_A = eval_36_A['individual'][i]['iou']
        j36_B = eval_36_B['individual'][i]['iou']
        j256_A = eval_256_A['individual'][i]['iou']
        j256_B = eval_256_B['individual'][i]['iou']
        
        eval_rows.append({
            'data_id': did,
            'case': d['case'],
            'density': d['density_str'],
            'eye': d['eye'],
            'no_extra_occ_iou': f"{no_occ:.6f}",
            'common_8_iou': f"{j8:.6f}",
            'common_20_iou': f"{j20:.6f}",
            'common_36_iou_objA': f"{j36_A:.6f}",
            'common_36_iou_objB': f"{j36_B:.6f}",
            'common_256_iou_objA': f"{j256_A:.6f}",
            'common_256_iou_objB': f"{j256_B:.6f}",
            'rel_red_36_objA': f"{eval_36_A['individual'][i]['rel_reduction']*100:+.2f}%",
            'rel_red_36_objB': f"{eval_36_B['individual'][i]['rel_reduction']*100:+.2f}%",
            'net_err_36_objA': f"{eval_36_A['individual'][i]['net_err_reduction']*100:+.2f}%",
            'net_err_36_objB': f"{eval_36_B['individual'][i]['net_err_reduction']*100:+.2f}%"
        })
        
    eval_rows.append({
        'data_id': '=== MEAN ===',
        'case': 'ALL', 'density': 'ALL', 'eye': 'ALL',
        'no_extra_occ_iou': f"{float(np.mean([d['no_occ_iou'] for d in dataset])):.6f}",
        'common_8_iou': f"{res_8_base['common_eval']['mean_iou']:.6f}",
        'common_20_iou': f"{res_20_A['common_eval']['mean_iou']:.6f}",
        'common_36_iou_objA': f"{eval_36_A['mean_iou']:.6f}",
        'common_36_iou_objB': f"{eval_36_B['mean_iou']:.6f}",
        'common_256_iou_objA': f"{eval_256_A['mean_iou']:.6f}",
        'common_256_iou_objB': f"{eval_256_B['mean_iou']:.6f}",
        'rel_red_36_objA': f"{eval_36_A['mean_rel_reduction']*100:+.2f}%",
        'rel_red_36_objB': f"{eval_36_B['mean_rel_reduction']*100:+.2f}%",
        'net_err_36_objA': f"{eval_36_A['mean_net_err_reduction']*100:+.2f}%",
        'net_err_36_objB': f"{eval_36_B['mean_net_err_reduction']*100:+.2f}%"
    })
    
    csv_eval = os.path.join(output_dir, "common_rule_evaluation.csv")
    safe_write_csv(csv_eval, eval_rows, list(eval_rows[0].keys()))
    print(f"[+] 共通規則評価一覧 (common_rule_evaluation.csv) を保存しました: {csv_eval}")
    
    # 5.4 個別最適一覧 (individual_optima.csv)
    indiv_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        case = d['case']
        dens = d['density_str']
        eye = d['eye']
        M = d['total_eval_pixels']
        O_tot = int(np.sum(d['O']))
        
        # 追加遮蔽なし
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'No_Extra_Occlusion',
            'best_rule': 'All Visible (No Extra Occlusion)',
            'rule_summary': 'No occlusion check applied (Baseline)',
            'selected_details': 'All 256 patterns visible',
            'hex_mask': '0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF',
            'tp': d['G'], 'fp': O_tot, 'fn': 0,
            'iou': f"{d['no_occ_iou']:.6f}", 'iou_pct': f"{d['no_occ_iou']*100:.4f}%",
            'net_err_reduction': "+0.00%", 'rel_reduction': "+0.00%"
        })
        # 8候補
        r8 = res_8_base['individual'][i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Fixed_8_Candidates',
            'best_rule': r8['best_rule'],
            'rule_summary': r8['rule_summary'],
            'selected_details': r8['selected_details'],
            'hex_mask': r8['hex_mask'],
            'tp': r8['tp'], 'fp': r8['fp'], 'fn': r8['fn'],
            'iou': f"{r8['iou']:.6f}", 'iou_pct': f"{r8['iou']*100:.4f}%",
            'net_err_reduction': f"{r8['net_err_reduction']*100:+.2f}%",
            'rel_reduction': f"{r8['rel_reduction']*100:+.2f}%"
        })
        # 20候補
        r20 = res_20_A['individual'][i]
        indiv_rows.append({
            'data_id': did, 'case': case, 'density': dens, 'eye': eye,
            'method': 'Fixed_20_Candidates',
            'best_rule': r20['best_rule'],
            'rule_summary': r20['rule_summary'],
            'selected_details': r20['selected_details'],
            'hex_mask': r20['hex_mask'],
            'tp': r20['tp'], 'fp': r20['fp'], 'fn': r20['fn'],
            'iou': f"{r20['iou']:.6f}", 'iou_pct': f"{r20['iou']*100:.4f}%",
            'net_err_reduction': f"{r20['net_err_reduction']*100:+.2f}%",
            'rel_reduction': f"{r20['rel_reduction']*100:+.2f}%"
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
            'iou': f"{r36['iou']:.6f}", 'iou_pct': f"{r36['iou']*100:.4f}%",
            'net_err_reduction': f"{r36['net_err_reduction']*100:+.2f}%",
            'rel_reduction': f"{r36['rel_reduction']*100:+.2f}%"
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
            'iou': f"{r256['iou']:.6f}", 'iou_pct': f"{r256['iou']*100:.4f}%",
            'net_err_reduction': f"{r256['net_err_reduction']*100:+.2f}%",
            'rel_reduction': f"{r256['rel_reduction']*100:+.2f}%"
        })
        
    csv_indiv = os.path.join(output_dir, "individual_optima.csv")
    safe_write_csv(csv_indiv, indiv_rows, list(indiv_rows[0].keys()))
    print(f"[+] 個別最適一覧 (individual_optima.csv) を保存しました: {csv_indiv}")
    
    # 5.5 個別最適からの低下 (individual_gap_analysis.csv)
    gap_rows = []
    for i, d in enumerate(dataset):
        did = d['data_id']
        opt8 = res_8_base['individual'][i]['iou']
        opt20 = res_20_A['individual'][i]['iou']
        opt36 = indiv_36[i]['iou']
        opt256 = indiv_256[i]['iou']
        
        com8 = res_8_base['common_eval']['individual'][i]['iou']
        com20 = res_20_A['common_eval']['individual'][i]['iou']
        com36_A = eval_36_A['individual'][i]['iou']
        com36_B = eval_36_B['individual'][i]['iou']
        com256_A = eval_256_A['individual'][i]['iou']
        com256_B = eval_256_B['individual'][i]['iou']
        
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
            'common_36_iou_objA': f"{com36_A:.6f}",
            'gap_36_objA': f"{(opt36 - com36_A)*100:.4f}%pt",
            'common_36_iou_objB': f"{com36_B:.6f}",
            'gap_36_objB': f"{(opt36 - com36_B)*100:.4f}%pt",
            'opt_256_iou': f"{opt256:.6f}",
            'common_256_iou_objA': f"{com256_A:.6f}",
            'gap_256_objA': f"{(opt256 - com256_A)*100:.4f}%pt",
            'common_256_iou_objB': f"{com256_B:.6f}",
            'gap_256_objB': f"{(opt256 - com256_B)*100:.4f}%pt"
        })
        
    csv_gap = os.path.join(output_dir, "individual_gap_analysis.csv")
    safe_write_csv(csv_gap, gap_rows, list(gap_rows[0].keys()))
    print(f"[+] 個別最適からの低下分析 (individual_gap_analysis.csv) を保存しました: {csv_gap}")
    
    # 5.6 共通規則・共通LUTファイル (ObjA & ObjB)
    for obj_name, z36, z256 in [('objA', z_36_A, z_256_A), ('objB', z_36_B, z_256_B)]:
        # common_lut_36
        csv_lut_36 = os.path.join(output_dir, f"common_lut_36_{obj_name}.csv")
        tot_obs_36 = np.sum(counts_V_36 + counts_O_36, axis=0)
        lut_36_rows = []
        for cid in range(36):
            rep = lut['unique_reps'][cid]
            tot_obs = int(tot_obs_36[cid])
            dec = z36[cid]
            lut_36_rows.append([cid, rep, bin(rep)[2:].zfill(8), lut['n_occ'][rep], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs])
        safe_write_csv_raw(csv_lut_36, ["rotation_class_id", "rotation_representative", "representative_bits", "occupied_count", "decision", "label", "total_observed_pixels"], lut_36_rows)
            
        # common_lut_256
        csv_lut_256 = os.path.join(output_dir, f"common_lut_256_{obj_name}.csv")
        tot_obs_256 = np.sum(counts_V_256 + counts_O_256, axis=0)
        lut_256_rows = []
        for m in range(256):
            tot_obs = int(tot_obs_256[m])
            dec = z256[m]
            lut_256_rows.append([m, bin(m)[2:].zfill(8), lut['n_occ'][m], lut['rot_cid'][m], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs])
        safe_write_csv_raw(csv_lut_256, ["mask", "bits_b7_to_b0", "occupied_count", "rotation_class_id", "decision", "label", "total_observed_pixels"], lut_256_rows)
            
    print(f"[+] 共通規則・共通LUTファイル (ObjA/ObjB) を保存しました。")
    
    # ==========================================================================
    # 9. 自動検証 & サマリー表示
    # ==========================================================================
    print("\n" + "=" * 80)
    print("【方式間の包含関係 & 数学的整合性チェック】")
    m8_A = res_8_base['common_eval']['mean_iou']
    m20_A = res_20_A['common_eval']['mean_iou']
    m36_A = eval_36_A['mean_iou']
    m256_A = eval_256_A['mean_iou']
    print(f"  [目的関数 A: 平均IoU最大化] 不等式: J*_8 <= J*_20 <= J*_36 <= J*_256")
    print(f"    {m8_A*100:.4f}% <= {m20_A*100:.4f}% <= {m36_A*100:.4f}% <= {m256_A*100:.4f}%")
    ineq_A = (m8_A <= m20_A + 1e-6) and (m20_A <= m36_A + 1e-6) and (m36_A <= m256_A + 1e-6)
    print(f"    -> 整合性判定: {'完全成立 (PASS!!)' if ineq_A else '不成立 (FAIL)'}")
    
    r8_B = res_8_B['common_eval']['mean_rel_reduction']
    r20_B = res_20_B['common_eval']['mean_rel_reduction']
    r36_B = eval_36_B['mean_rel_reduction']
    r256_B = eval_256_B['mean_rel_reduction']
    print(f"  [目的関数 B: 相対低減率最大化] 不等式: r*_8 <= r*_20 <= r*_36 <= r*_256")
    print(f"    {r8_B*100:+.2f}% <= {r20_B*100:+.2f}% <= {r36_B*100:+.2f}% <= {r256_B*100:+.2f}%")
    ineq_B = (r8_B <= r20_B + 1e-6) and (r20_B <= r36_B + 1e-6) and (r36_B <= r256_B + 1e-6)
    print(f"    -> 整合性判定: {'完全成立 (PASS!!)' if ineq_B else '不成立 (FAIL)'}")
    print("=" * 80)

if __name__ == "__main__":
    search_root = None
    if len(sys.argv) > 1:
        search_root = sys.argv[1]
        
    out_dir = None
    if len(sys.argv) > 2:
        out_dir = sys.argv[2]
        
    run_pattern_rules_optimization(search_root, out_dir)
