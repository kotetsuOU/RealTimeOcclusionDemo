using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Weather
{
    /// <summary>
    /// 雨シミュレーション・パーティクル描画に関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherRainSettings
    {
        [Tooltip("雨粒・飛沫用の描画マテリアル (M_Weather_Rain)")]
        public Material material;

        [Header("Fall Speed & Simulation")]
        [Tooltip("雨粒の最小落下速度 (m/s)")]
        public float fallSpeedMin = 7.0f;

        [Tooltip("雨粒の最大落下速度 (m/s)")]
        public float fallSpeedMax = 9.0f;

        [Header("Drop Appearance")]
        [Tooltip("雨粒の基本サイズ・幅 (m) (卓上SRDisplay推奨: 0.001〜0.003)")]
        [Range(0.0005f, 0.015f)]
        public float dropSize = 0.002f;

        [Tooltip("雨粒の落下方向への引き伸ばし倍率 (Stretch Length Scale)")]
        [Range(0.5f, 5.0f)]
        public float lengthScale = 1.8f;

        [Header("Emission & Bounds")]
        [Tooltip("雨の発生面の幅 (X) と奥行き (Z) (m)")]
        public Vector2 areaSize = new Vector2(6.0f, 6.0f);

        [Tooltip("雨の発生面の中心オフセット (X, Z) (m)")]
        public Vector2 areaCenter = Vector2.zero;

        [Tooltip("General の共有 Bounds を上書きして独自の X, Z 平面設定を使用するか")]
        public bool useCustomBounds = false;

        [Tooltip("最大降雨時の毎秒放出パーティクル数")]
        public float maxRate = 2500f;

        [Tooltip("雨粒の衝突消滅・飛沫生成モード")]
        public WeatherRainCollisionMode collisionMode = WeatherRainCollisionMode.Plane;

        [Tooltip("World衝突判定時の対象レイヤー (床・家具など)")]
        public LayerMask environmentMask = ~0;

        // 互換性プロパティ (旧 area)
        public float area
        {
            get => areaSize.x;
            set => areaSize = new Vector2(value, value);
        }
    }

    /// <summary>
    /// 雲（Cloud）の描画・不透明PCDレイヤー・激しさに応じたグラデーションに関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherCloudSettings
    {
        [Tooltip("雲の表示を有効にするか (True: 表示 / False: 非表示)")]
        public bool enableClouds = true;

        [Tooltip("雲用描画マテリアル (M_Weather_Cloud。URP Opaque Lit 推奨)")]
        public Material material;

        [Header("Appearance & Color Gradient")]
        [Tooltip("雨の激しさ 0 (穏やか・晴れ/低降雨) での雲の色 (明るい白・シルバー)")]
        public Color lightCloudColor = new Color(0.92f, 0.94f, 0.98f, 1.0f);

        [Tooltip("雨の激しさ 1 (激しい嵐・豪雨) での雲の色 (重厚な濃い暗黒灰色・雷雲)")]
        public Color stormCloudColor = new Color(0.18f, 0.20f, 0.24f, 1.0f);

        [Header("Scale & Coverage")]
        [Tooltip("雨強度 0 での雲のスケール倍率 (最小サイズ。卓上SRDisplay等の0.1〜0.2m極小設定にも対応)")]
        [Range(0.01f, 1.5f)]
        public float minScale = 0.2f;

        [Tooltip("雨強度 1 での雲のスケール倍率 (最大膨張・空を覆う)")]
        [Range(0.02f, 3.0f)]
        public float maxScale = 0.6f;

        [Tooltip("雲クラスタの配置個数")]
        [Range(3, 16)]
        public int clusterCount = 7;

        [Tooltip("各クラスタ内のパフ (低ポリ球体塊) の数")]
        [Range(3, 10)]
        public int puffsPerCluster = 6;

        [Tooltip("雲の基本厚み (Y方向の高さ) (m)")]
        public float cloudThickness = 0.15f;

        [Header("Drift Animation")]
        [Tooltip("雲のゆっくりとした揺らぎ・漂い速度 (0: 静止)")]
        public float driftSpeed = 0.05f;
    }

    /// <summary>
    /// 稲妻ジオメトリ・落雷シーケンスに関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherLightningSettings
    {
        [Tooltip("稲妻用描画マテリアル (M_Weather_Lightning)")]
        public Material material;

        [Header("Bounds & Area Mode")]
        [Tooltip("General の共有設定や視線円環を上書きし、指定した X, Z 矩形平面内に落雷を限定するか")]
        public bool useCustomBounds = false;

        [Tooltip("落雷対象矩形平面の中心 (X, Z) (m)")]
        public Vector2 strikeAreaCenter = Vector2.zero;

        [Tooltip("落雷対象矩形平面の幅 (X) と奥行き (Z) (m)")]
        public Vector2 strikeAreaSize = new Vector2(0.5f, 0.5f);

        [Header("Viewer-Relative Range (If Custom Bounds Disabled)")]
        [Tooltip("落雷エリアの最小半径 (m) (卓上SRDisplay推奨: 0.05〜0.15)")]
        public float minRadius = 0.05f;

        [Tooltip("落雷エリアの最大半径 (m) (卓上SRDisplay推奨: 0.25〜0.40)")]
        public float maxRadius = 0.35f;

        [Tooltip("落雷エリアの視野角 (度)")]
        public float fov = 80f;

        [Tooltip("視点から着弾点が見えるか (遮蔽物裏の浮き防止)")]
        public bool requireLineOfSight = true;

        [Tooltip("床・部屋メッシュのレイヤーマスク")]
        public LayerMask environmentMask = ~0;

        [Header("Lightning Colors (Yellow / Electric)")]
        [Tooltip("稲妻の芯 (Core) の発光色 (デフォルト: 眩しい淡黄色)")]
        public Color coreColor = new Color(1.0f, 0.98f, 0.75f, 1.0f);

        [Tooltip("稲妻の外側 (Glow) の発光色 (デフォルト: 鮮烈な雷イエロー)")]
        public Color glowColor = new Color(1.0f, 0.82f, 0.15f, 0.9f);

        [Header("Lightning Appearance & Timing")]
        [Tooltip("落雷閃光の基本持続時間 (秒)。稲妻と閃光が視認できる長さ (推奨: 0.2〜0.5s)")]
        [Range(0.05f, 1.2f)]
        public float flashDuration = 0.35f;

        [Tooltip("落雷後の残光フェードアウト時間 (秒)")]
        [Range(0.1f, 1.5f)]
        public float fadeDuration = 0.45f;

        [Tooltip("稲妻の主幹の太さ (m) (推奨: 0.015〜0.06)")]
        [Range(0.005f, 0.08f)]
        public float trunkWidth = 0.03f;

        [Tooltip("連続落雷の最小インターバル (s)")]
        public float minStrikeInterval = 1.0f;

        [Header("Auto Lightning (Storm)")]
        [Tooltip("雨が強い時にポアソン過程で自動落雷させるか")]
        public bool autoThunder = true;

        [Tooltip("自動落雷が開始される雨強度の閾値 (0.0 〜 1.0)")]
        [Range(0f, 1f)] public float stormThreshold = 0.7f;

        [Tooltip("自動落雷の平均間隔 (s)")]
        public float meanStrikeInterval = 10.0f;
    }

    /// <summary>
    /// 太陽光・環境光・落雷点光源・URPポストプロセス調光に関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherLightingSettings
    {
        [Tooltip("太陽光 (Directional Light)。未指定時はシーン内から自動取得")]
        public Light sunLight;

        [Tooltip("落雷局所照明用 Point Light")]
        public Light boltPointLight;

        [Tooltip("雨最大時の太陽光減光率 (0: 減光なし 〜 1: 完全消灯)")]
        [Range(0f, 1f)] public float lightDarkenRate = 0.55f;

        [Tooltip("雨最大時の環境光減光率")]
        [Range(0f, 1f)] public float ambientDarkenRate = 0.5f;

        [Tooltip("落雷 Point Light の最大強度")]
        public float boltLightIntensity = 7.0f;

        [Tooltip("落雷 Point Light の照射範囲 (m)")]
        public float boltLightRange = 7.0f;

        [Tooltip("落雷瞬間の太陽光ブースト倍率")]
        public float sunFlashBoost = 2.5f;

        [Tooltip("落雷瞬間の環境光ブースト倍率")]
        public float ambientFlashBoost = 2.5f;

        [Tooltip("URP Post-Processing Volume (任意)")]
        public Volume postProcessVolume;
    }

    /// <summary>
    /// プロシージャル DSP 音響合成（雨音・雷鳴）に関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherAudioSettings
    {
        [Tooltip("雨音の最大音量ゲイン")]
        [Range(0f, 1f)] public float rainGain = 0.35f;

        [Tooltip("雨音のローパスフィルタ遮断周波数 (Hz)")]
        public float rainCutoff = 2500f;

        [Tooltip("雷鳴の最大音量ゲイン")]
        [Range(0f, 1f)] public float thunderGain = 0.85f;

        [Tooltip("距離による音速遅延の誇張倍率")]
        public float soundDistanceScale = 15f;
    }

    /// <summary>
    /// 点群ジェスチャー判定およびデバッグキー操作に関する設定データモデル。
    /// </summary>
    [Serializable]
    public class WeatherInputSettings
    {
        [Header("Hand Tracking")]
        [Tooltip("点群未検出時のフォールバック追跡対象 (任意)")]
        public Transform fallbackHandTransform;

        [Tooltip("手の平クラスタとみなす最小点数")]
        public int minClusterPoints = 15;

        [Header("Key Bindings")]
        [Tooltip("雨 ON/OFF トグルキー")]
        public KeyCode toggleRainKey = KeyCode.G;

        [Tooltip("雲 ON/OFF トグルキー")]
        public KeyCode toggleCloudKey = KeyCode.C;

        [Tooltip("落雷トリガーキー")]
        public KeyCode strikeKey = KeyCode.B;

        [Tooltip("晴れ (0%)")]
        public KeyCode preset0Key = KeyCode.Alpha7;

        [Tooltip("小雨 (30%)")]
        public KeyCode preset30Key = KeyCode.Alpha8;

        [Tooltip("強い雨 (70%)")]
        public KeyCode preset70Key = KeyCode.Alpha9;

        [Tooltip("豪雨・嵐 (100%)")]
        public KeyCode preset100Key = KeyCode.Alpha0;
    }
}
