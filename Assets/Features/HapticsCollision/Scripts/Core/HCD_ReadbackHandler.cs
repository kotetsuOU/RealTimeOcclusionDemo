using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Core.Logging;

namespace Features.HapticsCollision.Core
{
    /// <summary>
    /// GPU からの非同期読み込み (AsyncGPUReadback) リクエストキューイングおよび完了フレーム監視を担当するクラス。
    /// </summary>
    public class HCD_ReadbackHandler
    {
        private struct ReadbackRequest
        {
            public AsyncGPUReadbackRequest clusterReq;
            public AsyncGPUReadbackRequest precisionReq;
            public bool hasPrecision;
        }

        /// <summary>
        /// GPU 読み出しの最大インフライト数。
        /// キューの滞留による遅延（過去フレームの蓄積）および Unity の Readback バッファ枯渇エラーを防ぐため 1 に制限します。
        /// </summary>
        private const int MaxInFlightRequests = 1;

        private readonly Queue<ReadbackRequest> _readbackQueue = new Queue<ReadbackRequest>();

        public void RequestAsyncReadback(ComputeBuffer clusterBuffer, ComputeBuffer precisionBuffer, bool precisionMode)
        {
            if (clusterBuffer == null) return;
            if (_readbackQueue.Count >= MaxInFlightRequests) return;

            bool hasPrecision = precisionMode && precisionBuffer != null;

            _readbackQueue.Enqueue(new ReadbackRequest
            {
                clusterReq = AsyncGPUReadback.Request(clusterBuffer),
                precisionReq = hasPrecision ? AsyncGPUReadback.Request(precisionBuffer) : default,
                hasPrecision = hasPrecision
            });
        }

        public bool ProcessQueue(HCD_ClusterDecoder decoder, Object context)
        {
            ReadbackRequest? latestDoneReq = null;

            while (_readbackQueue.Count > 0)
            {
                var req = _readbackQueue.Peek();

                if (req.clusterReq.hasError || (req.hasPrecision && req.precisionReq.hasError))
                {
                    AppLogger.LogWarning(context, "HCD_Pipeline", "AsyncGPUReadback error. クラスタバッファ読み込みエラー。キューをリセットします。");
                    _readbackQueue.Clear();
                    return false;
                }

                if (!req.clusterReq.done || (req.hasPrecision && !req.precisionReq.done))
                {
                    break;
                }

                latestDoneReq = _readbackQueue.Dequeue();
            }

            if (latestDoneReq.HasValue)
            {
                var req = latestDoneReq.Value;
                req.clusterReq.GetData<HCD_ClusterDecoder.ClusterData>().CopyTo(decoder.ClusterResults);
                if (req.hasPrecision && decoder.PrecisionResults != null)
                {
                    req.precisionReq.GetData<HCD_ClusterDecoder.ClusterPrecisionDataRaw>().CopyTo(decoder.PrecisionResults);
                }
                return true;
            }

            return false;
        }

        public void Clear()
        {
            _readbackQueue.Clear();
        }
    }
}
