using UnityEngine;

public class RsPointCloudSyntheticData
{
    public enum SyntheticShape { Cylinder, Cube, Sphere }

    private readonly SyntheticShape _shape;
    private readonly int _pointCount;
    private readonly float _scale;
    private readonly int _seed;

    public RsPointCloudSyntheticData(SyntheticShape shape, int pointCount, float scale, int seed = 12345)
    {
        _shape = shape;
        _pointCount = pointCount;
        _scale = scale;
        _seed = seed;
    }

    public Vector3[] Generate()
    {
        Vector3[] vertices = new Vector3[_pointCount];
        Random.InitState(_seed);

        for (int i = 0; i < _pointCount; i++)
        {
            vertices[i] = GeneratePoint();
        }

        return vertices;
    }

    public void GenerateInto(Vector3[] vertices)
    {
        if (vertices == null || vertices.Length == 0) return;

        Random.InitState(_seed);

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = GeneratePoint();
        }
    }

    private Vector3 GeneratePoint()
    {
        switch (_shape)
        {
            case SyntheticShape.Cylinder:
                return GenerateCylinderPoint();
            case SyntheticShape.Cube:
                return GenerateCubePoint();
            case SyntheticShape.Sphere:
                return GenerateSpherePoint();
            default:
                return Vector3.zero;
        }
    }

    private Vector3 GenerateCylinderPoint()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = _scale * 0.5f;
        float height = Random.Range(0f, _scale);
        return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
    }

    private Vector3 GenerateCubePoint()
    {
        return new Vector3(
            Random.Range(-0.5f, 0.5f),
            Random.Range(-0.5f, 0.5f),
            Random.Range(-0.5f, 0.5f)
        ) * _scale;
    }

    private Vector3 GenerateSpherePoint()
    {
        return Random.onUnitSphere * (_scale * 0.5f);
    }
}
