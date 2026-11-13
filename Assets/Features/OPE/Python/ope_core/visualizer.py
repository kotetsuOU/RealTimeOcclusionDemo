"""
SICESI Python Suite: 差分可視化・オーバーレイ・モンタージュ生成モジュール
"""

import os
import csv
from typing import Dict, Any, List, Tuple, Optional
import numpy as np
import cv2
from PIL import Image

from .constants import (
    COLOR_CORRECT, COLOR_OCC_MATCH, COLOR_OVER_OCC, COLOR_UNDER_OCC, COLOR_BG
)
from .pattern_lut import hex_to_lut


class OcclusionDiffVisualizer:
    """GT (正解) と Test (実測) 画像を比較し、誤差領域を色分け可視化するクラス"""
    def __init__(self, gt_rgb: np.ndarray, test_rgb: np.ndarray, threshold: int = 128):
        if gt_rgb.shape != test_rgb.shape:
            raise ValueError(f"画像サイズ不一致: GT {gt_rgb.shape} vs Test {test_rgb.shape}")
        self.gt_rgb = gt_rgb
        self.test_rgb = test_rgb
        self.h, self.w = gt_rgb.shape[:2]
        self.threshold = threshold

        # マスク抽出 (50%カバレッジ基準 threshold > 128 で判定)
        self.gt_mask = (self.gt_rgb[:, :, 0] > threshold)
        self.test_mask = (self.test_rgb[:, :, 0] > threshold)

        # 4分割の論理判定
        self.tp_mask = self.gt_mask & self.test_mask       # 正しく表示
        self.fn_mask = self.gt_mask & (~self.test_mask)    # 誤遮蔽 (Over-occlusion: GT=可視, Test=遮蔽)
        self.fp_mask = (~self.gt_mask) & self.test_mask    # 誤透過 (Under-occlusion: GT=遮蔽, Test=可視)
        self.tn_mask = (~self.gt_mask) & (~self.test_mask)  # 正しく遮蔽 / 背景

    def get_statistics(self) -> Dict[str, Any]:
        """各種ピクセル数およびIoU・誤り率の統計を計算"""
        union_px = int(np.sum(self.gt_mask | self.test_mask))
        tp_px = int(np.sum(self.tp_mask))
        fn_px = int(np.sum(self.fn_mask))
        fp_px = int(np.sum(self.fp_mask))

        iou = (tp_px / union_px) if union_px > 0 else 1.0
        over_occ_rate = (fn_px / union_px) if union_px > 0 else 0.0
        under_occ_rate = (fp_px / union_px) if union_px > 0 else 0.0

        return {
            "IoU": iou,
            "TP_pixels": tp_px,
            "FN_pixels": fn_px,
            "FP_pixels": fp_px,
            "Union_pixels": union_px,
            "OverOccRate": over_occ_rate,
            "UnderOccRate": under_occ_rate,
        }

    def generate_diff_mask(self) -> np.ndarray:
        """単色マスクによる差分マップを生成 (RGB)"""
        diff_img = np.full((self.h, self.w, 3), COLOR_BG, dtype=np.uint8)
        diff_img[self.tp_mask] = COLOR_CORRECT
        diff_img[self.fn_mask] = COLOR_OVER_OCC
        diff_img[self.fp_mask] = COLOR_UNDER_OCC
        return diff_img

    def generate_overlay(self, alpha: float = 0.65) -> np.ndarray:
        """元画像の上にエラー領域を半透明ハイライトしたオーバーレイ画像を生成 (RGB)"""
        overlay = self.test_rgb.copy()

        # 誤遮蔽領域（本来見えるはずだった部分）: GTのテクスチャに赤をブレンド
        if np.any(self.fn_mask):
            gt_area = self.gt_rgb[self.fn_mask].astype(np.float32)
            red_tint = np.full_like(gt_area, COLOR_OVER_OCC, dtype=np.float32)
            blended_fn = (gt_area * (1.0 - alpha) + red_tint * alpha).clip(0, 255).astype(np.uint8)
            overlay[self.fn_mask] = blended_fn

        # 誤透過領域（透けて見えてしまった部分）: Testのテクスチャに青をブレンド
        if np.any(self.fp_mask):
            test_area = self.test_rgb[self.fp_mask].astype(np.float32)
            blue_tint = np.full_like(test_area, COLOR_UNDER_OCC, dtype=np.float32)
            blended_fp = (test_area * (1.0 - alpha) + blue_tint * alpha).clip(0, 255).astype(np.uint8)
            overlay[self.fp_mask] = blended_fp

        return overlay

    def generate_montage(self, title: str = "") -> np.ndarray:
        """[GT | Test | 差分マスク | オーバーレイ] の4ペイン横連結モンタージュを生成 (RGB)"""
        diff_mask = self.generate_diff_mask()
        overlay = self.generate_overlay()
        stats = self.get_statistics()

        panels = [self.gt_rgb, self.test_rgb, diff_mask, overlay]
        labels = ["GT (Reference)", "Test (Result)", "Error Mask", "Overlay"]

        panel_w = self.w
        panel_h = self.h
        header_h = 45
        footer_h = 55
        total_w = panel_w * 4
        total_h = panel_h + header_h + footer_h

        canvas = np.zeros((total_h, total_w, 3), dtype=np.uint8)

        for i, (p, label) in enumerate(zip(panels, labels)):
            x_start = i * panel_w
            x_end = x_start + panel_w
            canvas[header_h:header_h + panel_h, x_start:x_end] = p

            cv2.putText(canvas, label, (x_start + 15, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (230, 230, 230), 2, cv2.LINE_AA)
            if i > 0:
                cv2.line(canvas, (x_start, 0), (x_start, total_h), (60, 60, 60), 1)

        if title:
            cv2.putText(canvas, f"[{title}]", (total_w - 600, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.75, (100, 220, 255), 2, cv2.LINE_AA)

        footer_y = header_h + panel_h + 38
        info_texts = [
            f"IoU: {stats['IoU']*100:.2f}%",
            f"Over-Occ (Red/FN): {stats['FN_pixels']:,} px ({stats['OverOccRate']*100:.2f}%)",
            f"Under-Occ (Blue/FP): {stats['FP_pixels']:,} px ({stats['UnderOccRate']*100:.2f}%)",
            f"Union: {stats['Union_pixels']:,} px"
        ]
        info_x = 20
        for text in info_texts:
            cv2.putText(canvas, text, (info_x, footer_y),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (240, 240, 240), 2, cv2.LINE_AA)
            info_x += 420

        return canvas


def export_all_diff_maps(dataset: List[Dict[str, Any]], indiv_rows: List[Dict[str, Any]], common_luts: Dict[str, Any], output_dir: str):
    """
    最適化された各手法（個別最適・共通最適）について、元画像が存在するデータセットの
    GT（手メッシュ真値）との厳密な差分マップ (diff_map) および比較モンタージュ画像を生成・保存する。
    """
    print("\n" + "=" * 80)
    print("【最適化結果 diff_map の自動エクスポート (GTとの厳密比較)】")
    print("=" * 80)

    indiv_dict = {}
    for r in indiv_rows:
        did = r['data_id']
        m = r['method']
        if did not in indiv_dict:
            indiv_dict[did] = {}
        indiv_dict[did][m] = r

    method_keys = [
        ('No_Extra_Occlusion', 'Baseline (No Extra Occ)'),
        ('Fixed_8_Candidates', 'Fixed 8 (N_occ < R)'),
        ('Fixed_20_Candidates', 'Fixed 20 (R_th, L_th)'),
        ('Oracle_36_RotationLUT', 'Oracle 36 (Rotation LUT)'),
        ('Oracle_256_ExactLUT', 'Oracle 256 (Exact LUT)')
    ]

    exported_count = 0
    diff_base_dir = os.path.join(output_dir, "diff_maps")
    metrics_summary_rows = []

    for item in dataset:
        fpath = item['file_path']
        eye_dir = os.path.dirname(fpath)
        if os.path.basename(eye_dir) == "Oracle256Analysis":
            eye_dir = os.path.dirname(eye_dir)

        sweep_dir = os.path.dirname(os.path.dirname(eye_dir))
        gt_dir = os.path.join(sweep_dir, "GT", item['eye'])

        gt_path = os.path.join(gt_dir, f"gt_{item['eye'].lower()}.png")
        vo_path = os.path.join(gt_dir, f"vo_silhouette_{item['eye'].lower()}.png")
        sec_path = os.path.join(eye_dir, f"sector_mask_{item['eye'].lower()}.png")

        if not (os.path.exists(gt_path) and os.path.exists(vo_path)):
            continue

        try:
            gt_rgb = np.array(Image.open(gt_path).convert("RGB"))
            vo_mask = np.array(Image.open(vo_path))[:, :, 0] > 128
        except Exception as e:
            print(f"[!] 画像読み込み失敗: {item['data_id']} ({e})")
            continue

        h, w = gt_rgb.shape[:2]
        sec_mask = np.zeros((h, w), dtype=np.uint8)
        has_sec_channels = True
        for s in range(8):
            s_path = os.path.join(eye_dir, f"sector_{s}_mask_{item['eye'].lower()}.png")
            if os.path.exists(s_path):
                s_img = np.array(Image.open(s_path))
                if s_img.ndim == 3:
                    s_img = s_img[:, :, 0]
                sec_mask |= ((s_img > 128).astype(np.uint8) << s)
            else:
                has_sec_channels = False
                break

        if not has_sec_channels:
            if os.path.exists(sec_path):
                s_img = np.array(Image.open(sec_path))
                if s_img.ndim == 3:
                    s_img = s_img[:, :, 0]
                sec_mask = s_img.astype(np.uint8)
            else:
                continue

        target_mask = vo_mask
        gt_vis = (gt_rgb[:, :, 0] > 128) & target_mask

        case_clean = item['case'].replace("RawTest_", "").capitalize()
        sub_dir = os.path.join(diff_base_dir, case_clean, f"density_{item['density_str']}_{item['eye']}")
        os.makedirs(sub_dir, exist_ok=True)
        case_dir = os.path.join(diff_base_dir, case_clean)

        for eval_type in ['Individual', 'Common']:
            montage_cols = []
            gt_bgr = cv2.cvtColor(gt_rgb, cv2.COLOR_RGB2BGR)
            gt_card = gt_bgr.copy()
            cv2.putText(gt_card, f"GT ({item['eye']})", (20, 35), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (255, 255, 255), 2)
            cv2.putText(gt_card, f"Density: {item['density_str']}", (20, 65), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 2)
            cv2.putText(gt_card, f"Mode: {eval_type} Opt", (20, 95), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 255, 255), 2)
            montage_cols.append(gt_card)

            for m_key, m_label in method_keys:
                if eval_type == 'Individual':
                    r_info = indiv_dict[item['data_id']][m_key]
                    lut = hex_to_lut(r_info['hex_mask'])
                    rule_text = r_info['best_rule']
                else:
                    label, rule_text, lut = common_luts[m_key]

                pred_vis = lut[sec_mask] & target_mask

                tp = gt_vis & pred_vis
                tn = (~gt_vis) & (~pred_vis) & target_mask
                fn = gt_vis & (~pred_vis) & target_mask
                fp = (~gt_vis) & pred_vis & target_mask

                diff_img = np.full((h, w, 3), COLOR_BG, dtype=np.uint8)
                diff_img[tp] = COLOR_CORRECT
                diff_img[tn] = COLOR_OCC_MATCH
                diff_img[fn] = COLOR_OVER_OCC
                diff_img[fp] = COLOR_UNDER_OCC

                union_px = int(np.sum(gt_vis | pred_vis))
                tp_px = int(np.sum(tp))
                fn_px = int(np.sum(fn))
                fp_px = int(np.sum(fp))
                iou = (tp_px / union_px) if union_px > 0 else 1.0

                diff_bgr = cv2.cvtColor(diff_img, cv2.COLOR_RGB2BGR)
                cv2.imwrite(os.path.join(sub_dir, f"diff_{eval_type}_{m_key}.png"), diff_bgr)

                card = diff_bgr.copy()
                cv2.putText(card, f"[{eval_type}] {m_label}", (15, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (255, 255, 255), 2)
                cv2.putText(card, f"IoU: {iou*100:.2f}%", (15, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.70, (0, 255, 255), 2)
                cv2.putText(card, f"Over(Red): {fn_px:,}", (15, 85), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (50, 50, 255), 1)
                cv2.putText(card, f"Under(Blue): {fp_px:,}", (15, 105), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (255, 180, 50), 1)
                cv2.putText(card, f"Rule: {rule_text}", (15, 125), cv2.FONT_HERSHEY_SIMPLEX, 0.40, (200, 200, 200), 1)
                montage_cols.append(card)

                metrics_summary_rows.append({
                    'data_id': item['data_id'],
                    'case': item['case'],
                    'density': item['density_str'],
                    'eye': item['eye'],
                    'type': f"{eval_type}_Optimal",
                    'method': m_key,
                    'method_label': m_label,
                    'rule': rule_text,
                    'iou': iou,
                    'iou_pct': f"{iou*100:.4f}%",
                    'tp': tp_px,
                    'fn_over_occ': fn_px,
                    'fp_under_occ': fp_px,
                    'union': union_px
                })

            montage_img = np.hstack(montage_cols)
            cv2.imwrite(os.path.join(case_dir, f"comparison_{eval_type}_{item['density_str']}_{item['eye']}.png"), montage_img)

        exported_count += 1

    if exported_count > 0:
        summary_csv_path = os.path.join(diff_base_dir, "diff_metrics_summary.csv")
        if metrics_summary_rows:
            with open(summary_csv_path, 'w', newline='', encoding='utf-8-sig') as f:
                writer = csv.DictWriter(f, fieldnames=list(metrics_summary_rows[0].keys()))
                writer.writeheader()
                writer.writerows(metrics_summary_rows)
        print(f"[+] {exported_count} 条件の diff_map およびモンタージュ画像を保存しました: {diff_base_dir}")
        print(f"[+] diff_map メトリクス集計CSVを保存しました: {summary_csv_path}")
