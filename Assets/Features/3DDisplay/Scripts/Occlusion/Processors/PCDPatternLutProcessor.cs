using System;
using System.Collections.Generic;
using UnityEngine;
using static PCDRendererFeature;

/// <summary>
/// 256bit パターン LUT（Mode 8 / 20 / 36 / 256）および密度適応型 LUT テーブルの構築・キャッシュを担う Pure C# プロセッサ。
/// 通常フレームではパラメータ変化時のみ再計算を行い、GetActiveLutWords / GetDensityLutData の GC Alloc をゼロに抑えます。
/// </summary>
public sealed class PCDPatternLutProcessor
{
    #region 定数・事前定義 LUT

    /// <summary> 256パターン独立最適LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_256 = "000BC04F0000018B0003000D8001908D000100010001000080000001D000DD45";

    /// <summary> 36クラス幾何学回転不変LUTの最良解 (HEX: 64文字) </summary>
    public const string DEFAULT_HEX_36 = "130B008B0000808B000000008000808B5101000100000001D1010001D101D1DF";

    /// <summary> GPU 定数バッファに転送可能な密度ビンの最大数 </summary>
    public const int MAX_DENSITY_LUT_BINS = 8;

    /// <summary> Mode 256 最適解の int[8] ワード表現 (GC Alloc 回避用キャッシュ) </summary>
    private static readonly int[] LUT_WORDS_256_DEFAULT_INT = new int[8]
    {
        unchecked((int)0xD000DD45u), unchecked((int)0x80000001u), unchecked((int)0x00010000u), unchecked((int)0x00010001u),
        unchecked((int)0x8001908Du), unchecked((int)0x0003000Du), unchecked((int)0x0000018Bu), unchecked((int)0x000BC04Fu)
    };

    /// <summary> Mode 36 最適解の int[8] ワード表現 (GC Alloc 回避用キャッシュ) </summary>
    private static readonly int[] LUT_WORDS_36_DEFAULT_INT = new int[8]
    {
        unchecked((int)0xD101D1DFu), unchecked((int)0xD1010001u), unchecked((int)0x00000001u), unchecked((int)0x51010001u),
        unchecked((int)0x8000808Bu), unchecked((int)0x00000000u), unchecked((int)0x0000808Bu), unchecked((int)0x130B008Bu)
    };

    #endregion

    private readonly uint[] _tempUintWords = new uint[8];
    private readonly int[] _cachedLutWords = new int[8];
    private readonly Vector4[] _cachedDensityThresholds = new Vector4[2];
    private readonly int[] _cachedDensityLutWords = new int[MAX_DENSITY_LUT_BINS * 8];
    private readonly List<PCDOcclusionPipelineController.PCD_DensityLutEntry> _sortedDensityScratch =
        new List<PCDOcclusionPipelineController.PCD_DensityLutEntry>(MAX_DENSITY_LUT_BINS);

    private int _cachedDensityLutCount = 0;
    private bool _isLutDirty = true;
    private bool _isDensityLutDirty = true;
    private PCD_OcclusionEvaluationMode _lastEvaluationMode = (PCD_OcclusionEvaluationMode)(-1);
    private int _lastMin8 = -1;
    private int _lastMin20 = -1;
    private int _lastMaxEmpty = -1;
    private string _lastCustomHex = null;

    /// <summary>
    /// 既定の 3 段階密度別 LUT 設定（Sparse / Medium / Dense）を生成します。
    /// </summary>
    public static List<PCDOcclusionPipelineController.PCD_DensityLutEntry> CreateDefaultDensityLutEntries()
    {
        return new List<PCDOcclusionPipelineController.PCD_DensityLutEntry>
        {
            new PCDOcclusionPipelineController.PCD_DensityLutEntry
            {
                label = "Sparse (0.25 pts/mm²)",
                sourcePhysicalDensity = 0.25f,
                maxScreenDensity = 0.08f,
                patternLutHex = "033B3FAB400380A802008000E0E8808400010101C0008008C0008000C0008001"
            },
            new PCDOcclusionPipelineController.PCD_DensityLutEntry
            {
                label = "Medium (1.0 pts/mm²)",
                sourcePhysicalDensity = 1.0f,
                maxScreenDensity = 0.28f,
                patternLutHex = "3FFFFFFFCADAFBFBD3B3E29BD709D98BD77FF3FBC0C5F31BD59BDF5FFB97FF7F"
            },
            new PCDOcclusionPipelineController.PCD_DensityLutEntry
            {
                label = "Dense (4.0+ pts/mm²)",
                sourcePhysicalDensity = 4.0f,
                maxScreenDensity = 1.00f,
                patternLutHex = "7FFFFFFFFFFFFFFFF7BFF3DFFFFBFFFFFFFFFFFFFFF7FFFFFFFFF7FFFFFFFFFF"
            }
        };
    }

