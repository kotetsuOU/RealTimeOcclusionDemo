"""
SICESI Python Suite: データセット探索・画像解決・pattern_counts_256 & pattern_VO 集計モジュール
"""

import os
import glob
import re
import csv
import json
import time
from typing import List, Dict, Any, Optional, Tuple
import numpy as np
from PIL import Image


def natural_sort_key(s: str) -> List[Any]:
    """文字列中の数値を数値として扱い自然順 (sector_0, sector_1, ..., sector_8) でソート"""
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'(\d+)', s)]


def find_first_image(folder_path: str, is_gt: bool = False) -> Optional[str]:
    """
    評価対象画像を探索します。
    diff_*, gpu_*, sector_*, vo_* などの派生・マスク画像は自動的に除外し、
    GTフォルダなら gt_*, テストフォルダなら test_* を優先的に検出します。
    """
    for ext in ("*.png", "*.bmp", "*.jpg"):
        files = glob.glob(os.path.join(folder_path, ext))
        if not files:
            continue
        valid_files = [
            f for f in files
            if not any(os.path.basename(f).startswith(p) for p in ("diff_", "gpu_", "sector_", "vo_"))
        ]
        if not valid_files:
            continue
        pref = "gt_" if is_gt else "test_"
        matched = [f for f in valid_files if os.path.basename(f).lower().startswith(pref)]
        if matched:
            return sorted(matched)[0]
        return sorted(valid_files)[0]
    return None


def resolve_gt_dir(test_dir_abs: str, base_dataset_dir: str) -> Optional[str]:
    """
    テストフォルダに対して最も適切な GT ディレクトリを階層的に探索・解決します。
    """
    curr = os.path.abspath(test_dir_abs)
    while len(curr) >= len(os.path.abspath(base_dataset_dir)):
        gt_candidate = os.path.join(curr, "GT")
        if os.path.isdir(gt_candidate):
            return gt_candidate
        parent = os.path.dirname(curr)
        if parent == curr:
            break
        curr = parent

    default_gt = os.path.join(base_dataset_dir, "GT")
    if os.path.isdir(default_gt):
        return default_gt

    parent_gt = os.path.join(os.path.dirname(base_dataset_dir), "GT")
    if os.path.isdir(parent_gt):
        return parent_gt

    recursive_gts = glob.glob(os.path.join(base_dataset_dir, "**", "GT"), recursive=True)
    if recursive_gts:
        return sorted(recursive_gts, key=len)[0]

    return None


def parse_condition_metadata(cond_str: str, test_dir_abs: Optional[str] = None) -> Dict[str, Any]:
    """パス文字列および evaluation_params.json から実験パラメータを抽出"""
    meta = {
        "Fixed_Mode": "",
        "Density_Value": "",
        "Density_Unit": "",
        "Sector": "",
        "Max_Consecutive_Zeros": "",
        "Threshold": ""
    }

    if test_dir_abs:
        json_path = os.path.join(test_dir_abs, "evaluation_params.json")
        if os.path.isfile(json_path):
            try:
                with open(json_path, "r", encoding="utf-8") as jf:
                    data = json.load(jf)
                    meta["Density_Value"] = float(data.get("densityValue", 0.0))
                    meta["Density_Unit"] = str(data.get("densityUnit", ""))
                    meta["Threshold"] = float(data.get("occlusionThreshold", 0.0))
                    meta["Fixed_Mode"] = str(data.get("evaluationMode", ""))
                    meta["Sector"] = str(data.get("minOccludedSectors", ""))
                    meta["Max_Consecutive_Zeros"] = str(data.get("maxConsecutiveEmptySectors", ""))
                    return meta
            except Exception:
                pass

    m_mode = re.search(r"Proposed_([A-Za-z0-9]+)", cond_str)
    if m_mode:
        meta["Fixed_Mode"] = m_mode.group(1)

    m_dens = re.search(r"density_([0-9\.]+)(mm|cm|pts_mm2)?", cond_str)
    if m_dens:
        meta["Density_Value"] = float(m_dens.group(1))
        meta["Density_Unit"] = m_dens.group(2) if m_dens.group(2) else ""

    m_sec = re.search(r"sector_([0-9]+)", cond_str)
    if m_sec:
        meta["Sector"] = int(m_sec.group(1))
    elif "Average" in cond_str:
        meta["Sector"] = "Average"

    m_consec = re.search(r"maxConsecutiveZeros_([0-9]+)", cond_str)
    if m_consec:
        meta["Max_Consecutive_Zeros"] = int(m_consec.group(1))

    m_th = re.search(r"th_([0-9\.]+)", cond_str)
    if m_th:
        meta["Threshold"] = float(m_th.group(1))

    return meta


