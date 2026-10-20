using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace SonarGG;

public class AudioDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsCapture { get; set; }

    public override string ToString() => Name;
}

public static class DeviceManager
{
    public static List<AudioDeviceInfo> GetPlaybackDevices()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in devices)
            {
                // Skip virtual Sonar endpoints so user sees real hardware
                if (d.FriendlyName.Contains("SteelSeries Sonar")) continue;
                list.Add(new AudioDeviceInfo { Id = d.ID, Name = d.FriendlyName, IsCapture = false });
            }
        }
        catch { }
        return list;
    }

    public static List<AudioDeviceInfo> GetCaptureDevices()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var d in devices)
            {
                if (d.FriendlyName.Contains("SteelSeries Sonar")) continue;
                list.Add(new AudioDeviceInfo { Id = d.ID, Name = d.FriendlyName, IsCapture = true });
            }
        }
        catch { }
        return list;
    }
}
