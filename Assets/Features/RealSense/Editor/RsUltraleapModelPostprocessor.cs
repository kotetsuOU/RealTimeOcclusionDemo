#if UNITY_EDITOR
using UnityEditor;

namespace Features.RealSense.Editor
{
    /// <summary>
    /// Ultraleap (Leap Motion) などの外部パッケージに含まれるハンドモデル FBX を、
    /// 点群サンプリング (RsMeshPointCloudSampler) で頂点アクセスできるように
    /// isReadable = true で自動インポートする Postprocessor です。
    /// </summary>
    public class RsUltraleapModelPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath.Contains("com.ultraleap.tracking") && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                var modelImporter = (ModelImporter)assetImporter;
                if (!modelImporter.isReadable)
                {
                    modelImporter.isReadable = true;
                }
            }
        }
    }
}
#endif
