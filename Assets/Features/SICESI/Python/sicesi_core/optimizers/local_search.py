"""
SICESI Python Suite: 最急上昇局所探索 (Steepest Ascent Local Search) モジュール
"""

from typing import Tuple
import numpy as np


def local_search_36_iou(
    init_z36: np.ndarray,
    counts_V_36: np.ndarray,
    counts_O_36: np.ndarray,
    G_arr: np.ndarray
) -> Tuple[np.ndarray, float, int]:
    """
    36クラスLUTの割当てを初期値から1クラスずつ反転し、
    全条件の平均IoUが向上する限り最急上昇法 (Steepest Ascent) で更新を繰り返す。
    """
    cur_z = np.array(init_z36, dtype=np.float64).copy()
    
    def calc_mean_iou(z):
        tp = counts_V_36 @ z
        fp = counts_O_36 @ z
        denom = G_arr + fp
        ious = np.where(denom > 0, tp / denom, 0.0)
        return float(np.mean(ious))

    cur_mean = calc_mean_iou(cur_z)
    iter_count = 0
    while True:
        best_cand = None
        best_cand_mean = cur_mean
        for c in range(36):
            flipped_z = cur_z.copy()
            flipped_z[c] = 1.0 - flipped_z[c]
            m_iou = calc_mean_iou(flipped_z)
            if m_iou > best_cand_mean + 1e-12:
                best_cand_mean = m_iou
                best_cand = flipped_z
        if best_cand is not None:
            cur_z = best_cand
            cur_mean = best_cand_mean
            iter_count += 1
        else:
            break
            
    return cur_z.astype(np.int32), cur_mean, iter_count


def local_search_256_iou(
    init_z256: np.ndarray,
    counts_V_256: np.ndarray,
    counts_O_256: np.ndarray,
    G_arr: np.ndarray
) -> Tuple[np.ndarray, float, int]:
    """
    256パターンLUTの割当てを初期値から1パターンずつ反転し、
    全条件の平均IoUが向上する限り最急上昇法 (Steepest Ascent) で更新を繰り返す。
    """
    cur_z = np.array(init_z256, dtype=np.float64).copy()
    
    def calc_mean_iou(z):
        tp = counts_V_256 @ z
        fp = counts_O_256 @ z
        denom = G_arr + fp
        ious = np.where(denom > 0, tp / denom, 0.0)
        return float(np.mean(ious))

    cur_mean = calc_mean_iou(cur_z)
    iter_count = 0
    while True:
        best_cand = None
        best_cand_mean = cur_mean
        for m in range(256):
            flipped_z = cur_z.copy()
            flipped_z[m] = 1.0 - flipped_z[m]
            m_iou = calc_mean_iou(flipped_z)
            if m_iou > best_cand_mean + 1e-12:
                best_cand_mean = m_iou
                best_cand = flipped_z
        if best_cand is not None:
            cur_z = best_cand
            cur_mean = best_cand_mean
            iter_count += 1
        else:
            break
            
    return cur_z.astype(np.int32), cur_mean, iter_count