def auto_generate_missing_pattern_counts(root_dir: str, lut: Dict[str, Any], force_regenerate: bool = False):
    """
    root_dir 配下の全撮影ディレクトリ (Sector8MaskSweep / Pattern256MaskSweep) を走査し、
    pattern_counts_256.csv が未生成または force_regenerate=True の場合に高速自動集計する。
    """
    sweep_dirs = sorted(glob.glob(os.path.join(root_dir, "**", "Sector8MaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "Pattern256MaskSweep"), recursive=True))
    sweep_dirs += sorted(glob.glob(os.path.join(root_dir, "**", "SectorMaskSweep"), recursive=True))
    sweep_dirs = sorted(list(set(sweep_dirs)))
    
    if not sweep_dirs:
        return
        
    missing_targets = []
    for s_dir in sweep_dirs:
        density_dirs = sorted(glob.glob(os.path.join(s_dir, "density_*pts_mm2")))
        for d_dir in density_dirs:
            for eye in ["Left", "Right"]:
                eye_dir = os.path.join(d_dir, eye)
                if not os.path.exists(eye_dir):
                    continue
                p1 = os.path.join(eye_dir, "Oracle256Analysis", "pattern_counts_256.csv")
                p2 = os.path.join(eye_dir, "pattern_counts_256.csv")
                if force_regenerate or not (os.path.exists(p1) or os.path.exists(p2)):
                    missing_targets.append((s_dir, d_dir, eye, eye_dir))
                    
    if not missing_targets:
        return
        
    print(f"[*] 集計対象の撮影データ ({len(missing_targets)} 件) を検出しました。超高速自動集計中...")
    generated_count = 0
    t0 = time.time()
    
    for s_dir, d_dir, eye, eye_dir in missing_targets:
        gt_candidates = [
            os.path.join(s_dir, "GT", eye, f"gt_{eye.lower()}.png"),
            os.path.join(s_dir, "GT", f"gt_{eye.lower()}.png"),
            os.path.join(s_dir, "..", "GT", eye, f"gt_{eye.lower()}.png"),
            os.path.join(s_dir, "..", "GT", f"gt_{eye.lower()}.png"),
        ]
        gt_path = next((c for c in gt_candidates if os.path.exists(c)), None)
        if not gt_path:
            continue
            
        vo_candidates = [
            os.path.join(s_dir, "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
            os.path.join(s_dir, "GT", f"vo_silhouette_{eye.lower()}.png"),
            os.path.join(s_dir, "..", "GT", eye, f"vo_silhouette_{eye.lower()}.png"),
            os.path.join(s_dir, "..", "GT", f"vo_silhouette_{eye.lower()}.png"),
        ]
        vo_path = next((c for c in vo_candidates if os.path.exists(c)), None)
        
        gt_img = np.array(Image.open(gt_path))[:, :, 0]
        H, W = gt_img.shape
        gt_mask = (gt_img > 128)

        # 直接投影画素 (point_mask) の取得: 手前点群画素は確定遮蔽 (Fixed Classification) とし除外
        pt_candidates = [
            os.path.join(eye_dir, f"point_mask_{eye.lower()}.png"),
            os.path.join(eye_dir, "point_mask.png")
        ]
        pt_path = next((c for c in pt_candidates if os.path.exists(c)), None)
        pt_mask = (np.array(Image.open(pt_path))[:, :, 0] > 128) if (pt_path and os.path.exists(pt_path)) else np.zeros((H, W), dtype=bool)
        vo_mask = (np.array(Image.open(vo_path))[:, :, 0] > 128) if (vo_path and os.path.exists(vo_path)) else np.ones((H, W), dtype=bool)

        target_mask = vo_mask & (~pt_mask)
        
        sector_pngs = sorted(glob.glob(os.path.join(eye_dir, "sector_*_mask_*.png")))
        occupied_mask = None
        is_evaluated = None
        
        # 1. 8セクター二値マスク画面保存
        if len(sector_pngs) >= 8:
            sector_map = {}
            for p in sector_pngs:
                fname = os.path.basename(p)
                m_match = re.search(r"sector_(\d+)_mask", fname)
                if m_match:
                    sec_id = int(m_match.group(1))
                    if 0 <= sec_id < 8:
                        sector_map[sec_id] = p
                        
            if len(sector_map) == 8:
                occupied_mask = np.zeros((H, W), dtype=np.uint8)
                for sec_id in range(8):
                    sec_img = np.array(Image.open(sector_map[sec_id]))[:, :, 0]
                    sec_bit = (sec_img > 128)
                    occupied_mask |= (sec_bit.astype(np.uint8) << sec_id)
                is_evaluated = target_mask

        # 2. 全8セクター統合 8-bit 占有パターンマスク
        if occupied_mask is None:
            unified_candidates = [
                os.path.join(eye_dir, f"sector_mask_{eye.lower()}.png"),
                os.path.join(eye_dir, "sector_mask.png")
            ]
            unified_path = next((c for c in unified_candidates if os.path.exists(c)), None)
            if unified_path:
                occupied_mask = np.array(Image.open(unified_path))[:, :, 0]
                is_evaluated = target_mask

        if occupied_mask is None:
            continue
            
        eval_mask = is_evaluated & target_mask
        eval_patterns = occupied_mask[eval_mask]
        eval_gt_vis = gt_mask[eval_mask]
        
        p_bins_V = np.bincount(eval_patterns[eval_gt_vis], minlength=256)
        p_bins_O = np.bincount(eval_patterns[~eval_gt_vis], minlength=256)
        
        counts_csv_path = os.path.join(eye_dir, "pattern_counts_256.csv")
        with open(counts_csv_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.writer(f)
            writer.writerow(["mask", "bits_b7_to_b0", "occupied_count", "gt_visible_count", "gt_occluded_count", "total_count"])
            for m in range(256):
                v_cnt = int(p_bins_V[m])
                o_cnt = int(p_bins_O[m])
                t_cnt = v_cnt + o_cnt
                b_str = bin(m)[2:].zfill(8)
                writer.writerow([m, b_str, lut['n_occ'][m], v_cnt, o_cnt, t_cnt])
                
        generated_count += 1
        
    elapsed = time.time() - t0
    if generated_count > 0:
        print(f"[+] {generated_count} 件の pattern_counts_256.csv を自動集計・保存しました ({elapsed:.2f}秒)。\n")


def load_dataset(root_dir: str, lut: Dict[str, Any], force_regenerate: bool = False) -> List[Dict[str, Any]]:
    """
    root_dir 配下から pattern_counts_256.csv を再帰検索し、検証して読み込む。
    """
    auto_generate_missing_pattern_counts(root_dir, lut, force_regenerate=force_regenerate)
    
    csv_paths = sorted(glob.glob(os.path.join(root_dir, "**", "pattern_counts_256.csv"), recursive=True))
    if not csv_paths:
        print(f"[!] pattern_counts_256.csv が見つかりませんでした: {root_dir}")
        return []
        
    dataset = []
    print(f"[*] {len(csv_paths)} 件の pattern_counts_256.csv を検出しました。検証中...")
    
    for path in csv_paths:
        norm_path = os.path.normpath(path)
        parts = norm_path.split(os.sep)
        
        case_name = "UnknownCase"
        density_str = "UnknownDensity"
        eye = "UnknownEye"
        
        for idx, p in enumerate(parts):
            if p in ["Sector8MaskSweep", "Pattern256MaskSweep", "SectorMaskSweep"] and idx > 0:
                parent = parts[idx - 1]
                if idx > 1 and "RawTest" in parts[idx - 2]:
                    case_name = f"{parts[idx - 2]}_case{parent}" if parent.isdigit() else f"{parts[idx - 2]}_{parent}"
                elif parent.isdigit():
                    case_name = f"Case_{parent}"
                else:
                    case_name = parent
            elif p.startswith("density_") and "pts_mm2" in p:
                density_str = p.replace("density_", "").replace("pts_mm2", "")
            elif p in ["Left", "Right"]:
                eye = p
                
        data_id = f"{case_name}_{density_str}_{eye}"
        
        # 密度 16.0 は上限クランプにより 4.0 と重複していたため除外
        if density_str == "16.0":
            continue

        rows = []
        with open(path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            rows = list(reader)
            
        if len(rows) != 256:
            continue
            
        V = np.zeros(256, dtype=np.int64)
        O = np.zeros(256, dtype=np.int64)
        
        valid = True
        for r in rows:
            m = int(r['mask'])
            v = int(r['gt_visible_count'])
            o = int(r['gt_occluded_count'])
            if v < 0 or o < 0:
                valid = False
                break
            V[m] = v
            O[m] = o
            
        if not valid:
            continue
            
        G = int(np.sum(V))
        if G == 0:
            continue
            
        total_eval_pixels = int(np.sum(V) + np.sum(O))
        no_occ_iou = G / total_eval_pixels if total_eval_pixels > 0 else 0.0
        
        dataset.append({
            'data_id': data_id,
            'case': case_name,
            'density': float(density_str) if density_str != "UnknownDensity" else 0.0,
            'density_str': density_str,
            'eye': eye,
            'file_path': path,
            'V': V,
            'O': O,
            'G': G,
            'total_eval_pixels': total_eval_pixels,
            'no_occ_iou': no_occ_iou
        })
        
    print(f"[+] {len(dataset)} 件の有効なデータセットを読み込み・検証しました。\n")
    return dataset


def save_pattern_vo_csv(dataset: List[Dict[str, Any]], output_dirs: List[str]):
    """
    全データセットの counts_V_256, counts_O_256 を合算し、pattern_VO.csv を保存する。
    """
    counts_V_256 = np.array([d['V'] for d in dataset], dtype=np.int64)
    counts_O_256 = np.array([d['O'] for d in dataset], dtype=np.int64)
    tot_V_all = np.sum(counts_V_256, axis=0)
    tot_O_all = np.sum(counts_O_256, axis=0)
    vo_rows = []
    for m in range(256):
        vo_rows.append({'mask': m, 'V': int(tot_V_all[m]), 'O': int(tot_O_all[m])})

    for out_dir in output_dirs:
        p_vo = os.path.join(out_dir, "pattern_VO.csv") if not out_dir.endswith(".csv") else out_dir
        os.makedirs(os.path.dirname(p_vo), exist_ok=True)
        with open(p_vo, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=['mask', 'V', 'O'])
            writer.writeheader()
            writer.writerows(vo_rows)
        print(f"[+] pattern_VO.csv を保存しました: {p_vo}")
