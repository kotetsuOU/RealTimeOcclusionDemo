r"""
SICE SI 2026: オクルージョン定性評価用 差分可視化スクリプト (Error / Difference Map Generator)

【目的】
GT（正解画像）と各手法のレンダリング結果（Test画像）を比較し、
画素単位で以下の状態を明確に色分けした差分マップを自動生成・出力します。

  1. 正しく表示（True Positive）  : 白/薄グレー または 原色
  2. 正しく遮蔽（True Negative）  : 背景（黒）
  3. 誤って遮蔽（False Negative）: 赤色 (Red: [255, 40, 40])   - 過剰遮蔽 (Over-occlusion)
  4. 誤って透過（False Positive）: 青色 (Blue: [30, 144, 255]) - 遮蔽漏れ (Under-occlusion)

【出力バリエーション】
  - diff_mask    : 単色マスクによる差分マップ
  - diff_overlay : 元画像の上にエラー領域を半透明オーバーレイしたマップ
  - montage      : [GT | Test | 差分マスク | オーバーレイ] を横連結した比較図（メトリクス情報付き）

【実行方法】
  # 1) 特定の条件（例: Bouchiba）の差分マップを一括生成
  python C:\Users\hongo\Documents\tsutsumi\RealTimeOcclusion\Assets\Features\SICESI\Python\export_occlusion_diff_maps.py -c Bouchiba

  # 2) 特定の密度（例: density_1.00）のみ生成
  python C:\Users\hongo\Documents\tsutsumi\RealTimeOcclusion\Assets\Features\SICESI\Python\export_occlusion_diff_maps.py -c density_1.00

  # 3) モンタージュ画像のみ生成（論文・スライド用）
  python C:\Users\hongo\Documents\tsutsumi\RealTimeOcclusion\Assets\Features\SICESI\Python\export_occlusion_diff_maps.py -c Bouchiba --mode montage

  # 4) 元のフォルダ構造内 (Left/Right直下) に差分画像を直接保存（データ管理用）
  python C:\Users\hongo\Documents\tsutsumi\RealTimeOcclusion\Assets\Features\SICESI\Python\export_occlusion_diff_maps.py -c Bouchiba --in-place

  # 5) 別フォルダへ出力先ディレクトリを指定
  python C:\Users\hongo\Documents\tsutsumi\RealTimeOcclusion\Assets\Features\SICESI\Python\export_occlusion_diff_maps.py -c Bouchiba -o "C:/Users/hongo/Documents/tsutsumi/Estimation/SICESI_Dataset/DiffMaps"
"""

import os
import sys
import glob
import re
import argparse
from typing import Optional, Tuple, List, Dict

# Windowsコンソールでの文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import cv2
import numpy as np

# 配色定義 (RGB)
COLOR_CORRECT   = np.array([200, 200, 200], dtype=np.uint8)  # 正しく表示（薄いグレー）
COLOR_OVER_OCC  = np.array([255, 45, 45], dtype=np.uint8)    # 誤って遮蔽（赤: 過剰遮蔽 / 本来見えるはずが消えた）
COLOR_UNDER_OCC = np.array([30, 144, 255], dtype=np.uint8)   # 誤って透過（青: 遮蔽漏れ / 本来隠れるはずが透けた）
COLOR_BG        = np.array([15, 15, 15], dtype=np.uint8)      # 背景（黒）

