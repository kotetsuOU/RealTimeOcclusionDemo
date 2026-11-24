/// <summary>
/// フレーム処理の間引き間隔（フレーム数）を外部から設定・取得するための共通インターフェース。
/// </summary>
public interface IRsProcessIntervalConfigurable
{
    void SetProcessIntervalFrames(int value);
    int GetProcessIntervalFrames();
}

