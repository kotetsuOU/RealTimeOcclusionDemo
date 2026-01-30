using UnityEngine;

public class RsComputeStats
{
    public int FilterCallsPerSec { get; private set; }
    public int CountReadbackSkippedPerSec { get; private set; }
    public int SamplesReadbackSkippedPerSec { get; private set; }

    private int _filterCallsCounter;
    private int _countReadbackSkippedCounter;
    private int _samplesReadbackSkippedCounter;
    private float _lastResetTime;

    public void RecordFilterCall()
    {
        _filterCallsCounter++;
        UpdatePerSecondStats();
    }

    public void RecordCountReadbackSkipped()
    {
        _countReadbackSkippedCounter++;
    }

    public void RecordSamplesReadbackSkipped()
    {
        _samplesReadbackSkippedCounter++;
    }

    private void UpdatePerSecondStats()
    {
        float currentTime = Time.realtimeSinceStartup;
        if (currentTime - _lastResetTime >= 1f)
        {
            FilterCallsPerSec = _filterCallsCounter;
            CountReadbackSkippedPerSec = _countReadbackSkippedCounter;
            SamplesReadbackSkippedPerSec = _samplesReadbackSkippedCounter;

            _filterCallsCounter = 0;
            _countReadbackSkippedCounter = 0;
            _samplesReadbackSkippedCounter = 0;
            _lastResetTime = currentTime;
        }
    }

    public void Reset()
    {
        FilterCallsPerSec = 0;
        CountReadbackSkippedPerSec = 0;
        SamplesReadbackSkippedPerSec = 0;
        _filterCallsCounter = 0;
        _countReadbackSkippedCounter = 0;
        _samplesReadbackSkippedCounter = 0;
        _lastResetTime = Time.realtimeSinceStartup;
    }
}
