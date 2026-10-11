using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// ダミー点群のターゲットオブジェクト群のマテリアルカラーを同期・更新するプレゼンタークラス。
    /// MaterialPropertyBlock を活用してマテリアルインスタンスを複製せずに色を変更します。
    /// </summary>
    public class RsDummyMaterialColorPresenter
    {
        private MaterialPropertyBlock _materialPropertyBlock;

        /// <summary>
        /// ターゲットオブジェクトの各 Renderer に対し、SolidColor をプロパティブロック経由で適用します。
        /// </summary>
        public void ApplySolidColor(
            IEnumerable<GameObject> targetObjects,
            bool includeChildren,
            Color solidColor)
        {
            if (targetObjects == null) return;

            if (_materialPropertyBlock == null)
            {
                _materialPropertyBlock = new MaterialPropertyBlock();
            }

            foreach (var obj in targetObjects)
            {
                if (obj == null || !obj.activeInHierarchy) continue;

                var renderers = includeChildren
                    ? obj.GetComponentsInChildren<Renderer>()
                    : obj.GetComponents<Renderer>();

                foreach (var r in renderers)
                {
                    if (r == null || !RsMeshPointCloudSampler.IsRendererActiveForSampling(r, obj)) continue;

                    r.GetPropertyBlock(_materialPropertyBlock);
                    _materialPropertyBlock.SetColor("_Color", solidColor);
                    _materialPropertyBlock.SetColor("_BaseColor", solidColor);
                    r.SetPropertyBlock(_materialPropertyBlock);
                }
            }
        }
    }
}
