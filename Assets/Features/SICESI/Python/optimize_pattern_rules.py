"""
================================================================================
SICESI: 占有パターン自動解析・真値集計 & 4手法最適化スクリプト
(Feature: SICESI - Pattern Rule Optimization & Objective Evaluation)
================================================================================

【概要】
  本スクリプトは、撮影データセットから未サンプリング隙間領域を対象とした
  真値集計ファイル (pattern_VO.csv) を自動出力し、以下の4手法に対する
  個別最適・共通最適の数理計算（MILP / 局所探索）および diff_map 画像出力を行います：
    1. 固定8候補      : 最低占有数 1..8 (N_occ < R が可視)
    2. 固定20候補     : 占有数・最大連続非占有数の20代表設定 (R_th, L_th)
    3. 固定36クラスLUT : 回転不変36クラスへの独立最適割当
    4. 固定256パターン : 256ビット完全独立最適割当

【実行方法】
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py --skip-diff
  python Assets/Features/SICESI/Python/optimize_pattern_rules.py --force
"""

import os
import sys
import argparse
import numpy as np

# Windows コンソール文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# sicesi_core から各責務のモジュール・クラスをインポート
from sicesi_core.pattern_lut import (
    build_256_lookup
)
from sicesi_core.dataset_loader import (
    load_dataset, save_pattern_vo_csv
)
from sicesi_core.optimizers import (
    PatternOptimizationPipeline
)
from sicesi_core.report_exporter import (
    OptimizationReportExporter
)
from sicesi_core.validator import (
    validate_hierarchy_inequalities
)
from sicesi_core.visualizer import (
    export_all_diff_maps
)


def run_pattern_rules_optimization(dataset_root=None, output_dir=None, force_regenerate=False, skip_diff=False):
    """
    データセットの探索・読み込み、最適化パイプラインの実行、CSV レポート出力、
    数学的包含関係の検証、および diff_map 画像出力を一括統括して実行します。
    """
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_dataset_dir = os.path.abspath(os.path.join(script_dir, "../../../../../Estimation/SICESI_Dataset"))
    
    if dataset_root is None:
        if os.path.exists(default_dataset_dir):
            dataset_root = default_dataset_dir
        else:
            dataset_root = script_dir
            
    dataset_root = os.path.abspath(dataset_root)
    
    if output_dir is None:
        output_dir = os.path.join(dataset_root, "RuleOptimizationResults")
    os.makedirs(output_dir, exist_ok=True)
    
    lut = build_256_lookup()
    
    print("=" * 80)
    print("【占有パターンCSVから個別最適・共通最適を求める自動解析】")
    print(f"  探索対象ルート: {dataset_root}")
    print(f"  出力先フォルダ: {output_dir}")
    print(f"  強制再集計    : {force_regenerate}")
    print("=" * 80)
    
    # 1. 全CSVの読み込み
    dataset = load_dataset(dataset_root, lut, force_regenerate=force_regenerate)
    if not dataset:
        print("[!] 解析対象データが存在しませんでした。")
        return
        
    # 2. 統合最適化パイプラインの実行 (ステップ 1〜4: 基準決定, 目的関数B, 目的関数A, 個別最適)
    opt_res = PatternOptimizationPipeline.run(dataset, lut)
    
    # 3. CSV レポート群のエクスポート (ステップ 5)
    print("\n[*] [ステップ 5/5] CSV ファイル群の保存中...")
    indiv_rows = OptimizationReportExporter.export_all_reports(dataset, lut, opt_res, output_dir)
    
    # 4. 全48条件合算 pattern_VO.csv の保存
    vo_csv_paths = [
        output_dir,
        os.path.abspath(os.path.join(script_dir, "..", "Data"))
    ]
    save_pattern_vo_csv(dataset, vo_csv_paths)
    
    # 5. 自動検証 & サマリー表示
    validate_hierarchy_inequalities(
        opt_res['res_8_base'], opt_res['res_20_A'], opt_res['eval_36_A'], opt_res['eval_256_A'],
        opt_res['res_8_B'], opt_res['res_20_B'], opt_res['eval_36_B'], opt_res['eval_256_B']
    )
    
    # 6. 最適化結果 diff_map の自動エクスポート (ステップ 6)
    if not skip_diff:
        common_luts = {
            'No_Extra_Occlusion': ('Baseline (No Extra Occ)', 'All Visible', np.ones(256, dtype=bool)),
            'Fixed_8_Candidates': ('Fixed 8 (N_occ < R)', opt_res['res_8_base'].get('common_rule_desc', 'Occ < 7'), np.array([bool(opt_res['res_8_base']['common_z'][m]) for m in range(256)], dtype=bool)),
            'Fixed_20_Candidates': ('Fixed 20 (R_th, L_th)', opt_res['res_20_A'].get('common_rule_desc', ''), np.array([bool(opt_res['res_20_A']['common_z'][m]) for m in range(256)], dtype=bool)),
            'Oracle_36_RotationLUT': ('Oracle 36 (Rotation LUT)', f"{len(opt_res['vis_c_A'])} / 36 classes", np.array([bool(opt_res['z_36_A_as_256'][m]) for m in range(256)], dtype=bool)),
            'Oracle_256_ExactLUT': ('Oracle 256 (Exact LUT)', f"{len(opt_res['vis_m_A'])} / 256 patterns", np.array([bool(opt_res['z_256_A'][m]) for m in range(256)], dtype=bool)),
        }
        export_all_diff_maps(dataset, indiv_rows, common_luts, output_dir)
    else:
        print("[*] --skip-diff が指定されたため、diff_map の画像出力をスキップしました。")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="SICESI: 占有パターンCSV (pattern_VO.csv) の集計・生成および4手法の個別最適・共通最適LUT解析スクリプト"
    )
    parser.add_argument(
        "-i", "--input", "--dataset-root",
        dest="dataset_root",
        default=None,
        help="探索対象のデータセットルートパス (省略時: ../../../../../Estimation/SICESI_Dataset を自動探索)"
    )
    parser.add_argument(
        "-o", "--output-dir",
        dest="output_dir",
        default=None,
        help="解析結果CSV・最適化LUT・diff_mapの出力先ディレクトリ (省略時: <dataset_root>/RuleOptimizationResults)"
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="既存の集計キャッシュを無視し、撮影画像から強制的に pattern_counts_256.csv / pattern_VO.csv を再集計"
    )
    parser.add_argument(
        "--skip-diff",
        action="store_true",
        help="diff_map 画像およびモンタージュの出力をスキップし、数値計算および CSV 出力のみを高速実行"
    )
    
    parsed_args = parser.parse_args()
    
    run_pattern_rules_optimization(
        dataset_root=parsed_args.dataset_root,
        output_dir=parsed_args.output_dir,
        force_regenerate=parsed_args.force,
        skip_diff=parsed_args.skip_diff
    )
