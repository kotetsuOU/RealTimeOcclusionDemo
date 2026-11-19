// MultiAUTD3Controller に接続するアダプタ。
// MultiAUTD3Controller（OutputSide.Upper / Down を持つ版）があるプロジェクトで、
// Player Settings > Scripting Define Symbols に AMBIGUITY_MULTIAUTD3 を追加すると有効になる。
// （このブランチには MultiAUTD3Controller が無いため、既定ではコンパイル対象外。）
#if AMBIGUITY_MULTIAUTD3
using UnityEngine;

public class AmbiguityMultiAUTD3Haptics : MonoBehaviour, IAmbiguityHaptics
{
    [SerializeField] private MultiAUTD3Controller controller;

    private void Reset() => controller = FindFirstObjectByType<MultiAUTD3Controller>();

    public void Present(AmbiguitySide side, int intensity)
    {
        if (controller == null) return;
        intensity = Mathf.Clamp(intensity, 0, 255);

        // 強度 0 は実際に無音にする（Focus を 0 で出すのではなく停止）
        if (intensity == 0)
        {
            controller.StopOutput();
            return;
        }

        // 提示する側にだけ強度を設定し、SetMode で側を切り替える（もう一方は出力されない）
        if (side == AmbiguitySide.Upper)
        {
            controller.upperIntensity = intensity;
            controller.SetMode(MultiAUTD3Controller.OutputSide.Upper);
        }
        else
        {
            controller.downIntensity = intensity;
            controller.SetMode(MultiAUTD3Controller.OutputSide.Down);
        }
    }

    public void Stop()
    {
        if (controller != null) controller.StopOutput();
    }
}
#endif
