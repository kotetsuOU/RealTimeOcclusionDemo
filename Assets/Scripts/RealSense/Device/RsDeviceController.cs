using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RsDeviceController : MonoBehaviour
{
    [SerializeField] private Vector3 rsScanRange = new Vector3(0.72f, 0.74f, 0.52f);
    [SerializeField] private float frameWidth = 0.02f;
    [SerializeField] private float extraLength = 0.05f;
    [SerializeField] private int rsProcessIntervalFrames = 2;

    public bool adaptIntervalFrame = false;

    // Start is called before the first frame update
    void Start()
    {
        if (adaptIntervalFrame)
        {
            var pipes = FindObjectsByType<RsProcessingPipe>(FindObjectsSortMode.None);
            foreach (var pipe in pipes)
            {
                pipe.SetProcessIntervalFrames(rsProcessIntervalFrames);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
    }

    public Vector3 RealSenseScanRange
    {
        get { return rsScanRange; }
    }

    public float FrameWidth
    {
        get { return frameWidth + extraLength; }
    }
}