"""
SICESI Python Suite: 256 パターン LUT・36 回転不変クラス・ビット表現モジュール
"""

from typing import List, Dict, Any, Tuple
import numpy as np


def compress_indices(indices: List[int], prefix: str = "") -> str:
    """
    連続する数値を 0-7,9,11 のようにレンジ圧縮してカンマ区切りにする
    例: [0, 1, 2, 3, 5, 7, 8, 9] -> "0-3,5,7-9"
    prefix="c" -> "c[0-3,5,7-9]"
    """
    if not indices:
        return "none"
    indices = sorted(indices)
    ranges = []
    start = indices[0]
    prev = start
    for idx in indices[1:]:
        if idx == prev + 1:
            prev = idx
        else:
            if start == prev:
                ranges.append(f"{start}")
            else:
                ranges.append(f"{start}-{prev}")
            start = idx
            prev = idx
    if start == prev:
        ranges.append(f"{start}")
    else:
        ranges.append(f"{start}-{prev}")
    s = ",".join(ranges)
    return f"{prefix}[{s}]" if prefix else s


def z_to_hex(z_256: np.ndarray) -> str:
    """
    256ビット判定配列 (0または1の長さ256) を 64文字の 16進数文字列 (0x...) に変換
    z[0] が最下位ビット (bit 0), z[255] が最上位ビット (bit 255)
    """
    val = 0
    for i, bit in enumerate(z_256):
        if bit:
            val |= (1 << i)
    return f"0x{val:064X}"


def hex_to_lut(hex_str: str) -> np.ndarray:
    """
    64文字の16進数文字列から 256要素のブール判定配列 (LUT) を復元
    """
    if hex_str.startswith("0x") or hex_str.startswith("0X"):
        hex_str = hex_str[2:]
    hex_str = hex_str.zfill(64)
    val = int(hex_str, 16)
    lut = np.zeros(256, dtype=bool)
    for m in range(256):
        if (val >> m) & 1:
            lut[m] = True
    return lut


def occ_breakdown_str(z_256: np.ndarray, n_occ: np.ndarray) -> str:
    """
    占有数 0〜8 ごとの可視パターン数を [v0,v1,v2,v3,v4,v5,v6,v7,v8] 形式で返す
    """
    counts = [0] * 9
    for m in range(256):
        if z_256[m]:
            counts[n_occ[m]] += 1
    return f"[{','.join(str(c) for c in counts)}]"


def class_breakdown_str(z_36: np.ndarray, lut: Dict[str, Any]) -> str:
    """
    36クラス判定について、占有数 0〜8 ごとの可視クラス数を [c0,c1,...] 形式で返す
    """
    counts = [0] * 9
    for cid in range(36):
        if z_36[cid]:
            rep = lut['unique_reps'][cid]
            counts[lut['n_occ'][rep]] += 1
    return f"[{','.join(str(c) for c in counts)}]"


def build_256_lookup() -> Dict[str, Any]:
    """
    全256パターンの幾何学的属性・36回転クラスのLUTを構築
    """
    n_occ = np.zeros(256, dtype=np.int32)
    l_max = np.zeros(256, dtype=np.int32)
    rot_rep = np.zeros(256, dtype=np.int32)
    
    for m in range(256):
        n_occ[m] = bin(m).count('1')
        b = [(m >> i) & 1 for i in range(8)]
        if n_occ[m] == 0:
            l_max[m] = 8
        elif n_occ[m] == 8:
            l_max[m] = 0
        else:
            b_doubled = b + b
            max_z = 0
            cur_z = 0
            for v in b_doubled:
                if v == 0:
                    cur_z += 1
                    if cur_z > max_z:
                        max_z = cur_z
                else:
                    cur_z = 0
            l_max[m] = min(max_z, 8)
            
        min_shift = m
        cur = m
        for _ in range(7):
            cur = ((cur & 0x01) << 7) | (cur >> 1)
            if cur < min_shift:
                min_shift = cur
        rot_rep[m] = min_shift
        
    unique_reps = sorted(list(set(rot_rep)))
    rot_cid = np.zeros(256, dtype=np.int32)
    for m in range(256):
        rot_cid[m] = unique_reps.index(rot_rep[m])
        
    return {
        'n_occ': n_occ,
        'l_max': l_max,
        'rot_rep': rot_rep,
        'rot_cid': rot_cid,
        'unique_reps': unique_reps
    }


# 固定20候補の (R_th, L_th) 代表規則
RULES_20_CANDIDATES: List[Tuple[int, int]] = [
    (1, 8),
    (2, 3), (2, 4), (2, 5), (2, 8),
    (3, 2), (3, 3), (3, 4), (3, 8),
    (4, 1), (4, 2), (4, 3), (4, 8),
    (5, 1), (5, 2), (5, 8),
    (6, 1), (6, 8),
    (7, 8),
    (8, 8)
]
