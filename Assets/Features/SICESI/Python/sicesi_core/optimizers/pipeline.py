"""
SICESI Python Suite: 4手法・2目的関数の統合最適化パイプライン
"""

import numpy as np

from ..pattern_lut import (
    compress_indices, occ_breakdown_str, z_to_hex
)
from ..metrics import (
    evaluate_lut_extended
)
from .fixed_candidates import (
    optimize_fixed_8, optimize_fixed_20
)
from .milp_optimizer import (
    optimize_weighted_iou_milp, get_individual_optima_36_and_256
)
from .local_search import (
    local_search_36_iou, local_search_256_iou
)


class PatternOptimizationPipeline:
    """
    データセットと LUT を受け取り、
    1. 基準 8 候補共通最適 (Baseline) の決定
    2. 目的関数 B (相対低減率最大化) の 8 / 20 / 36 / 256 最適化
    3. 目的関数 A (平均IoU最大化) の 20 / 36 / 256 最適化 (マルチスタート局所探索)
    4. 個別最適 (36 / 256) の算出
    を一貫して実行・統括するパイプラインクラス。
    """
    
    @staticmethod
    def run(dataset, lut):
        """
        全最適化ステップを実行し、結果辞書を返します。
        """
        D = len(dataset)
        G_list = np.array([d['G'] for d in dataset], dtype=np.int64)
        
        # 1. カウント行列の事前作成
        counts_V_36 = np.zeros((D, 36), dtype=np.int64)
        counts_O_36 = np.zeros((D, 36), dtype=np.int64)
        for i, d in enumerate(dataset):
            for m in range(256):
                cid = lut['rot_cid'][m]
                counts_V_36[i, cid] += d['V'][m]
                counts_O_36[i, cid] += d['O'][m]
        counts_V_256 = np.array([d['V'] for d in dataset], dtype=np.int64)
        counts_O_256 = np.array([d['O'] for d in dataset], dtype=np.int64)
        
        tot_V_36 = np.sum(counts_V_36, axis=0)
        tot_O_36 = np.sum(counts_O_36, axis=0)
        majority_z_36 = (tot_V_36 >= tot_O_36).astype(np.int32)
        tot_V_256 = np.sum(counts_V_256, axis=0)
        tot_O_256 = np.sum(counts_O_256, axis=0)
        majority_z_256 = (tot_V_256 >= tot_O_256).astype(np.int32)
        
        # ----------------------------------------------------------------------
        # ステップ 1: 基準従来法 (固定8候補の共通最適) の決定
        # ----------------------------------------------------------------------
        print("[*] [ステップ 1/5] 基準従来法 (固定8候補 共通占有数ルール) の決定中...")
        res_8_base = optimize_fixed_8(dataset, lut)
        base_ious = np.array([r['iou'] for r in res_8_base['common_eval']['individual']], dtype=np.float64)
        print(f"    -> 基準規則: {res_8_base['common_rule_desc']} (平均IoU: {res_8_base['common_eval']['mean_iou']*100:.4f}%)")
        
        weights_rel = 1.0 / np.maximum(1.0 - base_ious, 1e-4)
        print(f"    -> 相対低減率 重み w_i 分布: min={weights_rel.min():.2f}, mean={weights_rel.mean():.2f}, max={weights_rel.max():.2f}")
        
        res_8_base = optimize_fixed_8(dataset, lut, base_ious=base_ious)
        
        # ----------------------------------------------------------------------
        # ステップ 2: 目的関数 B (1-IoU 相対低減率最大化: Weighted) による最適化
        # ----------------------------------------------------------------------
        print("\n" + "-" * 80)
        print("[*] [ステップ 2/5] 【目的関数 B: 1-IoU 相対低減率最大化 (Weighted)】の最適化を実行中...")
        print("-" * 80)
        
        # 2.1 固定8候補 B
        res_8_B = optimize_fixed_8(dataset, lut, base_ious=base_ious, weights=weights_rel)
        print(f"  [8候補 B] 最良: {res_8_B['common_rule_desc']} (平均相対低減: {res_8_B['common_eval']['mean_rel_reduction']*100:+.2f}%, 平均IoU: {res_8_B['common_eval']['mean_iou']*100:.4f}%)")
        
        # 2.2 固定20候補 B
        res_20_B = optimize_fixed_20(dataset, lut, base_ious=base_ious, weights=weights_rel)
        print(f"  [20候補 B] 最良: {res_20_B['common_rule_desc']} (平均相対低減: {res_20_B['common_eval']['mean_rel_reduction']*100:+.2f}%, 平均IoU: {res_20_B['common_eval']['mean_iou']*100:.4f}%)")
        print(f"    -> 規則詳細: {res_20_B['common_summary']} | 可視パターン: {res_20_B['common_details']}")
        
        # 2.3 36クラスLUT B (MILP)
        fallback_z_256_B = res_20_B['common_z']
        fallback_z_36_B = np.zeros(36, dtype=np.int32)
        for cid in range(36):
            rep = lut['unique_reps'][cid]
            fallback_z_36_B[cid] = fallback_z_256_B[rep]

        print("  [36クラス B] MILP 求解中 (相対低減率最大化)...")
        z_36_B_milp, _, status_36_B, time_36_B, gap_36_B = optimize_weighted_iou_milp(
            counts_V_36, counts_O_36, G_list, fallback_z_36_B, weights=weights_rel, time_limit_sec=60
        )
        
        cands_36_B = [z_36_B_milp, fallback_z_36_B, majority_z_36]
        best_eval_36_B = None
        best_z_36_B = None
        for cz in cands_36_B:
            cz_256 = np.array([cz[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
            cev = evaluate_lut_extended(cz_256, dataset, base_ious)
            if best_eval_36_B is None or cev['mean_rel_reduction'] > best_eval_36_B['mean_rel_reduction']:
                best_eval_36_B = cev
                best_z_36_B = cz

        z_36_B = best_z_36_B
        eval_36_B = best_eval_36_B
        z_36_B_as_256 = np.array([z_36_B[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        vis_c_B = [c for c in range(36) if z_36_B[c] == 1]
        print(f"    -> 完了 ({time_36_B:.2f}秒, {status_36_B}): 平均相対低減 = {eval_36_B['mean_rel_reduction']*100:+.2f}%, 平均IoU = {eval_36_B['mean_iou']*100:.4f}% (可視: {len(vis_c_B)}/36 クラス, 最悪悪化: {eval_36_B['worst_rel_drop']*100:+.2f}%)")
        
        # 2.4 256パターンLUT B (MILP)
        print("  [256パターン B] MILP 求解中 (相対低減率最大化)...")
        z_256_B_milp, _, status_256_B, time_256_B, gap_256_B = optimize_weighted_iou_milp(
            counts_V_256, counts_O_256, G_list, z_36_B_as_256, weights=weights_rel, time_limit_sec=120
        )
        
        cands_256_B = [z_256_B_milp, z_36_B_as_256, majority_z_256, res_20_B['common_z']]
        best_eval_256_B = None
        best_z_256_B = None
        for cz_256 in cands_256_B:
            cev = evaluate_lut_extended(cz_256, dataset, base_ious)
            if best_eval_256_B is None or cev['mean_rel_reduction'] > best_eval_256_B['mean_rel_reduction']:
                best_eval_256_B = cev
                best_z_256_B = cz_256
                
        z_256_B = best_z_256_B
        eval_256_B = best_eval_256_B
        vis_m_B = [m for m in range(256) if z_256_B[m] == 1]
        print(f"    -> 完了 ({time_256_B:.2f}秒, {status_256_B}): 平均相対低減 = {eval_256_B['mean_rel_reduction']*100:+.2f}%, 平均IoU = {eval_256_B['mean_iou']*100:.4f}% (可視: {len(vis_m_B)}/256 パターン, 最悪悪化: {eval_256_B['worst_rel_drop']*100:+.2f}%)")

        # ----------------------------------------------------------------------
        # ステップ 3: 目的関数 A (平均IoU最大化: Unweighted) による最適化 & 局所探索
        # ----------------------------------------------------------------------
        print("\n" + "-" * 80)
        print("[*] [ステップ 3/5] 【目的関数 A: 平均IoU最大化 (Unweighted)】の最適化を実行中...")
        print("-" * 80)
        
        # 3.1 固定20候補 A
        res_20_A = optimize_fixed_20(dataset, lut, base_ious=base_ious)
        print(f"  [20候補 A] 最良: {res_20_A['common_rule_desc']} (平均IoU: {res_20_A['common_eval']['mean_iou']*100:.4f}%, 平均相対低減: {res_20_A['common_eval']['mean_rel_reduction']*100:+.2f}%)")
        print(f"    -> 規則詳細: {res_20_A['common_summary']} | 可視パターン: {res_20_A['common_details']}")
        
        # 3.2 36クラスLUT (局所探索)
        print("  [36クラス A] 局所探索中 (平均IoU最大化: 占有数判定, 連続非占有規則, RelRed解, 多数決割当て)...")
        z_36_from_occ = np.array([res_8_base['common_z'][lut['unique_reps'][c]] for c in range(36)], dtype=np.int32)
        z_36_from_r20 = np.array([res_20_A['common_z'][lut['unique_reps'][c]] for c in range(36)], dtype=np.int32)
        z_36_from_relB = z_36_B.copy()
        z_36_from_maj = majority_z_36.copy()
        
        z_36_A_milp, _, status_36_A, time_36_A, gap_36_A = optimize_weighted_iou_milp(
            counts_V_36, counts_O_36, G_list, z_36_from_r20, weights=None, time_limit_sec=60
        )
        
        inits_36 = [
            ('占有数判定', z_36_from_occ),
            ('連続非占有規則', z_36_from_r20),
            ('Relative_Reduction解', z_36_from_relB),
            ('多数決割当て', z_36_from_maj),
            ('MILP_A解', z_36_A_milp)
        ]
        
        best_z_36_A = None
        best_eval_36_A = None
        best_init_name_36 = ""
        
        for init_name, init_z in inits_36:
            ls_z, ls_mean, ls_iters = local_search_36_iou(init_z, counts_V_36, counts_O_36, G_list)
            ls_z_256 = np.array([ls_z[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
            cev = evaluate_lut_extended(ls_z_256, dataset, base_ious)
            init_cev = evaluate_lut_extended(np.array([init_z[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32), dataset, base_ious)
            print(f"    - [初期値: {init_name:<16s}] 初期平均IoU: {init_cev['mean_iou']*100:.4f}% -> 探索後({ls_iters}回反転): {cev['mean_iou']*100:.4f}%")
            
            if best_eval_36_A is None or cev['mean_iou'] > best_eval_36_A['mean_iou']:
                best_eval_36_A = cev
                best_z_36_A = ls_z
                best_init_name_36 = init_name
                
        z_36_A = best_z_36_A
        eval_36_A = best_eval_36_A
        z_36_A_as_256 = np.array([z_36_A[lut['rot_cid'][m]] for m in range(256)], dtype=np.int32)
        vis_c_A = [c for c in range(36) if z_36_A[c] == 1]
        print(f"    -> 【採用解】 由来: {best_init_name_36}, 平均IoU = {eval_36_A['mean_iou']*100:.4f}%, 最小IoU = {np.min([r['iou'] for r in eval_36_A['individual']]):.6f}, common_8差 = {eval_36_A['mean_iou'] - res_8_base['common_eval']['mean_iou']:+.6f}, 最大悪化 = {eval_36_A['worst_iou_drop']:+.6f}")
        print(f"    -> クラス詳細: {compress_indices(vis_c_A, prefix='c')} ({int(np.sum(z_36_A_as_256))} / 256 patterns)")
        
        # 3.3 256パターンLUT (局所探索)
        print("  [256パターン A] 局所探索中 (平均IoU最大化: 既存解, 多数決, 36クラス最良解)...")
        z_256_A_milp, _, status_256_A, time_256_A, gap_256_A = optimize_weighted_iou_milp(
            counts_V_256, counts_O_256, G_list, z_36_A_as_256, weights=None, time_limit_sec=60
        )
        
        inits_256 = [
            ('36クラスLUT最良解', z_36_A_as_256),
            ('多数決割当て', majority_z_256),
            ('MILP_A解', z_256_A_milp),
            ('Relative_Reduction解', z_256_B),
            ('占有数判定', res_8_base['common_z']),
            ('連続非占有規則', res_20_A['common_z'])
        ]
        
        best_z_256_A = None
        best_eval_256_A = None
        best_init_name_256 = ""
        
        for init_name, init_z in inits_256:
            ls_z, ls_mean, ls_iters = local_search_256_iou(init_z, counts_V_256, counts_O_256, G_list)
            cev = evaluate_lut_extended(ls_z, dataset, base_ious)
            init_cev = evaluate_lut_extended(init_z, dataset, base_ious)
            print(f"    - [初期値: {init_name:<16s}] 初期平均IoU: {init_cev['mean_iou']*100:.4f}% -> 探索後({ls_iters}回反転): {cev['mean_iou']*100:.4f}%")
            
            if best_eval_256_A is None or cev['mean_iou'] > best_eval_256_A['mean_iou']:
                best_eval_256_A = cev
                best_z_256_A = ls_z
                best_init_name_256 = init_name

        z_256_A = best_z_256_A
        eval_256_A = best_eval_256_A
        vis_m_A = [m for m in range(256) if z_256_A[m] == 1]
        print(f"    -> 【採用解】 由来: {best_init_name_256}, 平均IoU = {eval_256_A['mean_iou']*100:.4f}%, 最小IoU = {np.min([r['iou'] for r in eval_256_A['individual']]):.6f}, common_8差 = {eval_256_A['mean_iou'] - res_8_base['common_eval']['mean_iou']:+.6f}, 最大悪化 = {eval_256_A['worst_iou_drop']:+.6f}")
        print(f"    -> 各占有数の可視パターン数: {occ_breakdown_str(z_256_A, lut['n_occ'])}")
        print(f"    -> パターンHEX: {z_to_hex(z_256_A)}")
        print(f"    -> 各占有数の可視パターン数: {occ_breakdown_str(z_256_B, lut['n_occ'])}")
        print(f"    -> パターンHEX: {z_to_hex(z_256_B)}")

        # ----------------------------------------------------------------------
        # ステップ 4: 個別最適 (36クラス & 256パターン)
        # ----------------------------------------------------------------------
        print("\n[*] [ステップ 4/5] 個別最適 (36クラス & 256パターン) の計算中...")
        indiv_36, indiv_256 = get_individual_optima_36_and_256(dataset, lut, base_ious=base_ious)

        return {
            'res_8_base': res_8_base,
            'base_ious': base_ious,
            'weights_rel': weights_rel,
            'counts_V_36': counts_V_36,
            'counts_O_36': counts_O_36,
            'counts_V_256': counts_V_256,
            'counts_O_256': counts_O_256,
            # 目的関数 A 結果
            'res_20_A': res_20_A,
            'z_36_A': z_36_A,
            'eval_36_A': eval_36_A,
            'z_36_A_as_256': z_36_A_as_256,
            'vis_c_A': vis_c_A,
            'z_256_A': z_256_A,
            'eval_256_A': eval_256_A,
            'vis_m_A': vis_m_A,
            # 目的関数 B 結果
            'res_8_B': res_8_B,
            'res_20_B': res_20_B,
            'z_36_B': z_36_B,
            'eval_36_B': eval_36_B,
            'z_36_B_as_256': z_36_B_as_256,
            'vis_c_B': vis_c_B,
            'z_256_B': z_256_B,
            'eval_256_B': eval_256_B,
            'vis_m_B': vis_m_B,
            # 個別最適
            'indiv_36': indiv_36,
            'indiv_256': indiv_256,
        }