class OcclusionDiffVisualizer:
    def __init__(self, gt_rgb: np.ndarray, test_rgb: np.ndarray):
        if gt_rgb.shape != test_rgb.shape:
            raise ValueError(f"画像サイズ不一致: GT {gt_rgb.shape} vs Test {test_rgb.shape}")
        self.gt_rgb = gt_rgb
        self.test_rgb = test_rgb
        self.h, self.w = gt_rgb.shape[:2]

        # マスク抽出 (> 0 の画素を可視オブジェクト領域と判定)
        self.gt_mask = np.any(self.gt_rgb > 0, axis=-1)
        self.test_mask = np.any(self.test_rgb > 0, axis=-1)

        # 4分割の論理判定
        self.tp_mask = self.gt_mask & self.test_mask       # 正しく表示
        self.fn_mask = self.gt_mask & (~self.test_mask)    # 誤遮蔽 (Over-occlusion: GT=可視, Test=遮蔽)
        self.fp_mask = (~self.gt_mask) & self.test_mask    # 誤透過 (Under-occlusion: GT=遮蔽, Test=可視)
        self.tn_mask = (~self.gt_mask) & (~self.test_mask)  # 正しく遮蔽 / 背景

    def get_statistics(self) -> Dict[str, float]:
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
            "FN_pixels": fn_px,  # 誤遮蔽
            "FP_pixels": fp_px,  # 誤透過
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

        # 誤遮蔽領域（本来見えるはずだった部分）: GTのテクスチャに赤をブレンドして表示
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

        # 4画像を横連結
        panels = [self.gt_rgb, self.test_rgb, diff_mask, overlay]
        labels = ["GT (Reference)", "Test (Result)", "Error Mask", "Overlay"]

        panel_w = self.w
        panel_h = self.h

        # ヘッダーとフッターの余白
        header_h = 45
        footer_h = 55
        total_w = panel_w * 4
        total_h = panel_h + header_h + footer_h

        canvas = np.zeros((total_h, total_w, 3), dtype=np.uint8)

        # 画像の配置
        for i, (p, label) in enumerate(zip(panels, labels)):
            x_start = i * panel_w
            x_end = x_start + panel_w
            canvas[header_h:header_h + panel_h, x_start:x_end] = p

            # パネル上部ラベル
            cv2.putText(canvas, label, (x_start + 15, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (230, 230, 230), 2, cv2.LINE_AA)
            # 境界線
            if i > 0:
                cv2.line(canvas, (x_start, 0), (x_start, total_h), (60, 60, 60), 1)

        # ヘッダータイトル（指定がある場合）
        if title:
            cv2.putText(canvas, f"[{title}]", (total_w - 600, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (180, 180, 180), 2, cv2.LINE_AA)

        # フッター情報（凡例とメトリクス）
        footer_y = header_h + panel_h + 35

        # 凡例チップ (赤: 誤遮蔽, 青: 誤透過, 白: 正解一致)
        chip_y = header_h + panel_h + 18
        chip_h = 20
        chip_w = 20

        # 赤チップ: 誤遮蔽
        cv2.rectangle(canvas, (30, chip_y), (30 + chip_w, chip_y + chip_h), [int(c) for c in COLOR_OVER_OCC], -1)
        cv2.putText(canvas, f"Over-occlusion (False Negative): {stats['FN_pixels']:,} px ({stats['OverOccRate']*100:.1f}%)",
                    (60, footer_y - 2), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (255, 120, 120), 2, cv2.LINE_AA)

        # 青チップ: 誤透過
        x_chip2 = 550
        cv2.rectangle(canvas, (x_chip2, chip_y), (x_chip2 + chip_w, chip_y + chip_h), [int(c) for c in COLOR_UNDER_OCC], -1)
        cv2.putText(canvas, f"Under-occlusion (False Positive): {stats['FP_pixels']:,} px ({stats['UnderOccRate']*100:.1f}%)",
                    (x_chip2 + 30, footer_y - 2), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (100, 190, 255), 2, cv2.LINE_AA)

        # メトリクス: IoU
        x_iou = 1100
        cv2.putText(canvas, f"Mask IoU: {stats['IoU']:.4f}",
                    (x_iou, footer_y - 2), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 255, 180), 2, cv2.LINE_AA)

        return canvas


def natural_sort_key(s: str):
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'(\d+)', s)]


def find_first_image(folder_path: str) -> Optional[str]:
    for ext in ("*.png", "*.bmp", "*.jpg"):
        files = glob.glob(os.path.join(folder_path, ext))
        if files:
            return sorted(files)[0]
    return None


def resolve_gt_dir(test_dir_abs: str, base_dataset_dir: str) -> Optional[str]:
    curr = os.path.abspath(test_dir_abs)
    base = os.path.abspath(base_dataset_dir)

    while True:
        candidate = os.path.join(curr, "GT")
        if os.path.isdir(candidate) and os.path.isdir(os.path.join(candidate, "Left")):
            return candidate
        if curr == base or os.path.dirname(curr) == curr:
            break
        curr = os.path.dirname(curr)

    candidate = os.path.join(base, "GT")
    if os.path.isdir(candidate) and os.path.isdir(os.path.join(candidate, "Left")):
        return candidate

    parent_candidate = os.path.join(base, "..", "GT")
    if os.path.isdir(parent_candidate) and os.path.isdir(os.path.join(parent_candidate, "Left")):
        return os.path.abspath(parent_candidate)

    for root, dirs, _ in os.walk(base):
        if "GT" in dirs:
            c = os.path.join(root, "GT")
            if os.path.isdir(os.path.join(c, "Left")):
                return c

    return None


