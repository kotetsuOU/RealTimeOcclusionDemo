using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LineEstimation : MonoBehaviour
{
    public string filePath1 = "Assets/HandTrakingData/currentGlobalVerticesRight.txt";
    public string filePath2 = "Assets/HandTrakingData/currentGlobalVerticesLeft.txt";
    public string filePath3 = "Assets/HandTrakingData/currentGlobalVerticesBottom.txt";
    public string filePath4 = "Assets/HandTrakingData/currentGlobalVerticesTop.txt";
    public string outputFilePath = "LineDistanceHistogram.csv";

    public bool useFile1 = true;
    public bool useFile2 = true;
    public bool useFile3 = true;
    public bool useFile4 = true;

    public float fixedRadius = 0.05f;
    public float extendLength = 0.3f;
    public Color cylinderColor = Color.white;

    public float binSize = 0.005f;
    public int numberOfBins = 50;

    private Vector3 centroid;
    private Vector3 direction;
    private GameObject lastCylinder;

    public void ExecuteEstimation()
    {
        ClearCylinder();

        List<Vector3> allPoints = new List<Vector3>();
        if (useFile1) allPoints.AddRange(LoadVerticesFromFile(filePath1));
        if (useFile2) allPoints.AddRange(LoadVerticesFromFile(filePath2));
        if (useFile3) allPoints.AddRange(LoadVerticesFromFile(filePath3));
        if (useFile4) allPoints.AddRange(LoadVerticesFromFile(filePath4));

        if (allPoints.Count < 2)
        {
            UnityEngine.Debug.LogWarning("点が少なすぎて直線推定できません");
            return;
        }

        FitLine(allPoints.ToArray());
        CreateCylinder();
        SaveHistogramToCsv(allPoints);
    }

    public void ClearCylinder()
    {
        if (lastCylinder != null)
        {
            Renderer renderer = lastCylinder.GetComponent<Renderer>();
            if (!UnityEngine.Application.isPlaying && renderer != null && renderer.material != null)
            {
                if (!EditorUtility.IsPersistent(renderer.material))
                {
                    DestroyImmediate(renderer.material);
                }
            }

            if (UnityEngine.Application.isPlaying)
            {
                Destroy(lastCylinder);
            }
            else
            {
                DestroyImmediate(lastCylinder);
            }
            lastCylinder = null;
        }
    }

    private void FitLine(Vector3[] vertices)
    {
        centroid = Vector3.zero;
        foreach (var v in vertices) centroid += v;
        centroid /= vertices.Length;

        float xx = 0, xy = 0, xz = 0;
        float yy = 0, yz = 0, zz = 0;
        foreach (var v in vertices)
        {
            Vector3 r = v - centroid;
            xx += r.x * r.x; xy += r.x * r.y; xz += r.x * r.z;
            yy += r.y * r.y; yz += r.y * r.z; zz += r.z * r.z;
        }

        var cov = new float[3, 3]
        {
            { xx, xy, xz },
            { xy, yy, yz },
            { xz, yz, zz }
        };

        direction = EigenMaxVector(cov).normalized;
        UnityEngine.Debug.Log($"直線方向: {direction}, 通過点: {centroid}");
    }

    private void CreateCylinder()
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "FittedCylinder";
        lastCylinder = cylinder;
        cylinder.transform.parent = this.transform;

        cylinder.transform.position = centroid;
        cylinder.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
        cylinder.transform.localScale = new Vector3(fixedRadius * 2, extendLength / 2, fixedRadius * 2);

        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = renderer.material;
            material.color = cylinderColor;
            material.SetFloat("_Mode", 2);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
    }

    private void SaveHistogramToCsv(List<Vector3> points)
    {
        List<float> distances = new List<float>();
        foreach (var p in points)
        {
            Vector3 vectorA = p - centroid;
            float distance = Vector3.Cross(vectorA, direction).magnitude;
            distances.Add(distance);
        }

        int[] bins = new int[numberOfBins];
        foreach (float distance in distances)
        {
            int binIndex = (int)(distance / binSize);
            if (binIndex >= 0 && binIndex < numberOfBins)
            {
                bins[binIndex]++;
            }
        }

        using (StreamWriter writer = new StreamWriter(outputFilePath))
        {
            writer.WriteLine("Distance Range,Point Count");
            for (int i = 0; i < numberOfBins; i++)
            {
                float lowerBound = i * binSize;
                float upperBound = lowerBound + binSize;
                string distanceRange = $"{lowerBound:F3}-{upperBound:F3}";
                writer.WriteLine($"{distanceRange},{bins[i]}");
            }
        }
        UnityEngine.Debug.Log($"直線からの距離ヒストグラムデータを {outputFilePath} に保存しました。");
    }

    private Vector3 EigenMaxVector(float[,] cov)
    {
        Vector3 v = new Vector3(1, 0, 0);
        for (int i = 0; i < 20; i++)
        {
            Vector3 v2 = new Vector3(
                cov[0, 0] * v.x + cov[0, 1] * v.y + cov[0, 2] * v.z,
                cov[1, 0] * v.x + cov[1, 1] * v.y + cov[1, 2] * v.z,
                cov[2, 0] * v.x + cov[2, 1] * v.y + cov[2, 2] * v.z
            );
            v = v2.normalized;
        }
        return v.normalized;
    }

    private Vector3[] LoadVerticesFromFile(string path)
    {
        if (!File.Exists(path)) return new Vector3[0];
        List<Vector3> vertices = new List<Vector3>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(',');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                vertices.Add(new Vector3(x, y, z));
            }
        }
        return vertices.ToArray();
    }
}


