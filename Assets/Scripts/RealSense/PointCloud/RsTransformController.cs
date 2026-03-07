using System;
using UnityEngine;

public class RsTransformController : MonoBehaviour
{
    [Serializable]
    public class CalibrationSlot
    {
        [Tooltip("直方体の起点となる座標（親からのローカル座標）")]
        public Vector3 origin = new Vector3(0.30f, 0.0f, 0.25f);

        [Tooltip("直方体のサイズ（幅・高さ・奥行き）")]
        public Vector3 boxSize = new Vector3(0.29f, 0.405f, 0.08f);
    }

    [Header("Config")]
    [Tooltip("現在選択中のスロット番号 (0-2)")]
    [Range(0, 2)]
    public int currentSlotIndex = 0;

    [Header("Calibration Slots")]
    public CalibrationSlot slot1 = new CalibrationSlot();
    public CalibrationSlot slot2 = new CalibrationSlot();
    public CalibrationSlot slot3 = new CalibrationSlot();

    [Header("Calibration Box Guide")]
    [Tooltip("シーンビューに位置合わせ用のガイドボックスを表示するか")]
    public bool showCalibrationGuide = true;

    [Tooltip("ボックス枠線の色")]
    public Color guideFrameColor = Color.green;

    [Tooltip("各頂点のマーカー色")]
    public Color cornerMarkerColor = Color.red;

    public CalibrationSlot CurrentSlot
    {
        get
        {
            switch (currentSlotIndex)
            {
                case 0: return slot1;
                case 1: return slot2;
                case 2: return slot3;
                default: return slot1;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (UnityEngine.Application.isPlaying) return;

        if (!showCalibrationGuide) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        CalibrationSlot slot = CurrentSlot;
        Vector3 origin = slot.origin;
        Vector3 size = slot.boxSize;

        Gizmos.color = guideFrameColor;

        Vector3 localCenter = origin + (size * 0.5f);
        Gizmos.DrawWireCube(localCenter, size);

        Gizmos.color = cornerMarkerColor;

        float markerRadius = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.05f;

        Vector3[] corners = new Vector3[]
        {
            origin,
            origin + new Vector3(size.x, 0, 0),
            origin + new Vector3(0, size.y, 0),
            origin + new Vector3(0, 0, size.z),
            origin + new Vector3(size.x, size.y, 0),
            origin + new Vector3(size.x, 0, size.z),
            origin + new Vector3(0, size.y, size.z),
            origin + size
        };

        foreach (var point in corners)
        {
            Gizmos.DrawWireSphere(point, markerRadius);
        }
    }
}