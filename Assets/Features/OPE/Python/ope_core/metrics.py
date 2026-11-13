"""
SICESI Python Suite: 評価指標計算モジュール (IoU, MAE, PSNR, SSIM, FN/FP分析, LUT評価)
"""

from typing import Dict, Any, List, Optional
import numpy as np
import cv2
from skimage.metrics import structural_similarity as ssim


class UnionMaskMetrics:
    """論理和 (Union Mask) および GT 可視領域に基づく評価指標を計算"""
    def __init__(self, ref_img: np.ndarray, test_img: np.ndarray, data_range: float = 255.0, threshold: int = 128):
        self.ref_img = ref_img
        self.test_img = test_img
        self.data_range = float(data_range)
        self.threshold = threshold
        self._validate_images()
        
        # マスク抽出 (50%カバレッジ基準 threshold > 128 で判定)
        if threshold > 0:
            self.ref_mask = np.any(self.ref_img > threshold, axis=-1)
            self.test_mask = np.any(self.test_img > threshold, axis=-1)
        else:
            self.ref_mask = np.any(self.ref_img > 0, axis=-1)
            self.test_mask = np.any(self.test_img > 0, axis=-1)
        self.union_mask = self.ref_mask | self.test_mask

    def _validate_images(self) -> None:
        if self.ref_img.shape != self.test_img.shape:
            raise ValueError(f"画像サイズ不一致: 基準 {self.ref_img.shape} vs 対象 {self.test_img.shape}")

    def calculate_metrics(self) -> Dict[str, Any]:
        valid_pixels = np.sum(self.union_mask)
        if valid_pixels == 0:
            return {
                "IoU": 0.0, "OverOcc_wrt_GT_Visible": 0.0,
                "OverOcc_wrt_Union": 0.0, "UnderOcc_wrt_Union": 0.0,
                "FN_pixels": 0, "FP_pixels": 0, "TP_pixels": 0,
                "GT_Visible_pixels": 0, "Union_pixels": 0,
                "MAE": 0.0, "PSNR": 0.0, "DSSIM": 0.0
            }

        # [1] IoU & 誤遮蔽・誤透過の分析
        intersection = self.ref_mask & self.test_mask  # TP: 正しく可視
        fn_mask = self.ref_mask & (~self.test_mask)    # FN: 誤遮蔽 (Over-occlusion: 本来見えるはずが遮蔽)
        fp_mask = (~self.ref_mask) & self.test_mask    # FP: 誤透過 (Under-occlusion: 本来隠れるはずが透過)

        tp_pixels = int(np.sum(intersection))
        fn_pixels = int(np.sum(fn_mask))
        fp_pixels = int(np.sum(fp_mask))
        union_pixels = int(valid_pixels)
        gt_visible_pixels = tp_pixels + fn_pixels

        iou = float(tp_pixels / union_pixels) if union_pixels > 0 else 0.0

        # 和集合(Union)を分母とする誤遮蔽率・誤透過率
        over_occ_wrt_union = float(fn_pixels / union_pixels) if union_pixels > 0 else 0.0
        under_occ_wrt_union = float(fp_pixels / union_pixels) if union_pixels > 0 else 0.0

        # GT可視領域(TP + FN)を分母とする過剰遮蔽率
        over_occ_wrt_gt_visible = float(fn_pixels / gt_visible_pixels) if gt_visible_pixels > 0 else 0.0

        # [2] Masked MAE & PSNR
        ref_f = self.ref_img.astype(np.float64)
        test_f = self.test_img.astype(np.float64)
        diff = np.abs(ref_f - test_f)[self.union_mask]
        mae = float(np.mean(diff))

        mse = float(np.mean(diff ** 2))
        psnr = float('inf') if mse == 0 else float(10 * np.log10((self.data_range ** 2) / mse))

        # [3] Masked DSSIM
        _, ssim_map = ssim(self.ref_img, self.test_img, channel_axis=-1, full=True, data_range=self.data_range)
        masked_ssim = float(np.mean(ssim_map[self.union_mask]))
        dssim = (1.0 - masked_ssim) / 2.0

        return {
            "IoU": iou,
            "OverOcc_wrt_GT_Visible": over_occ_wrt_gt_visible,
            "OverOcc_wrt_Union": over_occ_wrt_union,
            "UnderOcc_wrt_Union": under_occ_wrt_union,
            "FN_pixels": fn_pixels,
            "FP_pixels": fp_pixels,
            "TP_pixels": tp_pixels,
            "GT_Visible_pixels": gt_visible_pixels,
            "Union_pixels": union_pixels,
            "MAE": mae,
            "PSNR": psnr,
            "DSSIM": dssim
        }


def evaluate_lut_extended(z: np.ndarray, dataset: List[Dict[str, Any]], base_ious: Optional[np.ndarray] = None) -> Dict[str, Any]:
    """
    256項目の可視判定 z in {0, 1}^256 を全データセットに適用し、
    各データの TP, FP, FN, IoU、候補領域内の誤画素率 e、正味誤画素削減率 Δe、
    およびベースラインに対する 1-IoU 相対低減率 r を計算。
    """
    results = []
    total_tp = 0
    total_fp = 0
    total_fn = 0
    total_M = 0
    
    for i, d in enumerate(dataset):
        tp = int(np.dot(d['V'], z))
        fp = int(np.dot(d['O'], z))
        fn = d['G'] - tp
        denom = d['G'] + fp
        iou = tp / denom if denom > 0 else 0.0
        
        M = d['total_eval_pixels']
        O_total = int(np.sum(d['O']))
        err_rate = (fn + fp) / M if M > 0 else 0.0
        net_err_reduction = (O_total - fp - fn) / M if M > 0 else 0.0
        
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
