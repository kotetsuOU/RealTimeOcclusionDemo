using System;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Config
{
    /// <summary>
    /// 単焦点デバッグ照射および空間キャリブレーション（位置合わせ）に関する設定パラメータを保持する設定クラス。
    /// 有効時は通常パイプライン（AutoHCD / ObjectTarget）より優先して単焦点（Naive）を連続照射します。
    /// </summary>
    [Serializable]
    public class HAP_DebugFocusConfig
    {
        [Tooltip("有効にすると、通常のハプティクス出力を上書きし、指定座標へ単焦点（Naive）を連続照射します。")]
        public bool enableDebugFocus = false;

        [Tooltip("任意のターゲット Transform。設定されている場合はそのワールド座標を追従し、未指定時は下記の Focus Position を使用します。")]
        public Transform? targetTransform = null;

        [Tooltip("Target Transform 未指定時に使用する単焦点のワールド座標 (m)。")]
        public Vector3 focusPosition = new Vector3(0f, 0.2f, 0f);

        [Tooltip("有効にすると、振動子の出力上限（Intensity.Max / Duty 100%）で単焦点を駆動し、知覚・位置確認を行いやすくします。")]
        public bool maximizeDuty = true;

        [Tooltip("単焦点の照射強度 (Pascal)。Maximize Duty が OFF の場合に使用されます。")]
        [Range(0f, 25000f)]
        public float focusIntensityPascal = 10000f;

        [Tooltip("有効にすると、Hardware Settings が Static（無変調）であっても単焦点テスト中のみ Sine 変調 (AM) を一時適用して皮膚知覚を強くします。")]
        public bool forceSineModulation = true;

        [Tooltip("単焦点テスト中に一時適用する Sine 変調周波数 (Hz)。人間の皮膚感度が最も高い 150Hz 前後を推奨します。")]
        [Range(20f, 400f)]
        public float debugSineFrequency = 150f;

        [Tooltip("有効にすると、TransformLoader の全体焦点オフセット (offset) を加算した座標へ照射します。")]
        public bool applyCalibrationOffset = true;

        [Tooltip("差分キャリブレーション用の実空間リファレンス（正解位置 Transform）。")]
        public Transform? referenceTrueTarget = null;

        [Tooltip("Scene ビュー上に単焦点位置・照射レイ・オフセット差分を Gizmo 表示します。")]
        public bool showFocusGizmo = true;

        /// <summary>
        /// 実効目標音圧 (Pa) を取得します。Maximize Duty 有効時は振動子飽和（Duty 100%）となる目標値を返します。
        /// </summary>
        public float GetEffectiveTargetPascal()
            => maximizeDuty ? 50000f : Mathf.Max(0f, focusIntensityPascal);

        /// <summary>
        /// オフセット適用前の基準ワールド座標を取得します。
        /// </summary>
        public Vector3 GetRawFocusPosition()
            => targetTransform != null ? targetTransform.position : focusPosition;

        /// <summary>
        /// キャリブレーションオフセット適用後の実照射ワールド座標を取得します。
        /// </summary>
        public Vector3 GetEffectiveFocusPosition(Vector3 calibrationOffset)
            => GetRawFocusPosition() + (applyCalibrationOffset ? calibrationOffset : Vector3.zero);
    }
}

