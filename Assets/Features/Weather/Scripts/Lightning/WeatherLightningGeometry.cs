using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 中点変位法 (Midpoint Displacement) により、ヒープ割り当て (GC Allocation) なしで
    /// 稲妻の折れ線頂点配列を生成する純粋 C# 幾何演算クラス。
    /// 事前に確保された作業用配列のダブルバッファリングにより、毎フレームのメモリ確保をゼロに抑えます。
    /// </summary>
    public class WeatherLightningGeometry
    {
        private const int MaxPoints = 256;

        // ダブルバッファ
        private readonly Vector3[] _bufferA = new Vector3[MaxPoints];
        private readonly Vector3[] _bufferB = new Vector3[MaxPoints];

        /// <summary>
        /// 始点から終点までの中点変位パスを生成し、出力配列にコピーします。
        /// </summary>
        /// <param name="start">始点 (天空/雲側)</param>
        /// <param name="end">終点 (着地面)</param>
        /// <param name="displacement">初期変位幅 (m)</param>
        /// <param name="depth">再帰深度 (推奨: 4〜6)</param>
        /// <param name="outPoints">結果を格納する配列 (要素数は 2^depth + 1 以上必要)</param>
        /// <returns>有効な頂点数</returns>
        public int GeneratePath(Vector3 start, Vector3 end, float displacement, int depth, Vector3[] outPoints)
        {
            if (outPoints == null || outPoints.Length < 2) return 0;
            depth = Mathf.Clamp(depth, 1, 7); // 2^7 + 1 = 129 <= 256

            _bufferA[0] = start;
            _bufferA[1] = end;
            int count = 2;

            Vector3[] readBuf = _bufferA;
            Vector3[] writeBuf = _bufferB;
            float currentDisp = displacement;

            for (int d = 0; d < depth; d++)
            {
                int writeCount = 0;
                for (int i = 0; i < count - 1; i++)
                {
                    Vector3 p0 = readBuf[i];
                    Vector3 p1 = readBuf[i + 1];

                    // 始点を書き込み
                    writeBuf[writeCount++] = p0;

                    // 中点と法線方向のランダム変位
                    Vector3 mid = (p0 + p1) * 0.5f;
                    Vector3 dir = (p1 - p0).normalized;

                    // 進行方向に直交するランダムベクトルを生成
                    Vector3 randPerp = Vector3.Cross(dir, Random.onUnitSphere).normalized;
                    mid += randPerp * Random.Range(-currentDisp, currentDisp);

                    writeBuf[writeCount++] = mid;
                }
                // 終点を書き込み
                writeBuf[writeCount++] = readBuf[count - 1];

                // スワップ
                var tmp = readBuf;
                readBuf = writeBuf;
                writeBuf = tmp;
                count = writeCount;
                currentDisp *= 0.5f;
            }

            int finalCount = Mathf.Min(count, outPoints.Length);
            for (int i = 0; i < finalCount; i++)
            {
                outPoints[i] = readBuf[i];
            }

            return finalCount;
        }

        /// <summary>
        /// 主幹パスの途中から枝分かれするサブパスを生成します。
        /// </summary>
        public int GenerateBranch(Vector3[] trunkPoints, int trunkCount, float branchLength, int depth, Vector3[] outPoints)
        {
            if (trunkPoints == null || trunkCount < 4 || outPoints == null) return 0;

            // 幹の中央 20%〜70% の範囲からランダムに分岐点を選択
            int startIndex = Random.Range(trunkCount / 5, (trunkCount * 7) / 10);
            Vector3 start = trunkPoints[startIndex];

            // 下降 + 横方向のランダムベクトル
            Vector3 down = Vector3.down;
            Vector3 side = Random.onUnitSphere;
            side.y = 0f;
            if (side.sqrMagnitude < 0.01f) side = Vector3.forward;
            side.Normalize();

            Vector3 branchDir = (down * 0.7f + side * 0.7f).normalized;
            Vector3 end = start + branchDir * branchLength;

            return GeneratePath(start, end, branchLength * 0.18f, depth, outPoints);
        }
    }
}
