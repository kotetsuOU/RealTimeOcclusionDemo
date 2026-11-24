/// <summary>
/// 各ハプティクス生成ソースが個別に要求する変調 (Modulation) オーバーライド設定。
/// </summary>
[System.Serializable]
public class HapticsModulationOverride
{
    public bool enabled = false;
    public int priority = 0;
    public ModulationMode mode = ModulationMode.Sine;
    public float frequency = 150f;
}

