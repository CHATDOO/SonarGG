using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SonarAPOSettingsControl;

namespace SonarGG;

[ComImport]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

public class ApoController : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectDelegate(ref Guid rclsid, ref Guid riid, out IntPtr ppv);

    private static readonly Guid _agsRclsid = new Guid("DEA05346-7BE3-4DB0-AE9F-14423648EA7B");
    private static readonly Guid _classFactoryRiid = new Guid("00000001-0000-0000-C000-000000000046");
    private static readonly Guid _agsRiid = new Guid("40076C40-0E9C-4D14-9CF6-34839A766B8A");

    private IntPtr _hApoModule = IntPtr.Zero;
    private readonly Dictionary<string, IStore> _channelStores = new();
    private readonly Dictionary<string, BasicControl> _channelControls = new();

    public static readonly string[] Channels = { "Game", "ChatRender", "ChatCapture", "Media", "Aux" };

    public bool IsInitialized { get; private set; }

    public bool Initialize()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localApoDir = Path.Combine(baseDir, "drivers", "apoDriverPackage");
            string localApoDll = Path.Combine(localApoDir, "Sonar.APOAPI.dll");

            string apoApiDll;
            if (File.Exists(localApoDll))
            {
                SetDllDirectory(localApoDir);
                apoApiDll = localApoDll;
            }
            else
            {
                SetDllDirectory(@"C:\Program Files\SteelSeries\GG\apps\sonar");
                apoApiDll = @"C:\Program Files\SteelSeries\GG\apps\sonar\Sonar.APOAPI.dll";
                if (!File.Exists(apoApiDll))
                {
                    apoApiDll = @"C:\Program Files\SteelSeries\GG\apps\sonar\driver\apoDriverPackage\Sonar.APOAPI.dll";
                }
            }

            _hApoModule = LoadLibrary(apoApiDll);
            if (_hApoModule == IntPtr.Zero) return false;

            IntPtr proc = GetProcAddress(_hApoModule, "DllGetClassObject");
            if (proc == IntPtr.Zero) return false;

            var getClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(proc);

            Guid rclsid = _agsRclsid;
            Guid cfRiid = _classFactoryRiid;
            int hr = getClassObject(ref rclsid, ref cfRiid, out IntPtr pFactory);
            if (hr != 0 || pFactory == IntPtr.Zero) return false;

            var factory = (IClassFactory)Marshal.GetObjectForIUnknown(pFactory);

            foreach (var ch in Channels)
            {
                Guid agsRiid = _agsRiid;
                hr = factory.CreateInstance(null, ref agsRiid, out IntPtr pControl);
                if (hr == 0 && pControl != IntPtr.Zero)
                {
                    var ctrl = (BasicControl)Marshal.GetObjectForIUnknown(pControl);
                    ctrl.Initialize(ch);
                    ctrl.OpenStore(out IStore store);
                    _channelControls[ch] = ctrl;
                    _channelStores[ch] = store;
                }
            }

            IsInitialized = _channelStores.Count > 0;
            if (IsInitialized)
            {
                // Désactivation forcée et définitive de toute l'IA ClearCast / DirectML
                SetClearCastAi(false, 0f);
                SetImpactNoise(false, 0f);
            }
            return IsInitialized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("APO Init Exception: " + ex);
            return false;
        }
    }

    public ISetting? GetSetting(string channel, string settingName)
    {
        if (!_channelStores.TryGetValue(channel, out var store)) return null;
        try
        {
            store.GetSettingCount(out int count);
            for (int i = 0; i < count; i++)
            {
                store.GetSetting(i, out ISetting s);
                s.GetName(out string name);
                if (name.Equals(settingName, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
        }
        catch { }
        return null;
    }

    // =====================================================================
    // Volume & Mute Controls
    // =====================================================================
    public void SetVolume(string channel, float linearGain, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionGainLin2" : "kSet_CaptureStreamRedirectionGainLin")
            : (isStreamMix ? "kSet_StreamRedirectionGainLin2" : "kSet_StreamRedirectionGainLin");
        var setting = GetSetting(channel, propName);
        if (setting != null)
        {
            setting.SetFloatSetting(linearGain);
        }
    }

    public float GetVolume(string channel, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionGainLin2" : "kSet_CaptureStreamRedirectionGainLin")
            : (isStreamMix ? "kSet_StreamRedirectionGainLin2" : "kSet_StreamRedirectionGainLin");
        var setting = GetSetting(channel, propName);
        if (setting != null)
        {
            setting.GetFloatSetting(out float val);
            return val;
        }
        return 1.0f;
    }

    public void SetMute(string channel, bool isMuted, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionMute2" : "kSet_CaptureStreamRedirectionMute")
            : (isStreamMix ? "kSet_StreamRedirectionMute2" : "kSet_StreamRedirectionMute");
        var setting = GetSetting(channel, propName);
        if (setting != null)
        {
            setting.SetBoolSetting(isMuted);
        }
    }

    public bool GetMute(string channel, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionMute2" : "kSet_CaptureStreamRedirectionMute")
            : (isStreamMix ? "kSet_StreamRedirectionMute2" : "kSet_StreamRedirectionMute");
        var setting = GetSetting(channel, propName);
        if (setting != null)
        {
            setting.GetBoolSetting(out bool val);
            return val;
        }
        return false;
    }

    // =====================================================================
    // ChatMix
    // =====================================================================
    public void SetChatMix(float balance)
    {
        // balance: -1.0 (100% Game, 0% Chat) to 0.0 (100%/100%) to +1.0 (0% Game, 100% Chat)
        float gameLin = 1.0f;
        float chatLin = 1.0f;
        if (balance < 0)
        {
            chatLin = Math.Max(0.0f, 1.0f + balance);
        }
        else if (balance > 0)
        {
            gameLin = Math.Max(0.0f, 1.0f - balance);
        }

        var sGame = GetSetting("Game", "kSet_ChatMixGainLin");
        sGame?.SetFloatSetting(gameLin);

        var sChat = GetSetting("ChatRender", "kSet_ChatMixGainLin");
        sChat?.SetFloatSetting(chatLin);
    }

    // =====================================================================
    // Enhancements (Bass Boost, Voice Clarity, Treble Boost)
    // =====================================================================
    public void SetBassBoost(string channel, bool enabled, float gainDb)
    {
        GetSetting(channel, "kSet_BassBoostState")?.SetBoolSetting(enabled);
        GetSetting(channel, "kSet_BassBoostGainDB")?.SetFloatSetting(gainDb);
    }

    public void SetVoiceClarity(string channel, bool enabled, float gainDb)
    {
        GetSetting(channel, "kSet_VoiceClarityState")?.SetBoolSetting(enabled);
        GetSetting(channel, "kSet_VoiceClarityGainDB")?.SetFloatSetting(gainDb);
    }

    public void SetTrebleBoost(string channel, bool enabled, float gainDb)
    {
        GetSetting(channel, "kSet_TrebleBoostState")?.SetBoolSetting(enabled);
        GetSetting(channel, "kSet_TrebleBoostGainDB")?.SetFloatSetting(gainDb);
    }

    // =====================================================================
    // 10-Band Parametric Equalizer
    // =====================================================================
    public void SetEqFilter(string channel, int filterIndex, bool enabled, float gainDb, float freqHz = 0)
    {
        if (filterIndex < 1 || filterIndex > 10) return;
        string prefix = channel == "ChatCapture" ? "kSet_CaptureParametricEqFilter" : "kSet_ParametricEqFilter";
        
        GetSetting(channel, $"{prefix}{filterIndex}State")?.SetBoolSetting(enabled);
        GetSetting(channel, $"{prefix}{filterIndex}GainDb")?.SetFloatSetting(gainDb);
        if (freqHz > 0)
        {
            GetSetting(channel, $"{prefix}{filterIndex}FreqHz")?.SetFloatSetting(freqHz);
        }
    }

    // =====================================================================
    // ClearCast AI & Noise Suppression (ChatCapture / Mic)
    // =====================================================================
    public void SetClearCastAi(bool enabled, float mixFactor)
    {
        GetSetting("ChatCapture", "kSet_CaptureNoiseReductionAIState")?.SetBoolSetting(enabled);
        GetSetting("ChatCapture", "kSet_CaptureNoiseReductionAIMixFactor")?.SetFloatSetting(mixFactor);
    }

    public void SetImpactNoise(bool enabled, float aggressiveness)
    {
        GetSetting("ChatCapture", "kSet_CaptureImpactNoiseReductionState")?.SetBoolSetting(enabled);
        GetSetting("ChatCapture", "kSet_CaptureImpactNoiseReductionAggressiveness")?.SetFloatSetting(aggressiveness);
    }

    public void SetNoiseGate(bool enabled, float thresholdDb)
    {
        GetSetting("ChatCapture", "kSet_CaptureNoiseGateState")?.SetBoolSetting(enabled);
        GetSetting("ChatCapture", "kSet_CaptureNoiseGateThresholdDB")?.SetFloatSetting(thresholdDb);
    }

    public void SetCaptureCompressor(bool enabled, float thresholdDb)
    {
        GetSetting("ChatCapture", "kSet_CaptureCompressorState")?.SetBoolSetting(enabled);
        GetSetting("ChatCapture", "kSet_CaptureCompressorThresholdDB")?.SetFloatSetting(thresholdDb);
    }

    public void SetCaptureHighPass(float attenuation)
    {
        GetSetting("ChatCapture", "kSet_CaptureParamEqHighPassAttenuation")?.SetFloatSetting(attenuation);
    }

    public void SetCaptureInputDevice(string deviceId)
    {
        GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionDeviceId")?.SetStringSetting(deviceId ?? string.Empty);
    }

    public string GetCaptureInputDevice()
    {
        var s = GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionDeviceId");
        if (s != null)
        {
            s.GetStringSetting(out string val);
            return val ?? string.Empty;
        }
        return string.Empty;
    }

    // =====================================================================
    // Spatial Audio (7.1 Virtual Surround)
    // =====================================================================
    public void SetVirtualSurround(string channel, bool enabled, float reverbDb)
    {
        GetSetting(channel, "kSet_AdvancedVirtualSurroundState")?.SetBoolSetting(enabled);
        GetSetting(channel, "kSet_ReverbGainDB")?.SetFloatSetting(reverbDb);
    }

    // =====================================================================
    // Hardware Audio Device Routing
    // =====================================================================
    public void SetOutputDevice(string channel, string deviceId, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionDeviceId2" : "kSet_CaptureStreamRedirectionDeviceId")
            : (isStreamMix ? "kSet_StreamRedirectionDeviceId2" : "kSet_StreamRedirectionDeviceId");
        GetSetting(channel, propName)?.SetStringSetting(deviceId ?? string.Empty);
    }

    public string GetOutputDevice(string channel, bool isStreamMix = false)
    {
        string propName = channel == "ChatCapture"
            ? (isStreamMix ? "kSet_CaptureStreamRedirectionDeviceId2" : "kSet_CaptureStreamRedirectionDeviceId")
            : (isStreamMix ? "kSet_StreamRedirectionDeviceId2" : "kSet_StreamRedirectionDeviceId");
        var s = GetSetting(channel, propName);
        if (s != null)
        {
            s.GetStringSetting(out string val);
            return val ?? string.Empty;
        }
        return string.Empty;
    }

    public float GetChatMix()
    {
        var sGame = GetSetting("Game", "kSet_ChatMixGainLin");
        var sChat = GetSetting("ChatRender", "kSet_ChatMixGainLin");
        float gameLin = 1.0f;
        float chatLin = 1.0f;
        sGame?.GetFloatSetting(out gameLin);
        sChat?.GetFloatSetting(out chatLin);

        if (gameLin < 0.999f)
        {
            return 1.0f - gameLin;
        }
        else if (chatLin < 0.999f)
        {
            return chatLin - 1.0f;
        }
        return 0.0f;
    }

    public void GetBassBoost(string channel, out bool enabled, out float gainDb)
    {
        enabled = false;
        gainDb = 0f;
        GetSetting(channel, "kSet_BassBoostState")?.GetBoolSetting(out enabled);
        GetSetting(channel, "kSet_BassBoostGainDB")?.GetFloatSetting(out gainDb);
    }

    public void GetVoiceClarity(string channel, out bool enabled, out float gainDb)
    {
        enabled = false;
        gainDb = 0f;
        GetSetting(channel, "kSet_VoiceClarityState")?.GetBoolSetting(out enabled);
        GetSetting(channel, "kSet_VoiceClarityGainDB")?.GetFloatSetting(out gainDb);
    }

    public void GetTrebleBoost(string channel, out bool enabled, out float gainDb)
    {
        enabled = false;
        gainDb = 0f;
        GetSetting(channel, "kSet_TrebleBoostState")?.GetBoolSetting(out enabled);
        GetSetting(channel, "kSet_TrebleBoostGainDB")?.GetFloatSetting(out gainDb);
    }

    public void GetEqFilter(string channel, int filterIndex, out bool enabled, out float gainDb, out float freqHz)
    {
        enabled = false;
        gainDb = 0f;
        freqHz = 1000f;
        if (filterIndex < 1 || filterIndex > 10) return;
        string prefix = channel == "ChatCapture" ? "kSet_CaptureParametricEqFilter" : "kSet_ParametricEqFilter";

        GetSetting(channel, $"{prefix}{filterIndex}State")?.GetBoolSetting(out enabled);
        GetSetting(channel, $"{prefix}{filterIndex}GainDb")?.GetFloatSetting(out gainDb);
        GetSetting(channel, $"{prefix}{filterIndex}FreqHz")?.GetFloatSetting(out freqHz);
    }

    public void GetClearCastAi(out bool enabled, out float mixFactor)
    {
        enabled = false;
        mixFactor = 0.75f;
        GetSetting("ChatCapture", "kSet_CaptureNoiseReductionAIState")?.GetBoolSetting(out enabled);
        GetSetting("ChatCapture", "kSet_CaptureNoiseReductionAIMixFactor")?.GetFloatSetting(out mixFactor);
    }

    public void GetImpactNoise(out bool enabled, out float aggressiveness)
    {
        enabled = false;
        aggressiveness = 0.9f;
        GetSetting("ChatCapture", "kSet_CaptureImpactNoiseReductionState")?.GetBoolSetting(out enabled);
        GetSetting("ChatCapture", "kSet_CaptureImpactNoiseReductionAggressiveness")?.GetFloatSetting(out aggressiveness);
    }

    public void GetNoiseGate(out bool enabled, out float thresholdDb)
    {
        enabled = false;
        thresholdDb = -44f;
        GetSetting("ChatCapture", "kSet_CaptureNoiseGateState")?.GetBoolSetting(out enabled);
        GetSetting("ChatCapture", "kSet_CaptureNoiseGateThresholdDB")?.GetFloatSetting(out thresholdDb);
    }

    public void GetCaptureCompressor(out bool enabled, out float thresholdDb)
    {
        enabled = false;
        thresholdDb = -18f;
        GetSetting("ChatCapture", "kSet_CaptureCompressorState")?.GetBoolSetting(out enabled);
        GetSetting("ChatCapture", "kSet_CaptureCompressorThresholdDB")?.GetFloatSetting(out thresholdDb);
    }

    public void GetVirtualSurround(string channel, out bool enabled, out float reverbDb)
    {
        enabled = false;
        reverbDb = -6f;
        GetSetting(channel, "kSet_AdvancedVirtualSurroundState")?.GetBoolSetting(out enabled);
        GetSetting(channel, "kSet_ReverbGainDB")?.GetFloatSetting(out reverbDb);
    }

    public static int KillGGProcesses()
    {
        string[] targets = { "SteelSeriesGG", "SteelSeriesGGClient", "SteelSeriesSonar", "SteelSeriesEngine" };
        int killed = 0;
        foreach (var name in targets)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                    killed++;
                }
                catch { }
            }
        }
        return killed;
    }

    public void Dispose()
    {
        _channelStores.Clear();
        _channelControls.Clear();
    }
}
