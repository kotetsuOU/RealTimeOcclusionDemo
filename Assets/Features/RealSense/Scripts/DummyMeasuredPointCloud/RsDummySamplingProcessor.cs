using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// メッシュからの点群サンプリングおよびノイズ付加パイプラインを実行するプロセッサークラス。
    /// </summary>
    public class RsDummySamplingProcessor
    {
        private RsMeshPointCloudSampler _sampler;
        private RsPointCloudNoiseProcessor _noiseProcessor;

        public RsDummySamplingProcessor()
        {
            // MonoBehaviour コンストラクタ呼び出し時の UnityException (new Mesh禁止) を防ぐため
            // インスタンス生成はサンプリング実行時の遅延初期化で行います。
        }

        /// <summary>
        /// サンプラーの内部キャッシュを無効化します。
        /// </summary>
        public void InvalidateCache()
        {
            if (_sampler != null)
            {
                _sampler.InvalidateCache();
            }
        }

        /// <summary>
        /// ターゲットオブジェクト群から点群をサンプリングし、必要に応じてノイズ・外れ値を適用したデータを返します。
        /// </summary>
        public SampledPointCloudData ProcessSampling(
            List<GameObject> targetObjects,
            bool includeChildren,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            int maxPointLimit,
            RsPointCloudNoiseSettings noiseSettings,
            out bool wasNoiseApplied)
        {
            wasNoiseApplied = false;

            if (targetObjects == null || targetObjects.Count == 0)
            {
                return new SampledPointCloudData();
            }

            if (_sampler == null) _sampler = new RsMeshPointCloudSampler();
            if (_noiseProcessor == null) _noiseProcessor = new RsPointCloudNoiseProcessor();

            // 1. Mesh / SkinnedMesh リストから物理密度・色に応じた点群をサンプリング
            var sampledData = _sampler.SamplePointCloud(
                targetObjects,
                includeChildren,
                densityUnit,
                densityValue,
                colorMode,
                solidColor,
                maxPointLimit);

            bool isNoiseActive = noiseSettings != null && (noiseSettings.enableNoise || noiseSettings.enableOutliers);

            // 2. ノイズ・外れ値の適用
            Vector3[] finalPositions = sampledData.Positions;
            if (isNoiseActive && sampledData.PointCount > 0)
            {
                finalPositions = _noiseProcessor.ProcessPointCloud(
                    sampledData.Positions,
                    sampledData.Normals,
                    sampledData.PointCount,
                    noiseSettings);
                wasNoiseApplied = true;
            }

            sampledData.Positions = finalPositions;
            return sampledData;
        }

        public void Dispose()
        {
            _sampler = null;
            _noiseProcessor = null;
        }
    }
}
