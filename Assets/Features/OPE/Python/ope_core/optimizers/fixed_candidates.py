"""
SICESI Python Suite: 固定8候補 (最低占有数判定) & 固定20候補 (連続非占有規則) 最適化モジュール
"""

from typing import List, Dict, Any, Optional
import numpy as np

from ..pattern_lut import RULES_20_CANDIDATES, z_to_hex, occ_breakdown_str
from ..metrics import evaluate_lut_extended


def optimize_fixed_8(dataset: List[Dict[str, Any]], lut: Dict[str, Any], base_ious: Optional[np.ndarray] = None, weights: Optional[np.ndarray] = None) -> Dict[str, Any]:
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
            tp = int(np.dot(d['V'], z))
            fp = int(np.dot(d['O'], z))
            fn = d['G'] - tp
            denom = d['G'] + fp
            iou = tp / denom if denom > 0 else 0.0
            
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
    best_common_r = 1
    best_common_obj = -1.0
    best_common_eval = None
    best_common_z = None
    
    for R, z in cands_z:
        ev = evaluate_lut_extended(z, dataset, base_ious)
        obj_val = float(np.sum([ev['individual'][i]['iou'] * norm_w[i] for i in range(D)]))
        if obj_val > best_common_obj:
            best_common_obj = obj_val
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


def optimize_fixed_20(dataset: List[Dict[str, Any]], lut: Dict[str, Any], base_ious: Optional[np.ndarray] = None, weights: Optional[np.ndarray] = None) -> Dict[str, Any]:
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
            tp = int(np.dot(d['V'], z))
            fp = int(np.dot(d['O'], z))
            fn = d['G'] - tp
            denom = d['G'] + fp
            iou = tp / denom if denom > 0 else 0.0
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
    best_common_obj = -1.0
    best_common_eval = None
    best_common_z = None
    best_common_params = (1, 8)
    
    for R_th, L_th, z in cands_z:
        ev = evaluate_lut_extended(z, dataset, base_ious)
        obj_val = float(np.sum([ev['individual'][i]['iou'] * norm_w[i] for i in range(D)]))
        if obj_val > best_common_obj:
            best_common_obj = obj_val
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
