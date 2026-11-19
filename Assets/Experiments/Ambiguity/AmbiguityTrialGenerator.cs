using System;
using System.Collections.Generic;

/// <summary>触覚定位の曖昧域実験：提示側（手のひら側 / 手の甲側）。</summary>
public enum AmbiguitySide
{
    Upper, // 手のひら側（上AUTD）
    Down   // 手の甲側（下AUTD）
}

/// <summary>1試行分のデータ（生成時に確定。実行時に引き直さない）。</summary>
[Serializable]
public struct AmbiguityTrial
{
    public AmbiguitySide side;
    public int level;           // 0..250（ログ用。固定側では解析に使わない）
    public int outputIntensity; // 実際に出力する強度 0..255
    public bool visual;
    public float visualDelayMs; // 視覚なしの試行では 0
}

/// <summary>
/// 試行リスト生成と強度ルール。Unity / AUTD に依存しない純ロジック。
/// </summary>
public static class AmbiguityTrialGenerator
{
    public const int LevelStep = 10;
    public const int LevelMax = 250;
    public const int Repetitions = 2;

    /// <summary>パターン1〜3の強度ルール（仕様 3.2）。0〜255 にクランプする。</summary>
    public static int GetOutputIntensity(int pattern, AmbiguitySide side, int level, int fixedValue)
    {
        int v;
        switch (pattern)
        {
            case 1: v = level; break;
            case 2: v = side == AmbiguitySide.Upper ? fixedValue : level; break; // 手のひら固定
            case 3: v = side == AmbiguitySide.Down ? fixedValue : level; break;  // 甲固定
            default: throw new ArgumentOutOfRangeException(nameof(pattern), "pattern must be 1..3");
        }
        return Math.Max(0, Math.Min(255, v));
    }

    /// <summary>被験者ラベルとパターンから決まる再現可能なシード（FNV-1a。string.GetHashCode は実行ごとに変わるため使わない）。</summary>
    public static int DeriveSeed(string participant, int pattern)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in (participant ?? "") + "|" + pattern)
            {
                h ^= c;
                h *= 16777619;
            }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    public static List<AmbiguityTrial> Generate(
        int pattern, int fixedValue, int seed, float visualDelayMinMs, float visualDelayMaxMs)
    {
        var rng = new Random(seed);
        var list = new List<AmbiguityTrial>();

        var sides = new[] { AmbiguitySide.Upper, AmbiguitySide.Down };
        foreach (var side in sides)
            for (int level = 0; level <= LevelMax; level += LevelStep)
                for (int vis = 0; vis < 2; vis++)
                    for (int rep = 0; rep < Repetitions; rep++)
                    {
                        bool visual = vis == 1;
                        list.Add(new AmbiguityTrial
                        {
                            side = side,
                            level = level,
                            outputIntensity = GetOutputIntensity(pattern, side, level, fixedValue),
                            visual = visual,
                            visualDelayMs = visual
                                ? visualDelayMinMs + (float)rng.NextDouble() * (visualDelayMaxMs - visualDelayMinMs)
                                : 0f
                        });
                    }

        Shuffle(list, rng);
        return list;
    }

    // Fisher-Yates
    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>仕様 10章の生成まわりの確認。問題があれば説明文を返し、なければ null。</summary>
    public static string Validate(IReadOnlyList<AmbiguityTrial> trials, int pattern, int fixedValue)
    {
        int expected = 2 * (LevelMax / LevelStep + 1) * 2 * Repetitions;
        if (trials.Count != expected) return $"試行数 {trials.Count} != {expected}";

        var counts = new Dictionary<(AmbiguitySide, int, bool), int>();
        foreach (var t in trials)
        {
            var key = (t.side, t.level, t.visual);
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

            int want = GetOutputIntensity(pattern, t.side, t.level, fixedValue);
            if (t.outputIntensity != want) return $"強度不一致 side={t.side} level={t.level}";
            if (t.level == 0 && pattern == 1 && t.outputIntensity != 0) return "level=0 が無音でない";
        }
        if (counts.Count != 2 * (LevelMax / LevelStep + 1) * 2) return $"セル数 {counts.Count}";
        foreach (var kv in counts)
            if (kv.Value != Repetitions) return $"セル {kv.Key} が {kv.Value} 回";
        return null;
    }
}
