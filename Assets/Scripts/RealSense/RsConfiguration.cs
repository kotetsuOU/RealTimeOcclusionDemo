using System;
using Intel.RealSense;

[Serializable]
public class RsConfiguration
{
    public enum Mode
    {
        Live,
        Playback,
        Record
    }

    public Mode mode;

    public string RequestedSerialNumber;

    public string PlaybackFile;

    public string RecordPath;

    public RsVideoStreamRequest[] Profiles;

    public Config ToPipelineConfig()
    {
        var cfg = new Config();
        switch (mode)
        {
            case Mode.Live:
                if (!string.IsNullOrEmpty(RequestedSerialNumber))
                    cfg.EnableDevice(RequestedSerialNumber);
                foreach (var p in Profiles)
                    p.Apply(cfg);
                break;
            case Mode.Playback:
                cfg.EnableDeviceFromFile(PlaybackFile);
                break;
            case Mode.Record:
                if (!string.IsNullOrEmpty(RequestedSerialNumber))
                    cfg.EnableDevice(RequestedSerialNumber);
                cfg.EnableRecordToFile(RecordPath);
                foreach (var p in Profiles)
                    p.Apply(cfg);
                break;
        }
        return cfg;
    }
}