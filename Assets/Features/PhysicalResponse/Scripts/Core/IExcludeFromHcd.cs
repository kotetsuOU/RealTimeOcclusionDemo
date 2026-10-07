namespace Features.PhysicalResponse
{
    /// <summary>
    /// PR_VirtualObjectManager による HCD (触覚判定) への自動登録・同期を除外するオブジェクトに付与するマーカーインターフェース。
    /// 天候演出 (Weather) や背景・演出専用オブジェクトなど、実体としてのメッシュ接触判定を持たない仮想オブジェクトに適用します。
    /// </summary>
    public interface IExcludeFromHcd
    {
    }
}
