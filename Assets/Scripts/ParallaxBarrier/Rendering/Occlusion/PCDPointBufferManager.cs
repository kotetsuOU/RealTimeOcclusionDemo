using System.Collections.Generic;
using UnityEngine;

public class PCDPointBufferManager
{
    // 点群データの1点分を表す構造体
    public struct Point
    {
        public Vector3 position;
        public Vector3 color;
        public uint originType; // 0 = PointCloud（動的点群）, 1 = StaticMesh（静的メッシュからの頂点）
    }

    // 登録された静的メッシュとトランスフォーム、処理モードを保持するクラス
    private class MeshTransformPair
    {
        public Mesh mesh;
        public Transform transform;
        public PCDProcessingMode mode;

        // 計算済みのワールド座標ポイントキャッシュ
        public Point[] cachedPoints;
        public Matrix4x4 lastMatrix;
    }

    // --- 内部バッファ管理 (静的メッシュ及びCPUベースの点群用) ---
    private ComputeBuffer _pointBuffer;
    private int _pointCount = 0;
    private Point[] _pointsCache;
    private bool _isDataDirty = false; // データが変更され、再構築が必要かどうか

    // --- 外部バッファ管理 (GPU側の直接連携用) ---
    private ComputeBuffer _externalPointBuffer;
    private int _externalPointCount = 0;
    private bool _useExternalBuffer = false;

    // --- 結合バッファ (外部バッファ + 内部バッファ) ---
    private ComputeBuffer _combinedBuffer;

    private PCV_Data _dynamicData; // CPU側から提供される動的点群データ
    private List<MeshTransformPair> _staticMeshes = new List<MeshTransformPair>();
    private const int STRIDE = 28; // 1要素のデータサイズ: sizeof(float)*3 + sizeof(float)*3 + sizeof(uint)

    // GC回避用の使い回しリスト
    private List<Vector3> _tempVertices = new List<Vector3>();
    private List<Color> _tempColors = new List<Color>();

    // 各種プロパティへのアクセス
    public ComputeBuffer PointBuffer => _pointBuffer;
    public int PointCount => _pointCount;
    public ComputeBuffer ExternalPointBuffer => _externalPointBuffer;
    public int ExternalPointCount => _externalPointCount;
    public bool UseExternalBuffer => _useExternalBuffer;
    public ComputeBuffer CombinedBuffer => _combinedBuffer;
    public bool IsDataDirty => _isDataDirty; // 最適化やデバッグ用のフラグ確認

    // 外部から渡されるGPUバッファを設定する
    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        bool prevUse = _useExternalBuffer;

        if (buffer != null && buffer.IsValid())
        {
            _externalPointBuffer = buffer;
            _externalPointCount = count;
            _useExternalBuffer = true;
        }
        else
        {
            _useExternalBuffer = false;
            _externalPointBuffer = null;
            _externalPointCount = 0;
        }

