using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResponseSpeedIndicator : MonoBehaviour
{
    private int frameCount = 0;
    private float elapsedTime = 0f;
    private float frequency = 0f;

    public bool isEnabled = false;

    void Update()
    {
        if (isEnabled)
        {
            frameCount++;
            elapsedTime += Time.unscaledDeltaTime;

            if (elapsedTime >= 5.0f)
            {
                frequency = frameCount / elapsedTime;
                frameCount = 0;
                elapsedTime = 0f;
                UnityEngine.Debug.Log($"ResponseFrequency : {frequency:F3}");
            }
        }
    }
}
