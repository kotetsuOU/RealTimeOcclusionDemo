using System.Collections.Generic;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 稲妻ボルトの核色 (Core)・外側発光色 (Glow) の HDR エミッション計算および
    /// MaterialPropertyBlock を用いた Zero-GC アルファ・輝度制御を担当する Pure C# クラス。
    /// </summary>
    public class WeatherBoltEmissionController
    {
        private static readonly Color DefaultCoreColor = new Color(1.0f, 0.98f, 0.8f, 1.0f);
        private static readonly Color DefaultGlowColor = new Color(1.0f, 0.85f, 0.15f, 0.95f);

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");

        private Color _coreColor = DefaultCoreColor;
        private Color _glowColor = DefaultGlowColor;

        public Color CoreColor => _coreColor;
        public Color GlowColor => _glowColor;

        /// <summary>
        /// 稲妻の核色 (Core) と外側発光色 (Glow) を設定します。
        /// </summary>
        public void SetColors(Color core, Color glow)
        {
            _coreColor = core;
            _glowColor = glow;
        }

        /// <summary>
        /// 発光レベル (0.0 〜 1.0) に応じて全アクティブリボンのマテリアルカラーとアルファを即時反映します。
        /// </summary>
        public void ApplyLevel(
            float level,
            WeatherBoltRibbon trunk,
            IReadOnlyList<WeatherBoltRibbon> branches,
            WeatherBoltRibbon impactSparks)
        {
            if (trunk != null && trunk.Root != null && trunk.Root.activeSelf)
            {
                ApplyAlphaToRibbon(trunk, Mathf.Clamp01(level * trunk.Weight));
            }

            if (branches != null)
            {
                for (int i = 0; i < branches.Count; i++)
                {
                    var br = branches[i];
                    if (br != null && br.Root != null && br.Root.activeSelf)
                    {
                        ApplyAlphaToRibbon(br, Mathf.Clamp01(level * br.Weight));
                    }
                }
            }

            if (impactSparks != null && impactSparks.Root != null && impactSparks.Root.activeSelf)
            {
                ApplyAlphaToRibbon(impactSparks, Mathf.Clamp01(level * impactSparks.Weight));
            }
        }

        private void ApplyAlphaToRibbon(WeatherBoltRibbon ribbon, float level)
        {
            float clamped = Mathf.Clamp01(level);

            // Core MPB: 芯の眩しい淡黄発光 (HDRブースト)
            Color coreCol = new Color(_coreColor.r * clamped, _coreColor.g * clamped, _coreColor.b * clamped, 1.0f);
            Color coreEmission = new Color(_coreColor.r * clamped * 3.0f, _coreColor.g * clamped * 3.0f, _coreColor.b * clamped * 3.0f, 1.0f);
            ribbon.CoreMpb.SetColor(BaseColorPropId, coreCol);
            ribbon.CoreMpb.SetColor(ColorPropId, coreCol);
            ribbon.CoreMpb.SetColor(EmissionColorPropId, coreEmission);
            ribbon.CoreRenderer.SetPropertyBlock(ribbon.CoreMpb);

            // Glow MPB: 外側の鮮烈な雷イエロー発光
            Color glowCol = new Color(_glowColor.r * clamped, _glowColor.g * clamped, _glowColor.b * clamped, 1.0f);
            Color glowEmission = new Color(_glowColor.r * clamped * 2.2f, _glowColor.g * clamped * 2.2f, _glowColor.b * clamped * 2.2f, 1.0f);
            ribbon.GlowMpb.SetColor(BaseColorPropId, glowCol);
            ribbon.GlowMpb.SetColor(ColorPropId, glowCol);
            ribbon.GlowMpb.SetColor(EmissionColorPropId, glowEmission);
            ribbon.GlowRenderer.SetPropertyBlock(ribbon.GlowMpb);
        }
    }
}
