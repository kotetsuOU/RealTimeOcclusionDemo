using System;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 落雷演出の実行受付、コルーチン駆動、および外部イベント通知を統括する薄いオーケストレーター。
    /// 詳細な処理は以下の 4 つの専任クラスに委譲されています:
    /// 1. WeatherLightningConfigApplier: 設定モデルの展開、共有空間バウンズ統合、プール配線
    /// 2. WeatherLightningStrikePlanner: 落雷可否判定、床面 Raycast 着地点・空中始点選定
    /// 3. WeatherLightningFlashController: 閃光レベル F(t) 状態管理、ボルトプール輝度反映、終了中断処理
    /// 4. WeatherLightningSequencer: 再雷撃マルチストローク・瞬き・残光タイムライン
    /// </summary>
    public class WeatherLightningProcessor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("稲妻マテリアル (URP Particles/Unlit 推奨)")]
        [SerializeField] private Material lightningMaterial;

        [Header("Ground Picking Area")]
        [Tooltip("矩形平面モード (True: 指定 X, Z 矩形平面内から着弾選定, False: 視点前方円環から選定)")]
        [SerializeField] private bool isFixedBounds = true;

        [Tooltip("True: カメラの水平位置に矩形範囲を追従 / False: 固定ワールド座標矩形")]
        [SerializeField] private bool followViewer = false;

        [Tooltip("落雷対象矩形平面の中心 (X, Z)")]
        [SerializeField] private Vector2 strikeAreaCenter = Vector2.zero;

        [Tooltip("落雷対象矩形平面の幅 (X) と奥行き (Z)")]
        [SerializeField] private Vector2 strikeAreaSize = new Vector2(0.55f, 0.35f);

        [Tooltip("落雷エリアの最小半径 (m) (円環モード時)")]
        [SerializeField] private float strikeMinRadius = 0.05f;

        [Tooltip("落雷エリアの最大半径 (m) (円環モード時)")]
        [SerializeField] private float strikeMaxRadius = 0.35f;

        [Tooltip("落雷エリアの視野角 (度) (円環モード時)")]
        [SerializeField] private float strikeFov = 80f;

        [Tooltip("視点から着弾点が見えるか (家具の向こうに落ちて手前に浮いて見えるのを防止)")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("床・部屋メッシュのレイヤーマスク")]
        [SerializeField] private LayerMask environmentMask = ~0;

        [Header("Timing & Appearance")]
        [Tooltip("連続落雷の最小インターバル (s)")]
        [SerializeField] private float minStrikeInterval = 1.0f;

        [Tooltip("落雷閃光の基本持続時間 (秒)。稲妻と閃光が視認できる長さ")]
        [SerializeField] private float flashDuration = 0.35f;

        [Tooltip("落雷後の残光フェードアウト時間 (秒)")]
        [SerializeField] private float fadeDuration = 0.45f;

        [Tooltip("稲妻の主幹の太さ (m)")]
        [SerializeField] private float trunkWidth = 0.03f;

        [SerializeField] private WeatherLightningBoltPool boltPool;

        // ── 外部イベント ──
        public event Action<Vector3> OnStrikeStarted;
        public event Action<float> OnFlashLevelChanged; // F(t) in [0, 1]
        public event Action OnStrikeEnded;

        // ── 専任コンポーネント (Pure C#) ──
        private readonly WeatherLightningGeometry _geometry = new WeatherLightningGeometry();
        private readonly WeatherLightningSequencer _sequencer = new WeatherLightningSequencer();
        private readonly WeatherLightningStrikePlanner _planner = new WeatherLightningStrikePlanner();
        private readonly WeatherLightningFlashController _flashController = new WeatherLightningFlashController();

        public bool IsBusy => _flashController.IsBusy;
        public float CurrentFlash => _flashController.CurrentFlash;

        public bool IsFixedBounds
        {
            get => isFixedBounds;
            set { isFixedBounds = value; _planner.GroundPicker.IsFixedBounds = value; }
        }

        public bool FollowViewerMode
        {
            get => followViewer;
            set { followViewer = value; _planner.GroundPicker.FollowViewer = value; }
        }

        public Vector2 StrikeAreaCenter
        {
            get => strikeAreaCenter;
            set { strikeAreaCenter = value; _planner.GroundPicker.StrikeAreaCenter = value; }
        }

        public Vector2 StrikeAreaSize
        {
            get => strikeAreaSize;
            set { strikeAreaSize = value; _planner.GroundPicker.StrikeAreaSize = value; }
        }

        public Material LightningMaterial
        {
            get => lightningMaterial;
            set => lightningMaterial = value;
        }

        public float FlashDuration
        {
            get => flashDuration;
            set { flashDuration = value; _sequencer.FlashDuration = value; }
        }

        public float FadeDuration
        {
            get => fadeDuration;
            set { fadeDuration = value; _sequencer.FadeDuration = value; }
        }

        public float TrunkWidth
        {
            get => trunkWidth;
            set
            {
                trunkWidth = value;
                if (boltPool != null) boltPool.SetWidth(trunkWidth);
            }
        }

        public WeatherLightningBoltPool BoltPool
        {
            get => boltPool;
            set => boltPool = value;
        }

        public void ApplySettings(
            WeatherLightningSettings settings,
            Vector2? sharedCenter = null,
            Vector2? sharedSize = null,
            bool? sharedFollowViewer = null)
        {
            WeatherLightningConfigApplier.ApplySettings(
                settings,
                sharedCenter,
                sharedSize,
                sharedFollowViewer,
                _planner,
                _sequencer,
                boltPool,
                ref lightningMaterial,
                ref isFixedBounds,
                ref followViewer,
                ref strikeAreaCenter,
                ref strikeAreaSize,
                ref strikeMinRadius,
                ref strikeMaxRadius,
                ref strikeFov,
                ref requireLineOfSight,
                ref environmentMask,
                ref minStrikeInterval,
                ref flashDuration,
                ref fadeDuration,
                ref trunkWidth);
        }

        public void Initialize(Transform parent, Material customMaterial = null)
        {
            if (customMaterial != null) lightningMaterial = customMaterial;
            boltPool = WeatherLightningConfigApplier.EnsureBoltPool(parent != null ? parent : transform, boltPool);
            if (boltPool != null)
            {
                boltPool.Initialize(boltPool.transform, lightningMaterial, trunkWidth);
            }
        }

        private void Awake()
        {
            WeatherLightningConfigApplier.SyncComponents(
                _planner,
                _sequencer,
                isFixedBounds,
                followViewer,
                strikeAreaCenter,
                strikeAreaSize,
                strikeMinRadius,
                strikeMaxRadius,
                strikeFov,
                requireLineOfSight,
                environmentMask,
                minStrikeInterval,
                flashDuration,
                fadeDuration);

            boltPool = WeatherLightningConfigApplier.EnsureBoltPool(transform, boltPool);
            if (boltPool != null)
            {
                boltPool.Initialize(transform, lightningMaterial, trunkWidth);
            }
        }

        /// <summary>
        /// 落雷を実行します。at が null の場合は視界内の安全な領域から自動選定します。
        /// </summary>
        public bool TriggerStrike(Transform viewer, float cloudY, float groundY, Vector3? at = null)
        {
            if (!_planner.CanStrike(Time.time, _flashController.IsBusy))
            {
                return false;
            }

            if (!_planner.TryPlanStrike(viewer, transform, cloudY, groundY, at, strikeAreaSize, Time.time, out Vector3 skyPoint, out Vector3 targetGround))
            {
                return false;
            }

            _flashController.IsBusy = true;

            // ボルト幾何パスをプールから構築・表示
            boltPool.BuildAndShow(skyPoint, targetGround, _geometry);

            OnStrikeStarted?.Invoke(targetGround);
            StartCoroutine(_sequencer.ExecuteStrikeRoutine(ApplyFlash, EndStrike));

            return true;
        }

        private void ApplyFlash(float level)
        {
            _flashController.ApplyFlash(level, boltPool, OnFlashLevelChanged);
        }

        private void EndStrike()
        {
            _flashController.EndStrike(boltPool, OnFlashLevelChanged, OnStrikeEnded);
        }

        public void Stop()
        {
            StopAllCoroutines();
            _flashController.Reset(boltPool, OnFlashLevelChanged, OnStrikeEnded);
        }

        private void OnDisable()
        {
            Stop();
        }
    }
}
