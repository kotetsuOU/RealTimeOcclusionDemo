using UnityEngine;

/// <summary>ハード無しで流れを確認するためのダミー出力（Console にだけ出す）。</summary>
public class AmbiguityLogOnlyHaptics : MonoBehaviour, IAmbiguityHaptics
{
    public void Present(AmbiguitySide side, int intensity) =>
        Debug.Log($"[AmbiguityHaptics:dummy] present side={side} intensity={intensity}");

    public void Stop() => Debug.Log("[AmbiguityHaptics:dummy] stop");
}
