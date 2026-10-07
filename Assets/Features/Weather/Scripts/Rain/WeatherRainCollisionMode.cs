namespace Features.Weather
{
    /// <summary>
    /// 雨粒の地面・環境衝突判定モード。
    /// </summary>
    public enum WeatherRainCollisionMode
    {
        /// <summary>
        /// 衝突判定なし。雨粒は寿命計算 (cloudY - groundY) / speed で自然消滅します（最も軽量）。
        /// </summary>
        None,

        /// <summary>
        /// 指定した groundY の仮想水平面との衝突判定を行い、着地時に飛沫 (Splash) を生成します。
        /// </summary>
        Plane,

        /// <summary>
        /// 空間内の Collider (床・家具メッシュ等) との 3D 衝突判定を行い、接触面で飛沫を生成します。
        /// </summary>
        World
    }
}
