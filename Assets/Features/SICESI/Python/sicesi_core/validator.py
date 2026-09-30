"""
SICESI Python Suite: 包含関係不等式および数学的整合性バリデータ
"""

import numpy as np


def validate_hierarchy_inequalities(res_8_base, res_20_A, eval_36_A, eval_256_A,
                                   res_8_B, res_20_B, eval_36_B, eval_256_B):
    """
    4手法（8候補, 20候補, 36クラス, 256パターン）間の包含関係不等式を検証し、
    結果をコンソールに表示するとともに真偽値を返します。
    
    目的関数 A (IoU 最大化):
        J*_8 <= J*_20 <= J*_36 <= J*_256
    目的関数 B (1-IoU 相対低減率最大化):
        r*_8 <= r*_20 <= r*_36 <= r*_256
    """
    print("\n" + "=" * 80)
    print("【方式間の包含関係 & 数学的整合性チェック】")
    
    # 目的関数 A
    m8_A = res_8_base['common_eval']['mean_iou']
    m20_A = res_20_A['common_eval']['mean_iou']
    m36_A = eval_36_A['mean_iou']
    m256_A = eval_256_A['mean_iou']
    print(f"  [目的関数 A: 平均IoU最大化] 不等式: J*_8 <= J*_20 <= J*_36 <= J*_256")
    print(f"    {m8_A*100:.4f}% <= {m20_A*100:.4f}% <= {m36_A*100:.4f}% <= {m256_A*100:.4f}%")
    ineq_A = (m8_A <= m20_A + 1e-6) and (m20_A <= m36_A + 1e-6) and (m36_A <= m256_A + 1e-6)
    print(f"    -> 整合性判定: {'完全成立 (PASS!!)' if ineq_A else '不成立 (FAIL)'}")
    
    # 目的関数 B
    m8_B = res_8_B['common_eval']['mean_rel_reduction']
    m20_B = res_20_B['common_eval']['mean_rel_reduction']
    m36_B = eval_36_B['mean_rel_reduction']
    m256_B = eval_256_B['mean_rel_reduction']
    print(f"  [目的関数 B: 相対低減率最大化] 不等式: r*_8 <= r*_20 <= r*_36 <= r*_256")
    print(f"    {m8_B*100:+.2f}% <= {m20_B*100:+.2f}% <= {m36_B*100:+.2f}% <= {m256_B*100:+.2f}%")
    ineq_B = (m8_B <= m20_B + 1e-4) and (m20_B <= m36_B + 1e-4) and (m36_B <= m256_B + 1e-4)
    print(f"    -> 整合性判定: {'完全成立 (PASS!!)' if ineq_B else '不成立 (FAIL)'}")
    print("=" * 80)
    
    return ineq_A and ineq_B
