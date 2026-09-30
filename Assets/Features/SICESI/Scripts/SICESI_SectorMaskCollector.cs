using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// 点群密度を自動的に切り替えながら、各密度の実測8ビット占有セクターマスク (SectorMask RAW/CSV/PNG) と
    /// GT画像、テストレンダリング画像を自動一括キャプチャ・保存するコレクタークラス (Facade)。
    /// 内部ロジックは SICESI_SectorMaskSweepRunner および SICESI_Pattern256SweepRunner に責務分離されています。
    /// </summary>
    [AppLoggable("SICESI")]
    [RequireComponent(typeof(SICESI_StereoEvaluationController))]
    public class SICESI_SectorMaskCollector : MonoBehaviour, IAppLoggable
    {
        /// <summary>
        /// セクターマスクのスイープ収集中かどうかを示すグローバルフラグ。
        /// </summary>
        public static bool IsCollectingActive { get; set; } = false;

        [Header("Status")]
        public bool isCollecting = false;
        public string statusMessage = "Ready";

        [Header("Pattern Sweep Settings")]
        [Tooltip("256パターンマスクスイープ時に収集する対象パターン番号配列 (null/空で全256パターン 0〜255)")]
        public int[] targetPatterns = null;

        private SICESI_StereoEvaluationController _controller;
        private SICESI_SectorMaskSweepRunner _sectorMaskRunner;
        private SICESI_Pattern256SweepRunner _pattern256Runner;

        public SICESI_SectorMaskSweepRunner SectorMaskRunner
        {
            get
            {
                if (_sectorMaskRunner == null)
                {
                    EnsureController();
                    _sectorMaskRunner = new SICESI_SectorMaskSweepRunner(_controller, this);
                }
                return _sectorMaskRunner;
            }
        }

        public SICESI_Pattern256SweepRunner Pattern256Runner
        {
            get
            {
                if (_pattern256Runner == null)
                {
                    EnsureController();
                    _pattern256Runner = new SICESI_Pattern256SweepRunner(_controller, this);
                }
                return _pattern256Runner;
            }
        }

        private void Awake()
        {
            EnsureController();
            _sectorMaskRunner = new SICESI_SectorMaskSweepRunner(_controller, this);
            _pattern256Runner = new SICESI_Pattern256SweepRunner(_controller, this);
        }

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // AppLogger 管理用
        }

        /// <summary>
        /// 全密度の占有セクターマスクおよびステレオ評価画像の一括自動取得を開始します（AsyncGPUReadback）。
        /// </summary>
        public void RunSectorMaskDensitySweep()
        {
            if (isCollecting)
            {
                Debug.LogWarning("[SICESI] 既に収集処理が実行中です。");
                return;
            }

            EnsureController();
            if (_controller == null) return;

            StartCoroutine(SectorMaskRunner.SectorMaskDensitySweepRoutine());
        }

        /// <summary>
        /// 8セクター・ビットプレーン方式の二値マスク画面保存スイープ。
        /// </summary>
        public void RunSector8MaskSweep()
        {
            if (isCollecting)
            {
                Debug.LogWarning("[SICESI] 既に収集処理が実行中です。");
                return;
            }

            EnsureController();
            if (_controller == null) return;

            StartCoroutine(SectorMaskRunner.Sector8MaskSweepRoutine());
        }

        /// <summary>
        /// 256占有パターンの単独二値マスク一括収集スイープ (旧36回転クラスを完全包括)
        /// </summary>
        public void RunPattern256MaskSweep()
        {
            if (isCollecting)
            {
                Debug.LogWarning("[SICESI] 既に収集処理が実行中です。");
                return;
            }

            EnsureController();
            if (_controller == null) return;

            StartCoroutine(Pattern256Runner.Pattern256MaskSweepRoutine());
        }

        /// <summary>
        /// 後方互換性エイリアス: 36回転クラスマスクスイープは 256パターンマスクスイープに包括されました。
        /// </summary>
        public void RunRotationClassMaskSweep()
        {
            RunPattern256MaskSweep();
        }

        private void EnsureController()
        {
            if (_controller == null)
            {
                _controller = GetComponent<SICESI_StereoEvaluationController>();
            }

            if (_controller == null)
            {
                Debug.LogError("[SICESI] SICESI_StereoEvaluationController が見つかりません。");
            }
        }
    }
}
