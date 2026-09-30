"""
SICESI Python Suite: 最適化レポート・評価 CSV エクスポーター
"""

import os
import csv
import numpy as np
from .pattern_lut import (
    compress_indices, z_to_hex, occ_breakdown_str, class_breakdown_str
)


class OptimizationReportExporter:
    """
    SICESI 最適化結果（目的関数 A / B、8候補 / 20候補 / 36クラス / 256パターン）の
    評価サマリー、個別比較、GAP分析、および LUT CSV を出力するクラス。
    """
    
    @staticmethod
    def export_all_reports(dataset, lut, opt_res, output_dir):
        """
        全レポート CSV を一括保存します。
        
        Args:
            dataset: load_dataset で取得したデータセットリスト
            lut: build_256_lookup() の辞書
            opt_res: PatternOptimizationPipeline の結果辞書
            output_dir: 出力先ディレクトリ
        """
        os.makedirs(output_dir, exist_ok=True)
        
        OptimizationReportExporter.export_common_rules_summary(opt_res, lut, output_dir)
        OptimizationReportExporter.export_objective_comparison(dataset, opt_res, output_dir)
        OptimizationReportExporter.export_common_rule_evaluation(dataset, opt_res, output_dir)
        indiv_rows = OptimizationReportExporter.export_individual_optima(dataset, opt_res, output_dir)
        OptimizationReportExporter.export_individual_gap_analysis(dataset, opt_res, output_dir)
        OptimizationReportExporter.export_common_luts(opt_res, lut, output_dir)
        
        return indiv_rows

    @staticmethod
    def export_common_rules_summary(opt_res, lut, output_dir):
        """1. 共通規則サマリー (common_rules_summary.csv) の出力"""
        res_8_base = opt_res['res_8_base']
        res_20_A = opt_res['res_20_A']
        eval_36_A = opt_res['eval_36_A']
        eval_256_A = opt_res['eval_256_A']
        z_36_A = opt_res['z_36_A']
        z_256_A = opt_res['z_256_A']
        z_36_A_as_256 = opt_res['z_36_A_as_256']
        vis_c_A = opt_res['vis_c_A']
        vis_m_A = opt_res['vis_m_A']
        
        res_8_B = opt_res['res_8_B']
        res_20_B = opt_res['res_20_B']
        eval_36_B = opt_res['eval_36_B']
        eval_256_B = opt_res['eval_256_B']
        z_36_B = opt_res['z_36_B']
        z_256_B = opt_res['z_256_B']
        z_36_B_as_256 = opt_res['z_36_B_as_256']
        vis_c_B = opt_res['vis_c_B']
        vis_m_B = opt_res['vis_m_B']

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
        
        csv_path = os.path.join(output_dir, "common_rules_summary.csv")
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=list(summary_rows[0].keys()))
            writer.writeheader()
            writer.writerows(summary_rows)
        print(f"[+] 共通規則サマリー (common_rules_summary.csv) を保存しました: {csv_path}")

    @staticmethod
    def export_objective_comparison(dataset, opt_res, output_dir):
        """2. 目的関数A vs B 直接比較表 (objective_comparison.csv) の出力"""
        base_ious = opt_res['base_ious']
        eval_36_A = opt_res['eval_36_A']
        eval_36_B = opt_res['eval_36_B']
        eval_256_A = opt_res['eval_256_A']
        eval_256_B = opt_res['eval_256_B']
        
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
        
        csv_path = os.path.join(output_dir, "objective_comparison.csv")
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=list(comp_rows[0].keys()))
            writer.writeheader()
            writer.writerows(comp_rows)
        print(f"[+] 目的関数A vs B 直接比較表 (objective_comparison.csv) を保存しました: {csv_path}")

    @staticmethod
    def export_common_rule_evaluation(dataset, opt_res, output_dir):
        """3. 全手法評価一覧 (common_rule_evaluation.csv) の出力"""
        base_ious = opt_res['base_ious']
        res_8_base = opt_res['res_8_base']
        res_20_A = opt_res['res_20_A']
        eval_36_A = opt_res['eval_36_A']
        eval_36_B = opt_res['eval_36_B']
        eval_256_A = opt_res['eval_256_A']
        eval_256_B = opt_res['eval_256_B']

        eval_rows = []
        for i, d in enumerate(dataset):
            did = d['data_id']
            no_occ = d['no_occ_iou']
            
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
        
        csv_path = os.path.join(output_dir, "common_rule_evaluation.csv")
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=list(eval_rows[0].keys()))
            writer.writeheader()
            writer.writerows(eval_rows)
        print(f"[+] 共通規則評価一覧 (common_rule_evaluation.csv) を保存しました: {csv_path}")

    @staticmethod
    def export_individual_optima(dataset, opt_res, output_dir):
        """4. 個別最適一覧 (individual_optima.csv) の出力"""
        res_8_base = opt_res['res_8_base']
        res_20_A = opt_res['res_20_A']
        indiv_36 = opt_res['indiv_36']
        indiv_256 = opt_res['indiv_256']

        indiv_rows = []
        for i, d in enumerate(dataset):
            did = d['data_id']
            case = d['case']
            dens = d['density_str']
            eye = d['eye']
            O_tot = int(np.sum(d['O']))
            
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
            
        csv_path = os.path.join(output_dir, "individual_optima.csv")
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=list(indiv_rows[0].keys()))
            writer.writeheader()
            writer.writerows(indiv_rows)
        print(f"[+] 個別最適一覧 (individual_optima.csv) を保存しました: {csv_path}")
        return indiv_rows

    @staticmethod
    def export_individual_gap_analysis(dataset, opt_res, output_dir):
        """5. 個別最適からの低下分析 (individual_gap_analysis.csv) の出力"""
        res_8_base = opt_res['res_8_base']
        res_20_A = opt_res['res_20_A']
        indiv_36 = opt_res['indiv_36']
        indiv_256 = opt_res['indiv_256']
        eval_36_A = opt_res['eval_36_A']
        eval_36_B = opt_res['eval_36_B']
        eval_256_A = opt_res['eval_256_A']
        eval_256_B = opt_res['eval_256_B']

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
            
        csv_path = os.path.join(output_dir, "individual_gap_analysis.csv")
        with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=list(gap_rows[0].keys()))
            writer.writeheader()
            writer.writerows(gap_rows)
        print(f"[+] 個別最適からの低下分析 (individual_gap_analysis.csv) を保存しました: {csv_path}")

    @staticmethod
    def export_common_luts(opt_res, lut, output_dir):
        """6. 共通規則・共通LUTファイル (ObjA & ObjB) の出力"""
        z_36_A = opt_res['z_36_A']
        z_256_A = opt_res['z_256_A']
        z_36_B = opt_res['z_36_B']
        z_256_B = opt_res['z_256_B']
        counts_V_36 = opt_res['counts_V_36']
        counts_O_36 = opt_res['counts_O_36']
        counts_V_256 = opt_res['counts_V_256']
        counts_O_256 = opt_res['counts_O_256']

        for obj_name, z36, z256 in [('objA', z_36_A, z_256_A), ('objB', z_36_B, z_256_B)]:
            csv_lut_36 = os.path.join(output_dir, f"common_lut_36_{obj_name}.csv")
            tot_obs_36 = np.sum(counts_V_36 + counts_O_36, axis=0)
            with open(csv_lut_36, 'w', newline='', encoding='utf-8-sig') as f:
                writer = csv.writer(f)
                writer.writerow(["rotation_class_id", "rotation_representative", "representative_bits", "occupied_count", "decision", "label", "total_observed_pixels"])
                for cid in range(36):
                    rep = lut['unique_reps'][cid]
                    tot_obs = int(tot_obs_36[cid])
                    dec = z36[cid]
                    writer.writerow([cid, rep, bin(rep)[2:].zfill(8), lut['n_occ'][rep], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs])
                    
            csv_lut_256 = os.path.join(output_dir, f"common_lut_256_{obj_name}.csv")
            tot_obs_256 = np.sum(counts_V_256 + counts_O_256, axis=0)
            with open(csv_lut_256, 'w', newline='', encoding='utf-8-sig') as f:
                writer = csv.writer(f)
                writer.writerow(["mask", "bits_b7_to_b0", "occupied_count", "rotation_class_id", "decision", "label", "total_observed_pixels"])
                for m in range(256):
                    tot_obs = int(tot_obs_256[m])
                    dec = z256[m]
                    writer.writerow([m, bin(m)[2:].zfill(8), lut['n_occ'][m], lut['rot_cid'][m], dec, "VISIBLE" if dec == 1 else "OCCLUDED", tot_obs])
                    
        print(f"[+] 共通規則・共通LUTファイル (ObjA/ObjB) を保存しました。")
