using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SonarGG;

/// <summary>
/// Moteur de capture et traitement audio du microphone en temps réel.
/// Fait la passerelle directe entre le micro physique et le périphérique virtuel SonarGG ("SteelSeries Sonar - Microphone"),
/// avec DSP studio matériel (Noise Gate, Filtre coupe-bas 80Hz, Compresseur) et latence ultra-faible.
/// </summary>
public class MicrophoneEngine : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private WasapiCapture? _capture;
    private WasapiOut? _renderToSonar;
    private BufferedWaveProvider? _sonarBuffer;

    private WasapiOut? _sidetoneRender;
    private BufferedWaveProvider? _sidetoneBuffer;

    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public string CurrentCaptureDeviceId { get; private set; } = string.Empty;

    // Paramètres DSP
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = false;

    public bool IsNoiseGateEnabled { get; set; } = true;
    public float NoiseGateThresholdDb { get; set; } = -44.0f;

    public bool IsCompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -18.0f;

    public bool IsHighPassEnabled { get; set; } = true;

    public bool IsSidetoneEnabled { get; set; } = false;
    public float SidetoneVolume { get; set; } = 0.5f;

    // Filtre Passe-Haut 80Hz (Biquad Butterworth 2nd order)
    private float _hpX1 = 0, _hpX2 = 0, _hpY1 = 0, _hpY2 = 0;
    private float _hpB0 = 1, _hpB1 = -2, _hpB2 = 1, _hpA1 = 0, _hpA2 = 0;
    private int _sampleRate = 48000;

    // Noise Gate smoothing
    private float _gateGain = 1.0f;

    // Événement pour le VU-mètre en direct
    public event Action<float>? PeakLevelChanged;

    public void Start(string? captureDeviceId = null, string? sidetonePlaybackDeviceId = null)
    {
        lock (_lock)
        {
            Stop();

            try
            {
                MMDevice? captureDev = null;
                if (!string.IsNullOrEmpty(captureDeviceId))
                {
                    try { captureDev = _enumerator.GetDevice(captureDeviceId); } catch { }
                }

                if (captureDev == null)
                {
                    // Fallback : premier micro physique valide (hors Sonar)
                    foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    {
                        if (!d.FriendlyName.Contains("Sonar"))
                        {
                            captureDev = d;
                            break;
                        }
                    }
                }

                if (captureDev == null) return;
                CurrentCaptureDeviceId = captureDev.ID;

                // Trouver le périphérique de rendu virtuel Sonar - Microphone
                MMDevice? sonarRenderDev = null;
                foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (d.FriendlyName.Contains("Sonar - Microphone"))
                    {
                        sonarRenderDev = d;
                        break;
                    }
                }

                if (sonarRenderDev == null) return;

                _capture = new WasapiCapture(captureDev);
                _sampleRate = _capture.WaveFormat.SampleRate;
                InitHighPassCoefficients(_sampleRate, 80.0f);

                _sonarBuffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(100)
                };

                _renderToSonar = new WasapiOut(sonarRenderDev, AudioClientShareMode.Shared, true, 20);
                _renderToSonar.Init(_sonarBuffer.ToSampleProvider());
                _renderToSonar.Play();

                // Sidetone (Retour casque direct) optionnel
                if (!string.IsNullOrEmpty(sidetonePlaybackDeviceId))
                {
                    try
                    {
                        var headDev = _enumerator.GetDevice(sidetonePlaybackDeviceId);
                        if (headDev != null)
                        {
                            _sidetoneBuffer = new BufferedWaveProvider(_capture.WaveFormat)
                            {
                                DiscardOnBufferOverflow = true,
                                BufferDuration = TimeSpan.FromMilliseconds(80)
                            };
                            _sidetoneRender = new WasapiOut(headDev, AudioClientShareMode.Shared, true, 20);
                            _sidetoneRender.Init(_sidetoneBuffer.ToSampleProvider());
                            _sidetoneRender.Play();
                        }
                    }
                    catch { }
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.StartRecording();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                App.Log("MicrophoneEngine Start error: " + ex);
            }
        }
    }

    private unsafe void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _sonarBuffer == null) return;

        int numSamples = e.BytesRecorded / sizeof(float);
        byte[] processedBytes = new byte[e.BytesRecorded];
        byte[] sidetoneBytes = IsSidetoneEnabled ? new byte[e.BytesRecorded] : Array.Empty<byte>();

        float peak = 0.0f;
        float gateLinearThreshold = (float)Math.Pow(10.0, NoiseGateThresholdDb / 20.0);
        float compLinearThreshold = (float)Math.Pow(10.0, CompressorThresholdDb / 20.0);

        fixed (byte* pSrc = e.Buffer, pDst = processedBytes, pSide = sidetoneBytes)
        {
            float* srcSamples = (float*)pSrc;
            float* dstSamples = (float*)pDst;
            float* sideSamples = IsSidetoneEnabled ? (float*)pSide : null;

            for (int i = 0; i < numSamples; i++)
            {
                float s = srcSamples[i];
                float absVal = Math.Abs(s);
                if (absVal > peak) peak = absVal;

                if (IsMuted)
                {
                    dstSamples[i] = 0.0f;
                    if (sideSamples != null) sideSamples[i] = 0.0f;
                    continue;
                }

                // 1. Gain volume
                s *= Volume;

                // 2. High-Pass Filter 80Hz (coupe-bas grondements)
                if (IsHighPassEnabled)
                {
                    float y = _hpB0 * s + _hpB1 * _hpX1 + _hpB2 * _hpX2 - _hpA1 * _hpY1 - _hpA2 * _hpY2;
                    _hpX2 = _hpX1;
                    _hpX1 = s;
                    _hpY2 = _hpY1;
                    _hpY1 = y;
                    s = y;
                }

                // 3. Noise Gate Hardware
                if (IsNoiseGateEnabled)
                {
                    float envelope = Math.Abs(s);
                    float targetGate = envelope > gateLinearThreshold ? 1.0f : 0.0f;
                    // Lissage 20ms
                    _gateGain += (targetGate - _gateGain) * 0.08f;
                    s *= _gateGain;
                }

                // 4. Studio Compressor
                if (IsCompressorEnabled)
                {
                    float env = Math.Abs(s);
                    if (env > compLinearThreshold && compLinearThreshold > 0.0001f)
                    {
                        float excess = env - compLinearThreshold;
                        float compressed = compLinearThreshold + (excess * 0.25f); // 4:1 ratio
                        s = Math.Sign(s) * compressed;
                    }
                }

                // 5. Soft-Limiter / Anti-clipping (-0.1 dB ceiling)
                if (s > 0.98f) s = 0.98f;
                else if (s < -0.98f) s = -0.98f;

                dstSamples[i] = s;
                if (sideSamples != null)
                {
                    sideSamples[i] = s * SidetoneVolume;
                }
            }
        }

        _sonarBuffer.AddSamples(processedBytes, 0, e.BytesRecorded);

        if (IsSidetoneEnabled && _sidetoneBuffer != null && sidetoneBytes.Length > 0)
        {
            _sidetoneBuffer.AddSamples(sidetoneBytes, 0, e.BytesRecorded);
        }

        PeakLevelChanged?.Invoke(Math.Min(1.0f, peak * (IsMuted ? 0f : Volume)));
    }

    private void InitHighPassCoefficients(int sampleRate, float cutoffHz)
    {
        double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cosw0 = Math.Cos(w0);
        double sinw0 = Math.Sin(w0);
        double alpha = sinw0 / (2.0 * 0.7071);

        double a0 = 1.0 + alpha;
        _hpB0 = (float)((1.0 + cosw0) / 2.0 / a0);
        _hpB1 = (float)(-(1.0 + cosw0) / a0);
        _hpB2 = (float)((1.0 + cosw0) / 2.0 / a0);
        _hpA1 = (float)(-2.0 * cosw0 / a0);
        _hpA2 = (float)((1.0 - alpha) / a0);
    }

    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }
                if (_renderToSonar != null)
                {
                    _renderToSonar.Stop();
                    _renderToSonar.Dispose();
                    _renderToSonar = null;
                }
                if (_sidetoneRender != null)
                {
                    _sidetoneRender.Stop();
                    _sidetoneRender.Dispose();
                    _sidetoneRender = null;
                }
                _sonarBuffer = null;
                _sidetoneBuffer = null;
                IsRunning = false;
            }
            catch (Exception ex)
            {
                App.Log("MicrophoneEngine Stop error: " + ex);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }
}