    /// <summary>
    /// LUT の再計算フラグを立てます。設定パラメータを変更した際に呼び出してください。
    /// </summary>
    public void MarkDirty()
    {
        _isLutDirty = true;
        _isDensityLutDirty = true;
    }

    /// <summary>
    /// 現在の評価モードおよび設定に応じた 256bit LUT の int[8] ワード配列を取得します。
    /// パラメータが変化していない通常フレームではキャッシュ配列を返します。
    /// </summary>
    public int[] GetActiveLutWords(
        PCD_OcclusionEvaluationMode evaluationMode,
        int minOccludedSectorsMode8,
        int minOccludedSectorsMode20,
        int maxConsecutiveEmptySectors,
        string customPatternLutHex)
    {
        bool settingsChanged = _isLutDirty ||
                               _lastEvaluationMode != evaluationMode ||
                               _lastMin8 != minOccludedSectorsMode8 ||
                               _lastMin20 != minOccludedSectorsMode20 ||
                               _lastMaxEmpty != maxConsecutiveEmptySectors ||
                               _lastCustomHex != customPatternLutHex;

        if (!settingsChanged)
        {
            return _cachedLutWords;
        }

        _isLutDirty = false;
        _isDensityLutDirty = true;
        _lastEvaluationMode = evaluationMode;
        _lastMin8 = minOccludedSectorsMode8;
        _lastMin20 = minOccludedSectorsMode20;
        _lastMaxEmpty = maxConsecutiveEmptySectors;
        _lastCustomHex = customPatternLutHex;

        Array.Clear(_tempUintWords, 0, 8);

        if (evaluationMode == PCD_OcclusionEvaluationMode.PatternLUT256)
        {
            if (!string.IsNullOrEmpty(customPatternLutHex) && TryParseHexToWords(customPatternLutHex, _tempUintWords))
            {
                CopyToIntArray(_tempUintWords, _cachedLutWords);
                return _cachedLutWords;
            }
            Array.Copy(LUT_WORDS_256_DEFAULT_INT, _cachedLutWords, 8);
            return _cachedLutWords;
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.RotationLUT36)
        {
            if (!string.IsNullOrEmpty(customPatternLutHex) && TryParseHexToWords(customPatternLutHex, _tempUintWords))
            {
                CopyToIntArray(_tempUintWords, _cachedLutWords);
                return _cachedLutWords;
            }
            Array.Copy(LUT_WORDS_36_DEFAULT_INT, _cachedLutWords, 8);
            return _cachedLutWords;
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.SectorThreshold) // Mode 8
        {
            for (int m = 0; m < 256; m++)
            {
                int occ = CountBits((byte)m);
                if (occ < minOccludedSectorsMode8)
                {
                    _tempUintWords[m >> 5] |= (1u << (m & 31));
                }
            }
            CopyToIntArray(_tempUintWords, _cachedLutWords);
            return _cachedLutWords;
        }
        else if (evaluationMode == PCD_OcclusionEvaluationMode.SectorConsecutiveZeros) // Mode 20
        {
            for (int m = 0; m < 256; m++)
            {
                int occ = CountBits((byte)m);
                int lMax = ComputeLMax((byte)m);
                bool isOccluded = (occ >= minOccludedSectorsMode20) && (lMax <= maxConsecutiveEmptySectors);
                if (!isOccluded)
                {
                    _tempUintWords[m >> 5] |= (1u << (m & 31));
                }
            }
            CopyToIntArray(_tempUintWords, _cachedLutWords);
            return _cachedLutWords;
        }

        // デフォルト (全可視)
        for (int i = 0; i < 8; i++)
        {
            _cachedLutWords[i] = unchecked((int)0xFFFFFFFFu);
        }
        return _cachedLutWords;
    }