#if UNITY_EDITOR
[CustomEditor(typeof(LineEstimation))]
public class LineEstimationEditor : Editor
{
    private bool pointCloudFilesFold = true;
    private bool optionsFold = true;
    private bool cylinderOptionsFold = true;
    private bool histogramOptionsFold = true;

    private SerializedProperty filePath1, filePath2, filePath3, filePath4, outputFilePath;
    private SerializedProperty useFile1, useFile2, useFile3, useFile4;
    private SerializedProperty fixedRadius, extendLength, cylinderColor;
    private SerializedProperty binSize, numberOfBins;

    private void OnEnable()
    {
        pointCloudFilesFold = EditorPrefs.GetBool(nameof(pointCloudFilesFold), true);
        optionsFold = EditorPrefs.GetBool(nameof(optionsFold), true);
        cylinderOptionsFold = EditorPrefs.GetBool(nameof(cylinderOptionsFold), true);
        histogramOptionsFold = EditorPrefs.GetBool(nameof(histogramOptionsFold), true);

        filePath1 = serializedObject.FindProperty("filePath1");
        filePath2 = serializedObject.FindProperty("filePath2");
        filePath3 = serializedObject.FindProperty("filePath3");
        filePath4 = serializedObject.FindProperty("filePath4");
        outputFilePath = serializedObject.FindProperty("outputFilePath");

        useFile1 = serializedObject.FindProperty("useFile1");
        useFile2 = serializedObject.FindProperty("useFile2");
        useFile3 = serializedObject.FindProperty("useFile3");
        useFile4 = serializedObject.FindProperty("useFile4");
        
        fixedRadius = serializedObject.FindProperty("fixedRadius");
        extendLength = serializedObject.FindProperty("extendLength");
        cylinderColor = serializedObject.FindProperty("cylinderColor");

        binSize = serializedObject.FindProperty("binSize");
        numberOfBins = serializedObject.FindProperty("numberOfBins");
    }

    private void OnDisable()
    {
        EditorPrefs.SetBool(nameof(pointCloudFilesFold), pointCloudFilesFold);
        EditorPrefs.SetBool(nameof(optionsFold), optionsFold);
        EditorPrefs.SetBool(nameof(cylinderOptionsFold), cylinderOptionsFold);
        EditorPrefs.SetBool(nameof(histogramOptionsFold), histogramOptionsFold);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        pointCloudFilesFold = EditorGUILayout.Foldout(pointCloudFilesFold, "Point Cloud Files", true, EditorStyles.foldoutHeader);
        if (pointCloudFilesFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(filePath1);
            EditorGUILayout.PropertyField(filePath2);
            EditorGUILayout.PropertyField(filePath3);
            EditorGUILayout.PropertyField(filePath4);
            EditorGUILayout.PropertyField(outputFilePath);
            EditorGUI.indentLevel--;
        }

        optionsFold = EditorGUILayout.Foldout(optionsFold, "Options", true, EditorStyles.foldoutHeader);
        if (optionsFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(useFile1);
            EditorGUILayout.PropertyField(useFile2);
            EditorGUILayout.PropertyField(useFile3);
            EditorGUILayout.PropertyField(useFile4);
            EditorGUI.indentLevel--;
        }

        cylinderOptionsFold = EditorGUILayout.Foldout(cylinderOptionsFold, "Cylinder Options", true, EditorStyles.foldoutHeader);
        if (cylinderOptionsFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(fixedRadius);
            EditorGUILayout.PropertyField(extendLength);
            EditorGUILayout.PropertyField(cylinderColor);
            EditorGUI.indentLevel--;
        }

        histogramOptionsFold = EditorGUILayout.Foldout(histogramOptionsFold, "Histogram Options", true, EditorStyles.foldoutHeader);
        if (histogramOptionsFold)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(binSize);
            EditorGUILayout.PropertyField(numberOfBins);
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        LineEstimation script = (LineEstimation)target;

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Execute Line Estimation"))
        {
            script.ExecuteEstimation();
        }

        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("Clear Cylinder"))
        {
            script.ClearCylinder();
        }

        GUI.backgroundColor = Color.white;
    }
}
#endif