def parse_args():
    parser = argparse.ArgumentParser(
        description="SICE SI 2026: オクルージョン定性評価用 差分可視化マップ生成スクリプト"
    )
    parser.add_argument(
        "dataset_dir",
        nargs="?",
        default=r"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset",
        help="データセットディレクトリ"
    )
    parser.add_argument(
        "-c", "--condition", "--filter", "-f",
        dest="condition_filter",
        default=None,
        help="評価対象とする条件名の絞り込みキーワード (例: -c Bouchiba, -c density_1.00)"
    )
    parser.add_argument(
        "-o", "--output-dir",
        dest="output_dir",
        default=None,
        help="差分マップの出力先ベースディレクトリ (省略時かつ --in-place なしの場合はデータセット直下の DiffMaps フォルダ)"
    )
    parser.add_argument(
        "--in-place",
        action="store_true",
        dest="in_place",
        help="元のフォルダ構造を崩さず、各評価対象の Left/Right フォルダ直下に差分画像を直接出力する (推奨)"
    )
    parser.add_argument(
        "-m", "--mode",
        dest="mode",
        choices=["all", "mask", "overlay", "montage"],
        default="all",
        help="出力する画像の種類 (all: 全て, mask: 差分マスクのみ, overlay: オーバーレイのみ, montage: 4ペイン横連結のみ)"
    )
    parser.add_argument(
        "--sides",
        dest="sides",
        choices=["left", "right", "both"],
        default="both",
        help="出力対象の眼 (left, right, both)"
    )
    return parser.parse_args()