    /// <summary>
    /// 密度適応型 LUT 用のビン数・上限閾値配列・LUT ワード配列を取得します。
    /// </summary>
    public void GetDensityLutData(
        bool useDensityAdaptiveLut,
        List<PCDOcclusionPipelineController.PCD_DensityLutEntry> entries,
        PCD_OcclusionEvaluationMode evaluationMode,
        int minOccludedSectorsMode8,
        int minOccludedSectorsMode20,
        int maxConsecutiveEmptySectors,
        string customPatternLutHex,
        out int count,
        out Vector4[] thresholds,
        out int[] words)
    {
        if (!useDensityAdaptiveLut || entries == null || entries.Count == 0)
        {
            count = 0;
            thresholds = _cachedDensityThresholds;
            words = _cachedDensityLutWords;
            return;
        }

        // 評価モードまたはベース LUT が変わっていたら密度テーブルも再構築
        int[] baseLut = GetActiveLutWords(
            evaluationMode,
            minOccludedSectorsMode8,
            minOccludedSectorsMode20,
            maxConsecutiveEmptySectors,
            customPatternLutHex);

        if (_isDensityLutDirty)
        {
            _isDensityLutDirty = false;
            _sortedDensityScratch.Clear();
            int rawCount = entries.Count;
            for (int i = 0; i < rawCount; i++)
            {
                var entry = entries[i];
                if (entry != null) _sortedDensityScratch.Add(entry);
            }

            _sortedDensityScratch.Sort((a, b) => a.maxScreenDensity.CompareTo(b.maxScreenDensity));
            int binCount = Mathf.Min(_sortedDensityScratch.Count, MAX_DENSITY_LUT_BINS);
            _cachedDensityLutCount = binCount;

            _cachedDensityThresholds[0] = Vector4.zero;
            _cachedDensityThresholds[1] = Vector4.zero;
            Array.Clear(_cachedDensityLutWords, 0, _cachedDensityLutWords.Length);

            for (int b = 0; b < binCount; b++)
            {
                var entry = _sortedDensityScratch[b];
                if (b < 4)
                {
                    var v = _cachedDensityThresholds[0];
                    v[b] = entry.maxScreenDensity;
                    _cachedDensityThresholds[0] = v;
                }
                else
                {
                    var v = _cachedDensityThresholds[1];
                    v[b - 4] = entry.maxScreenDensity;
                    _cachedDensityThresholds[1] = v;
                }

                int dstOffset = b * 8;
                if (!string.IsNullOrEmpty(entry.patternLutHex) && TryParseHexToWords(entry.patternLutHex, _tempUintWords))
                {
                    for (int w = 0; w < 8; w++)
                    {
                        _cachedDensityLutWords[dstOffset + w] = unchecked((int)_tempUintWords[w]);
                    }
                }
                else
                {
                    Array.Copy(baseLut, 0, _cachedDensityLutWords, dstOffset, 8);
                }
            }
        }

        count = _cachedDensityLutCount;
        thresholds = _cachedDensityThresholds;
        words = _cachedDensityLutWords;
    }

    private static void CopyToIntArray(uint[] source, int[] destination)
    {
        for (int i = 0; i < 8; i++)
        {
            destination[i] = unchecked((int)source[i]);
        }
    }

    private static bool TryParseHexToWords(string hex, uint[] outWords)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        string clean = hex.Trim().Replace("0x", "").Replace("0X", "").Replace(" ", "");
        if (clean.Length < 64) clean = clean.PadLeft(64, '0');
        if (clean.Length > 64) clean = clean.Substring(clean.Length - 64, 64);

        try
        {
            // clean は上位ビットが先頭 (MSB=clean[0..7] -> word 7, LSB=clean[56..63] -> word 0)
            for (int w = 0; w < 8; w++)
            {
                int start = 64 - (w + 1) * 8;
                string sub = clean.Substring(start, 8);
                outWords[w] = Convert.ToUInt32(sub, 16);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int CountBits(byte b)
    {
        int count = 0;
        while (b != 0)
        {
            b = (byte)(b & (b - 1));
            count++;
        }
        return count;
    }

    private static int ComputeLMax(byte m)
    {
        if (m == 0) return 8;
        if (m == 255) return 0;
        int maxZero = 0;
        int curZero = 0;
        for (int i = 0; i < 16; i++)
        {
            int bit = (m >> (i % 8)) & 1;
            if (bit == 0)
            {
                curZero++;
                if (curZero > maxZero) maxZero = curZero;
            }
            else
            {
                curZero = 0;
            }
        }
        return Mathf.Min(maxZero, 8);
    }
}

