using UnityEngine;

public class RsPointCloudVisualization
{
    private readonly MeshRenderer _renderer;
    private readonly MaterialPropertyBlock _props;

    public RsPointCloudVisualization(MeshRenderer renderer)
    {
        _renderer = renderer;
        _props = new MaterialPropertyBlock();
    }

    public void Draw(ComputeBuffer verticesBuffer, ComputeBuffer argsBuffer, Color pointCloudColor, int layer)
    {
        if (verticesBuffer == null || argsBuffer == null || _renderer == null || _renderer.sharedMaterial == null)
        {
            return;
        }

        _props.SetBuffer("_Vertices", verticesBuffer);
        _props.SetColor("_Color", pointCloudColor);

        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 50f);

        Graphics.DrawProceduralIndirect(
            _renderer.material,
            bounds,
            MeshTopology.Points,
            argsBuffer,
            0,
            null,
            _props,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            layer
        );
    }

    public static void DebugLogFilteredPoints(RsPointCloudCompute compute, int debugPointCount)
    {
        if (compute == null) return;

        Vector3[] debugPoints = new Vector3[debugPointCount];
        compute.GetFilteredVerticesData(debugPoints, debugPointCount);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[RsPointCloudRenderer] First {debugPointCount} Filtered Points (Global):");
        for (int i = 0; i < debugPointCount; i++)
        {
            sb.AppendLine($"  [{i}]: {debugPoints[i].ToString("F4")}");
        }
        Debug.Log(sb.ToString());
    }
}
