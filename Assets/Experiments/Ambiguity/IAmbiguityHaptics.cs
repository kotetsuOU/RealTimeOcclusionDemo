/// <summary>AmbiguityTask が使う触覚出力の窓口。AUTD の実装詳細を AmbiguityTask から切り離す。</summary>
public interface IAmbiguityHaptics
{
    /// <summary>指定側にだけ指定強度（0..255）を出す。もう一方は出さない。強度 0 は無音。</summary>
    void Present(AmbiguitySide side, int intensity);

    /// <summary>両側とも出力を止める。</summary>
    void Stop();
}
