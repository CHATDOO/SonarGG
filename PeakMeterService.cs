using System;
using NAudio.CoreAudioApi;

namespace SonarGG;

/// <summary>
/// Service de mesure de crête audio (Peak / VU Meter) en temps réel.
/// Interroge directement les endpoints virtuels Sonar via WASAPI CoreAudio
/// avec calcul de retombée balistique fluide (attack rapide, decay doux).
/// </summary>
public class PeakMeterService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    private MMDevice? _devGame;
    private MMDevice? _devChat;
    private MMDevice? _devMedia;
    private MMDevice? _devAux;
    private MMDevice? _devMic;

    // Niveaux lissés pour affichage fluide (0.0f à 1.0f)
    public float SmoothedGame { get; private set; }
    public float SmoothedChat { get; private set; }
    public float SmoothedMedia { get; private set; }
    public float SmoothedAux { get; private set; }
    public float SmoothedMic { get; private set; }

    public void Initialize()
    {
        try
        {
            var renderEndpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in renderEndpoints)
            {
                string name = d.FriendlyName.ToLowerInvariant();
                if (name.Contains("sonar"))
                {
                    if (name.Contains("gaming") || name.Contains("game"))
                        _devGame = d;
                    else if (name.Contains("chat"))
                        _devChat = d;
                    else if (name.Contains("media"))
                        _devMedia = d;
                    else if (name.Contains("aux"))
                        _devAux = d;
                }
            }

            var captureEndpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var d in captureEndpoints)
            {
                string name = d.FriendlyName.ToLowerInvariant();
                if (name.Contains("sonar") && (name.Contains("mic") || name.Contains("microphone")))
                {
                    _devMic = d;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log("PeakMeterService Init Warning: " + ex.Message);
        }
    }

    /// <summary>
    /// Met à jour les valeurs instantanées et applique l'atténuation (decay) fluide à ~30-60 FPS.
    /// </summary>
    public void Update()
    {
        SmoothedGame = ComputeSmooth(SmoothedGame, ReadPeak(_devGame));
        SmoothedChat = ComputeSmooth(SmoothedChat, ReadPeak(_devChat));
        SmoothedMedia = ComputeSmooth(SmoothedMedia, ReadPeak(_devMedia));
        SmoothedAux = ComputeSmooth(SmoothedAux, ReadPeak(_devAux));
        SmoothedMic = ComputeSmooth(SmoothedMic, ReadPeak(_devMic));
    }

    private static float ReadPeak(MMDevice? dev)
    {
        if (dev == null) return 0f;
        try
        {
            return dev.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0f;
        }
    }

    private static float ComputeSmooth(float current, float raw)
    {
        if (raw >= current)
        {
            // Attaque instantanée
            return raw;
        }
        // Retombée douce (balistique VU meter pro)
        float decayed = current * 0.85f;
        return decayed < 0.005f ? 0f : decayed;
    }

    public void Dispose()
    {
        try
        {
            _devGame?.Dispose();
            _devChat?.Dispose();
            _devMedia?.Dispose();
            _devAux?.Dispose();
            _devMic?.Dispose();
            _enumerator.Dispose();
        }
        catch { }
    }
}
