using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

[AppLoggable("RealSense (Device)")]
public class RsDeviceController : MonoBehaviour, ISerializationCallbackReceiver
{
    [SerializeField] private Vector3 scanMin = new Vector3(0.07f, 0.07f, 0.07f);
    [SerializeField] private Vector3 scanMax = new Vector3(0.65f, 0.67f, 0.45f);

    [SerializeField] private int rsProcessIntervalFrames = 2;

    // Deprecated fields kept for serialization migration
    [SerializeField, HideInInspector] private Vector3 rsScanRange = Vector3.zero;
    [SerializeField, HideInInspector] private float frameWidth = -1f;
    [SerializeField, HideInInspector] private float extraLength = -1f;

    public bool adaptIntervalFrame = false;

    public Vector3 ScanMin
    {
        get { return scanMin; }
        set { scanMin = value; }
    }

    public Vector3 ScanMax
    {
        get { return scanMax; }
        set { scanMax = value; }
    }

    // Start is called before the first frame update
    void Start()
    {
        if (adaptIntervalFrame)
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IRsProcessIntervalConfigurable configurable)
                {
                    configurable.SetProcessIntervalFrames(rsProcessIntervalFrames);
                }
            }
        }
    }

    [Obsolete("Use ScanMin and ScanMax instead.")]
    public Vector3 RealSenseScanRange
    {
        get { return scanMax + scanMin; }
    }

    [Obsolete("Use ScanMin and ScanMax instead.")]
    public float FrameWidth
    {
        get { return scanMin.x; }
    }

    public void OnBeforeSerialize()
    {
        // Nothing to do before serialization
    }

    public void OnAfterDeserialize()
    {
        // Migrate old values if they exist
        if (rsScanRange != Vector3.zero && frameWidth >= 0)
        {
            float totalFrameWidth = frameWidth + (extraLength >= 0 ? extraLength : 0.05f);
            scanMin = new Vector3(totalFrameWidth, totalFrameWidth, totalFrameWidth);
            scanMax = rsScanRange - scanMin;
            // Clear deprecated fields so migration only runs once
            rsScanRange = Vector3.zero;
            frameWidth = -1f;
            extraLength = -1f;
        }
    }
}