        if (prevUse != _useExternalBuffer)
        {
            _isDataDirty = true;
        }
    }

    // CPUから更新される動的な点群データをセットする
    public void SetPointCloudData(PCV_Data data)
    {
        // 参照が変わった、または頂点数が変わった場合はダーティフラグを立てる
        if (_dynamicData != data || (data != null && _dynamicData != null && _dynamicData.PointCount != data.PointCount))
        {
            _dynamicData = data;
            _isDataDirty = true;
        }
        else if (data == null && _dynamicData != null)
        {
            _dynamicData = null;
            _isDataDirty = true;
        }
    }

    // オクルージョン干渉用の静的メッシュを追加する
    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        if (mesh != null && transform != null)
        {
            var existing = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
            if (existing == null)
            {
                _staticMeshes.Add(new MeshTransformPair { mesh = mesh, transform = transform, mode = mode });
                _isDataDirty = true;
                UnityEngine.Debug.Log($"[PCDPointBufferManager] Static mesh '{mesh.name}' added from Transform '{transform.name}'.");
            }
            else if (existing.mode != mode)
            {
                // モードだけが変更になった場合
                existing.mode = mode;
                _isDataDirty = true;
            }
        }
    }

    // 動的メッシュの更新を強制するためにダーティフラグを立てる
    public void SetDataDirty()
    {
        _isDataDirty = true;
    }

    // 登録されている静的メッシュを削除する
    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        var pair = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
        if (pair != null)
        {
            _staticMeshes.Remove(pair);
            _isDataDirty = true;
            UnityEngine.Debug.Log($"[PCDPointBufferManager] Static mesh '{mesh.name}' removed from Transform '{transform.name}'.");
        }
    }

    // 登録済みメッシュにDepthMap（深さのレンダリング用）モードのものが存在するか確認する
    public bool HasDepthMapMeshes()
    {
        return _staticMeshes.Exists(p => p.mode == PCDProcessingMode.DepthMap);
    }

    // 登録済みメッシュにPointCloud（点群として扱う）モードのものが存在するか確認する
    public bool HasPointCloudMeshes()
    {
        return _staticMeshes.Exists(p => p.mode == PCDProcessingMode.PointCloud);
    }

    // データに変更があった場合のみ、キャッシュの再構築とバッファ更新をおこなう
    public void Update()
    {
        if (_isDataDirty)
        {
            MergeAndCachePoints();
            UpdateComputeBuffer();
        }
    }

    // 必要に応じて、外部バッファと内部バッファを結合するためのバッファサイズを確保・再確保する
    public void EnsureCombinedBuffer(int totalCount)
    {
        if (_combinedBuffer == null || !_combinedBuffer.IsValid() || _combinedBuffer.count < totalCount)
        {
            _combinedBuffer?.Release();
            _combinedBuffer = new ComputeBuffer(totalCount, STRIDE);
        }
    }

    // 静的メッシュの頂点と、CPU側の動的点群を一つのPoint構造体配列（キャッシュ）に統合する
    private void MergeAndCachePoints()
    {
        int dataPointCount = 0;
        // 外部バッファ（GPU）を使わない場合のみ、CPU側の点群データを統合対象とする
        if (!_useExternalBuffer && _dynamicData != null && _dynamicData.PointCount > 0)
        {
            dataPointCount = _dynamicData.PointCount;
        }

        int totalMeshPointCount = 0;
        // 点群モードに設定されているすべての静的メッシュの頂点数をカウントする
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || pair.transform == null) continue;
            if (!pair.mesh.isReadable) continue;
            // PointCloudモードのメッシュのみポイントバッファに追加する
            if (pair.mode != PCDProcessingMode.PointCloud) continue;
            totalMeshPointCount += pair.mesh.vertexCount;
        }

        // 合計の頂点数
        _pointCount = dataPointCount + totalMeshPointCount;

        // 点数がゼロなら配列を破棄して終了
        if (_pointCount == 0)
        {
            _pointsCache = null;
            return;
        }

        // 配列の確保が必要なら十分なサイズを確保する（再生成によるGCを削減）
        if (_pointsCache == null || _pointsCache.Length < _pointCount)
        {
            int newSize = Mathf.Max(_pointCount, _pointsCache != null ? _pointsCache.Length * 2 : 1024);
            _pointsCache = new Point[newSize];
        }

        int cacheIndex = 0;

        // 1. CPU動的点群データを配列へ格納
        if (dataPointCount > 0)
        {
            for (int i = 0; i < dataPointCount; i++)
            {
                _pointsCache[cacheIndex] = new Point
                {
                    position = _dynamicData.Vertices[i],
                    color = new Vector3(_dynamicData.Colors[i].r, _dynamicData.Colors[i].g, _dynamicData.Colors[i].b),
                    originType = 0 // 点群由来フラグ
                };
                cacheIndex++;
            }
        }

        // 2. 静的メッシュ（PointCloudモード）の頂点情報を順番に配列へ格納
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || !pair.mesh.isReadable || pair.transform == null) continue;
            if (pair.mode != PCDProcessingMode.PointCloud) continue;

            int meshPointCount = pair.mesh.vertexCount;
            if (meshPointCount == 0) continue;

            // ローカル座標からワールド座標へ変換するための行列
            Matrix4x4 localToWorld = pair.transform.localToWorldMatrix;

            // 行列が変わっているか、キャッシュがなければ再計算（毎フレームのVector3計算を避ける）
            if (pair.cachedPoints == null || pair.cachedPoints.Length != meshPointCount || pair.lastMatrix != localToWorld)
            {
                pair.mesh.GetVertices(_tempVertices);
                pair.mesh.GetColors(_tempColors);
                bool hasMeshColors = _tempColors.Count == meshPointCount;

                if (pair.cachedPoints == null || pair.cachedPoints.Length != meshPointCount)
                {
                    pair.cachedPoints = new Point[meshPointCount];
                }

                for (int i = 0; i < meshPointCount; i++)
                {
                    Vector3 color = hasMeshColors ? new Vector3(_tempColors[i].r, _tempColors[i].g, _tempColors[i].b) : Vector3.one;
                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(_tempVertices[i]);

                    pair.cachedPoints[i] = new Point
                    {
                        position = worldPos,
                        color = color,
                        originType = 1 // メッシュ由来フラグ
                    };
                }
                pair.lastMatrix = localToWorld;
            }

            // 計算済みのキャッシュから高速コピー（1万以上の反復処理を省略）
            System.Array.Copy(pair.cachedPoints, 0, _pointsCache, cacheIndex, meshPointCount);
            cacheIndex += meshPointCount;
        }

        if (_isDataDirty)
        {
            string mode = _useExternalBuffer ? "External(GPU) + Static" : "Internal(CPU) + Static";
            // Reduce repetitive logs if needed, but keeping for parity
            // UnityEngine.Debug.Log($"[PCDPointBufferManager] Merged points [{mode}] - Dynamic(CPU): {dataPointCount}, Static Meshes: {totalMeshPointCount}, InternalTotal: {_pointCount}");
        }
    }

    // 結合・キャッシュされた頂点情報をもとに、ComputeShaderへ渡すためのバッファを更新する
    private void UpdateComputeBuffer()
    {
        if (_pointCount == 0 || _pointsCache == null)
        {
            _pointBuffer?.Release();
            _pointBuffer = null;
            _isDataDirty = false;
            return;
        }

        // バッファが未割り当てか、サイズが不足している場合のみ再生成する
        if (_pointBuffer == null || !_pointBuffer.IsValid() || _pointBuffer.count < _pointCount)
        {
            int oldSize = (_pointBuffer != null && _pointBuffer.IsValid()) ? _pointBuffer.count : 0;
            _pointBuffer?.Release();
            int newSize = Mathf.Max(_pointCount, Mathf.Max(oldSize * 2, 1024));
            _pointBuffer = new ComputeBuffer(newSize, STRIDE);
        }

        // キャッシュした頂点配列のうち、有効な部分だけをGPU側へ転送
        _pointBuffer.SetData(_pointsCache, 0, 0, _pointCount);
        if (_pointCount > 0 && _isDataDirty)
        {
            UnityEngine.Debug.Log($"[PCDPointBufferManager] ComputeBuffer updated with {_pointCount} points (Static/Internal).");
        }
        _isDataDirty = false; // 更新が完了したのでフラグを下ろす
    }

    // システムの破棄時に、割り当てた全GPUバッファ(ComputeBuffer)と参照を適切に解放・クリアする
    public void Cleanup()
    {
        _pointBuffer?.Release();
        _pointBuffer = null;

        _combinedBuffer?.Release();
        _combinedBuffer = null;

        _pointsCache = null;
        _dynamicData = null;
        _staticMeshes.Clear();

        _externalPointBuffer = null;
        _useExternalBuffer = false;
    }
}
