"""
SICESI Python Suite: 共通定数・配色定義・環境設定
"""

import sys
import numpy as np

# Windows コンソールでの文字化け防止
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

# ==============================================================================
# 配色定義 (RGB) - 論文・可視化共通カラーパレット
# ==============================================================================
COLOR_CORRECT   = np.array([200, 200, 200], dtype=np.uint8)  # 正しく可視 (TP): 薄いグレー
COLOR_OCC_MATCH = np.array([45, 45, 45], dtype=np.uint8)     # 正しく遮蔽 (TN): 暗いグレー
COLOR_OVER_OCC  = np.array([255, 45, 45], dtype=np.uint8)    # 誤遮蔽 (FN: 過剰遮蔽): 赤色
COLOR_UNDER_OCC = np.array([30, 144, 255], dtype=np.uint8)   # 誤透過 (FP: 遮蔽漏れ): 青色
COLOR_BG        = np.array([15, 15, 15], dtype=np.uint8)      # 背景 (非対象領域): 漆黒

# ==============================================================================
# パイプライン一貫性のための二値化判定閾値
# 50% カバレッジ基準 (0〜255 の中央値 128) で完全統一
# ==============================================================================
DEFAULT_BINARY_THRESHOLD = 128
