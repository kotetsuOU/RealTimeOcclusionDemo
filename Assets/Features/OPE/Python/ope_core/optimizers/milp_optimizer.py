"""
SICESI Python Suite: MILP (混合整数計画法) 最適化 & 単一ケース並べ替え探索モジュール
"""

import time
from typing import List, Dict, Any, Tuple, Optional
import numpy as np
from scipy.optimize import milp, LinearConstraint, Bounds

from ..pattern_lut import z_to_hex, compress_indices, occ_breakdown_str, class_breakdown_str


def optimize_single_iou_lut(V_classes: np.ndarray, O_classes: np.ndarray, G: int) -> Tuple[float, np.ndarray]:
    """
    単一データに対する並べ替え探索 (O(C log C))
    可視確率 V/(V+O) の降順にソートし、IoU が最大となるカットオフを線形走査
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


def get_individual_optima_36_and_256(dataset: List[Dict[str, Any]], lut: Dict[str, Any], base_ious: Optional[np.ndarray] = None) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
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
        tp_36 = int(np.dot(d['V'], z_256_from_36))
        fp_36 = int(np.dot(d['O'], z_256_from_36))
        fn_36 = d['G'] - tp_36
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
        tp_256 = int(np.dot(d['V'], z_256))
        fp_256 = int(np.dot(d['O'], z_256))
        fn_256 = d['G'] - tp_256
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


def optimize_weighted_iou_milp(
    counts_V: np.ndarray,
    counts_O: np.ndarray,
    G_list: np.ndarray,
    fallback_z_classes: np.ndarray,
    weights: Optional[np.ndarray] = None,
    time_limit_sec: float = 60.0
) -> Tuple[np.ndarray, float, str, float, float]:
    """
    McCormick 緩和 MILP ソルバー (等重み平均IoU または 重み付き平均IoU/相対低減率を直接最大化)
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