def main():
    args = parse_args()
    base_dataset_dir = os.path.abspath(args.dataset_dir)
    condition_filter = args.condition_filter

    if args.output_dir:
        base_output_dir = os.path.abspath(args.output_dir)
    else:
        base_output_dir = os.path.join(base_dataset_dir, "DiffMaps")

    print("=== SICE SI オクルージョン定性評価 差分マップ生成 ===", flush=True)
    print(f"データセットディレクトリ: {base_dataset_dir}", flush=True)
    print(f"差分マップ保存先       : {base_output_dir}", flush=True)
    print(f"出力モード             : {args.mode}", flush=True)
    print(f"対象眼                 : {args.sides}", flush=True)

    # 評価対象ディレクトリの探索
    all_candidate_dirs = []
    for root, dirs, _ in os.walk(base_dataset_dir):
        rel_parts = os.path.relpath(root, base_dataset_dir).split(os.sep)
        if "GT" in rel_parts or "DiffMaps" in rel_parts:
            continue
        if "Left" in dirs and "Right" in dirs:
            rel_path = os.path.relpath(root, base_dataset_dir).replace("\\", "/")
            all_candidate_dirs.append(rel_path)

    all_candidate_dirs.sort(key=natural_sort_key)

    if not all_candidate_dirs:
        print("エラー: 評価対象フォルダ (Left/Right を含むフォルダ) が見つかりません。", flush=True)
        return

    if condition_filter:
        pattern = condition_filter.lower()
        candidate_dirs = [d for d in all_candidate_dirs if pattern in d.lower()]
        print(f"フィルタ適用: '{condition_filter}' (一致: {len(candidate_dirs)} / 全 {len(all_candidate_dirs)} 件)", flush=True)
        if not candidate_dirs:
            print(f"警告: キーワード '{condition_filter}' に一致するフォルダが見つかりませんでした。", flush=True)
            return
    else:
        candidate_dirs = all_candidate_dirs

    print(f"\n処理対象条件数: {len(candidate_dirs)} 件\n", flush=True)

    eyes_to_process = []
    if args.sides in ("left", "both"):
        eyes_to_process.append("Left")
    if args.sides in ("right", "both"):
        eyes_to_process.append("Right")

    success_count = 0

    for idx, cond in enumerate(candidate_dirs):
        test_dir = os.path.join(base_dataset_dir, cond)
        target_gt_dir = resolve_gt_dir(test_dir, base_dataset_dir)

        if not target_gt_dir:
            print(f"[{idx+1}/{len(candidate_dirs)}] スキップ (GTなし): {cond}", flush=True)
            continue

        if args.in_place:
            print(f"[{idx+1}/{len(candidate_dirs)}] 差分マップ生成中 (元フォルダ直下に出力): {cond}", flush=True)
        else:
            cond_out_dir = os.path.join(base_output_dir, cond)
            os.makedirs(cond_out_dir, exist_ok=True)
            print(f"[{idx+1}/{len(candidate_dirs)}] 差分マップ生成中: {cond}", flush=True)

        for eye in eyes_to_process:
            gt_eye_dir = os.path.join(target_gt_dir, eye)
            test_eye_dir = os.path.join(test_dir, eye)

            gt_img_path = find_first_image(gt_eye_dir)
            test_img_path = find_first_image(test_eye_dir)

            if not (gt_img_path and test_img_path):
                print(f"  - {eye}: 画像が見つかりません (GT: {gt_img_path}, Test: {test_img_path})", flush=True)
                continue

            # 画像読み込み (BGR -> RGB)
            gt_bgr = cv2.imread(gt_img_path, cv2.IMREAD_COLOR)
            test_bgr = cv2.imread(test_img_path, cv2.IMREAD_COLOR)

            if gt_bgr is None or test_bgr is None:
                continue

            gt_rgb = cv2.cvtColor(gt_bgr, cv2.COLOR_BGR2RGB)
            test_rgb = cv2.cvtColor(test_bgr, cv2.COLOR_BGR2RGB)

            vis = OcclusionDiffVisualizer(gt_rgb, test_rgb)
            stats = vis.get_statistics()

            title = f"{cond} [{eye}]"

            # 保存先ディレクトリの決定:
            # --in-place の場合は各眼のフォルダ (test_dir/Left, test_dir/Right) の直下に保存
            # それ以外は base_output_dir/cond 配下に保存
            if args.in_place:
                save_dir = test_eye_dir
                prefix = "diff_"
            else:
                save_dir = cond_out_dir
                prefix = f"diff_{eye.lower()}_"

            # 1) 差分マスク
            if args.mode in ("all", "mask"):
                diff_mask = vis.generate_diff_mask()
                diff_bgr = cv2.cvtColor(diff_mask, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}mask.png") if args.in_place else os.path.join(save_dir, f"diff_mask_{eye.lower()}.png")
                cv2.imwrite(out_path, diff_bgr)

            # 2) オーバーレイ
            if args.mode in ("all", "overlay"):
                overlay = vis.generate_overlay()
                overlay_bgr = cv2.cvtColor(overlay, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}overlay.png") if args.in_place else os.path.join(save_dir, f"diff_overlay_{eye.lower()}.png")
                cv2.imwrite(out_path, overlay_bgr)

            # 3) モンタージュ
            if args.mode in ("all", "montage"):
                montage = vis.generate_montage(title=title)
                montage_bgr = cv2.cvtColor(montage, cv2.COLOR_RGB2BGR)
                out_path = os.path.join(save_dir, f"{prefix}montage.png") if args.in_place else os.path.join(save_dir, f"diff_montage_{eye.lower()}.png")
                cv2.imwrite(out_path, montage_bgr)

            print(f"  - {eye:<5}: IoU={stats['IoU']:.4f} | 誤遮蔽(赤)={stats['FN_pixels']:,}px ({stats['OverOccRate']*100:.1f}%) | 誤透過(青)={stats['FP_pixels']:,}px ({stats['UnderOccRate']*100:.1f}%)", flush=True)

        success_count += 1

    print(f"\n[完了] {success_count} 件の条件の差分マップを保存しました: {base_output_dir}", flush=True)
    print("【色の凡例】")
    print("  ■ 赤 (Red)  : 誤って遮蔽 (False Negative / 過剰遮蔽) - 本来見えるべき領域が遮蔽された")
    print("  ■ 青 (Blue) : 誤って透過 (False Positive / 遮蔽漏れ) - 本来遮蔽されるべき領域が透過した")
    print("  ■ 白/グレー : 正しく表示 (True Positive) - GTと一致")
    print("  ■ 黒 (Black): 正しく遮蔽 / 背景 (True Negative)", flush=True)

if __name__ == "__main__":
    main()
