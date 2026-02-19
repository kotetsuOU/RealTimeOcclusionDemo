using UnityEngine;

public class RsHandMeshBlockSource : MonoBehaviour
{
    [Header("Pipe")]
    [Tooltip("Assign the scene instance of RsProcessingPipe (a Component), not the script file.")]
    public RsProcessingPipe Pipe;

    [Tooltip("If true, searches all RsProcessingPipe instances in the scene when the specified Pipe doesn't contain the target block.")]
    public bool SearchAllPipesInScene = true;

    [Header("Debug")]
    public bool LogOnce;

    [Header("Block Selection")]
    [Tooltip("Type name to resolve from Pipe.profile._processingBlocks (e.g., 'RsHandMeshBlock' or 'RsIntegratedPointCloud').")]
    public string BlockTypeName = "RsHandMeshBlock";

    bool _logged;
    bool _loggedDump;
    float _nextRetryTime;
    bool _loggedFirstData;

    public RsProcessingBlock Block { get; private set; }

    System.Type _blockType;
    System.Reflection.PropertyInfo _pPositions;
    System.Reflection.PropertyInfo _pColors;
    System.Reflection.PropertyInfo _pIndices;
    System.Reflection.PropertyInfo _pIndexCount;

    void Awake() => Resolve();
    void OnEnable() => Resolve();

    System.Collections.IEnumerator Start()
    {
        // Wait until pipes have executed Awake/OnSourceStart and instantiated runtime blocks.
        yield return null;
        yield return new WaitForEndOfFrame();
        Resolve();
    }

    void Update()
    {
        if (Block == null && Time.unscaledTime >= _nextRetryTime)
        {
            _nextRetryTime = Time.unscaledTime + 1f;
            Resolve();
        }

        if (LogOnce && !_loggedFirstData && LatestIndexCount > 0)
        {
            _loggedFirstData = true;
            Debug.Log($"[RsHandMeshBlockSource] First data received. indices={LatestIndexCount} pipe={Pipe} block={Block}");
        }
    }

    public void Resolve()
    {
        if (Pipe == null)
            Pipe = GetComponentInParent<RsProcessingPipe>(true);

        // First try the specified pipe
        if (Pipe != null)
        {
            Block = ResolveBlock(Pipe, BlockTypeName);
        }

        // If not found and SearchAllPipesInScene is enabled, search all pipes in scene
        if (Block == null && SearchAllPipesInScene)
        {
            var allPipes = FindObjectsByType<RsProcessingPipe>(FindObjectsSortMode.None);
            foreach (var pipe in allPipes)
            {
                Block = ResolveBlockDirect(pipe, BlockTypeName);
                if (Block != null)
                {
                    if (LogOnce && !_logged)
                    {
                        Debug.Log($"[RsHandMeshBlockSource] Found block in different pipe. originalPipe={Pipe} foundInPipe={pipe} block={Block.name}");
                    }
                    break;
                }
            }
        }

        if (LogOnce && !_logged && Block != null)
        {
            _logged = true;
            Debug.Log($"[RsHandMeshBlockSource] Resolved. pipe={Pipe} block={Block.name}");
        }

        if (LogOnce && !_loggedDump && Block == null)
        {
            _loggedDump = true;
            var blocks = Pipe != null ? Pipe.RuntimeBlocks : null;
            var blocksCount = blocks != null ? blocks.Count : -1;
            var msg = $"[RsHandMeshBlockSource] Resolve failed. pipe={Pipe} runtimeBlocksCount={blocksCount} searchingFor={BlockTypeName}";
            if (blocks != null)
            {
                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    var b = blocks[bi];
                    msg += $"\n  [{bi}] {(b != null ? b.GetType().Name : "<null>")} enabled={(b != null ? b.Enabled.ToString() : "-")}";
                }
            }
            Debug.LogWarning(msg);
        }
    }

    public Vector3[] LatestPositions => GetValue<Vector3[]>("LatestPositions");
    public Color[] LatestColors => GetValue<Color[]>("LatestColors");
    public int[] LatestIndices => GetValue<int[]>("LatestIndices");
    public int LatestIndexCount => GetValue<int>("LatestIndexCount");

    T GetValue<T>(string prop)
    {
        if (Block == null) return default;
        var p = Block.GetType().GetProperty(prop, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (p == null) return default;
        var v = p.GetValue(Block, null);
        return v is T t ? t : default;
    }

    static RsProcessingBlock ResolveBlock(RsProcessingPipe pipe, string typeName)
    {
        if (pipe == null) return null;
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        var found = ResolveBlockDirect(pipe, typeName);
        if (found != null) return found;

        // Recursively search in Source pipe chain
        if (pipe.Source is RsProcessingPipe sourcePipe)
        {
            found = ResolveBlock(sourcePipe, typeName);
            if (found != null)
                return found;
        }

        return null;
    }

    static RsProcessingBlock ResolveBlockDirect(RsProcessingPipe pipe, string typeName)
    {
        if (pipe == null) return null;
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        // Use RuntimeBlocks property which returns the cloned blocks after Awake()
        var runtimeBlocks = pipe.RuntimeBlocks;
        if (runtimeBlocks != null)
        {
            foreach (var b in runtimeBlocks)
            {
                if (b == null) continue;
                if (b.GetType().Name == typeName)
                    return b;
            }
        }

        return null;
    }

    // Reflection helpers kept only for legacy/debug, no longer used.
}
