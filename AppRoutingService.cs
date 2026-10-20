using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;

namespace SonarGG;

/// <summary>
/// Représente une application Windows en cours d'exécution qui émet du son.
/// </summary>
public class AudioAppItem
{
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ImageSource? Icon { get; set; }
    public string CurrentChannel { get; set; } = "Autre"; // "Game", "Chat", "Media", "Aux", "Autre"
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
}

/// <summary>
/// Service de routage audio par application (App Routing & Multi-Source).
/// Utilise l'interface interne Windows 11 WinRT AudioPolicyConfig via RoGetActivationFactory
/// pour rediriger dynamiquement n'importe quel processus (Discord, Spotify, Jeux, Chrome...)
/// vers les endpoints virtuels Sonar Gaming, Chat, Media et Aux.
/// </summary>
public class AppRoutingService : IDisposable
{
    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    private readonly MMDeviceEnumerator _enumerator = new();
    private object? _comPolicyConfig;
    private MethodInfo? _setPersistedMethod;
    private MethodInfo? _getPersistedMethod;

    // Properly typed enum instances for Reflection calls
    private object? _flowRender;
    private object? _roleConsole;
    private object? _roleMultimedia;

    // Full device paths for the 4 virtual render channels
    private string? _gameFullId;
    private string? _chatFullId;
    private string? _mediaFullId;
    private string? _auxFullId;

    // In-memory overrides cache to ensure zero UI delay & persistent routing across tabs
    private readonly Dictionary<string, string> _appProcessNameOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _appPidOverrides = new();

    private static readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable => _comPolicyConfig != null && _setPersistedMethod != null;

    public void Initialize()
    {
        try
        {
            // 1. Initialisation COM Factory Windows.Media.Internal.AudioPolicyConfig
            string classId = "Windows.Media.Internal.AudioPolicyConfig";
            WindowsCreateString(classId, classId.Length, out IntPtr hstr);
            Guid iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
            int hr = RoGetActivationFactory(hstr, ref iid, out IntPtr pFactory);
            WindowsDeleteString(hstr);

            if (hr == 0 && pFactory != IntPtr.Zero)
            {
                _comPolicyConfig = Marshal.GetObjectForIUnknown(pFactory);

                string sonarDll = @"C:\Program Files\SteelSeries\GG\apps\sonar\SoundStage.Interop.Services.dll";
                if (File.Exists(sonarDll))
                {
                    var asm = Assembly.LoadFrom(sonarDll);
                    var ifaceType = asm.GetType("SoundStage.Interop.Services.Interfaces.MMDeviceAPI.IAudioPolicyConfigVariantFor21H2Win11");
                    if (ifaceType != null)
                    {
                        _setPersistedMethod = ifaceType.GetMethod("SetPersistedDefaultAudioEndpoint");
                        _getPersistedMethod = ifaceType.GetMethod("GetPersistedDefaultAudioEndpoint");

                        if (_setPersistedMethod != null)
                        {
                            var pList = _setPersistedMethod.GetParameters();
                            if (pList.Length >= 4)
                            {
                                _flowRender = Enum.ToObject(pList[1].ParameterType, 0); // eRender
                                _roleConsole = Enum.ToObject(pList[2].ParameterType, 0); // eConsole
                                _roleMultimedia = Enum.ToObject(pList[2].ParameterType, 1); // eMultimedia
                            }
                        }
                    }
                }
            }

            // 2. Recherche et mise en cache des identifiants d'endpoints Sonar
            ResolveSonarEndpoints();
        }
        catch (Exception ex)
        {
            App.Log("AppRoutingService Init Warning: " + ex.Message);
        }
    }

    private void ResolveSonarEndpoints()
    {
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in devices)
            {
                string name = d.FriendlyName.ToLowerInvariant();
                string fullId = @"\\?\SWD#MMDEVAPI#" + d.ID + @"#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

                if (name.Contains("sonar"))
                {
                    if (name.Contains("gaming") || name.Contains("game"))
                        _gameFullId = fullId;
                    else if (name.Contains("chat"))
                        _chatFullId = fullId;
                    else if (name.Contains("media"))
                        _mediaFullId = fullId;
                    else if (name.Contains("aux"))
                        _auxFullId = fullId;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log("ResolveSonarEndpoints error: " + ex.Message);
        }
    }

    /// <summary>
    /// Récupère la liste de toutes les applications actives produisant du son dans Windows.
    /// </summary>
    public List<AudioAppItem> GetActiveApplications()
    {
        var result = new List<AudioAppItem>();
        var seenPids = new HashSet<uint>();

        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                AudioSessionManager? sm;
                try { sm = dev.AudioSessionManager; } catch { continue; }
                if (sm == null) continue;

                var sessions = sm.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    uint pid = s.GetProcessID;
                    if (pid == 0 || seenPids.Contains(pid)) continue;

                    string procName = "";
                    string dispName = "";
                    ImageSource? icon = null;

                    try
                    {
                        using var proc = Process.GetProcessById((int)pid);
                        procName = proc.ProcessName;
                        dispName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? proc.MainWindowTitle : procName;

                        // Ignorer les processus système internes
                        if (procName.Equals("audiodg", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("SonarGG", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Récupération de l'icône
                        try
                        {
                            string exePath = proc.MainModule?.FileName ?? "";
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                icon = GetProcessIcon(exePath);
                            }
                        }
                        catch { }
                    }
                    catch
                    {
                        // Processus fermé ou permissions restreintes
                        continue;
                    }

                    seenPids.Add(pid);

                    // Détermination du canal actuel
                    string channel = DetectAppChannel(pid, procName, dev.FriendlyName);

                    result.Add(new AudioAppItem
                    {
                        ProcessId = pid,
                        ProcessName = procName,
                        DisplayName = dispName,
                        Icon = icon,
                        CurrentChannel = channel,
                        Volume = s.SimpleAudioVolume.Volume,
                        IsMuted = s.SimpleAudioVolume.Mute
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log("GetActiveApplications error: " + ex.Message);
        }

        return result;
    }

    private string DetectAppChannel(uint pid, string procName, string devFriendlyName)
    {
        // 1. Priorité absolue : Cache utilisateur immédiat par Process Name ou PID
        if (!string.IsNullOrEmpty(procName) && _appProcessNameOverrides.TryGetValue(procName, out var nameTarget))
        {
            return nameTarget;
        }
        if (_appPidOverrides.TryGetValue(pid, out var pidTarget))
        {
            return pidTarget;
        }

        // 2. Détection via Windows Audio Policy Config persistant
        if (_comPolicyConfig != null && _getPersistedMethod != null && _flowRender != null && _roleConsole != null)
        {
            try
            {
                object[] callArgs = new object[] { pid, _flowRender, _roleConsole, string.Empty };
                _getPersistedMethod.Invoke(_comPolicyConfig, callArgs);
                if (callArgs[3] is string assigned && !string.IsNullOrEmpty(assigned))
                {
                    if (assigned.Equals(_gameFullId, StringComparison.OrdinalIgnoreCase)) return "Game";
                    if (assigned.Equals(_chatFullId, StringComparison.OrdinalIgnoreCase)) return "Chat";
                    if (assigned.Equals(_mediaFullId, StringComparison.OrdinalIgnoreCase)) return "Media";
                    if (assigned.Equals(_auxFullId, StringComparison.OrdinalIgnoreCase)) return "Aux";
                }
            }
            catch { }
        }

        // 3. Repli : Détection via le nom d'endpoint actif dans WASAPI
        string lower = devFriendlyName.ToLowerInvariant();
        if (lower.Contains("sonar"))
        {
            if (lower.Contains("gaming") || lower.Contains("game")) return "Game";
            if (lower.Contains("chat")) return "Chat";
            if (lower.Contains("media")) return "Media";
            if (lower.Contains("aux")) return "Aux";
        }

        return "Autre";
    }

    /// <summary>
    /// Redirige immédiatement une application vers un canal Sonar spécifique ("Game", "Chat", "Media", "Aux").
    /// </summary>
    public bool RouteApp(uint processId, string targetChannel)
    {
        string? targetFullId = targetChannel switch
        {
            "Game" => _gameFullId,
            "Chat" or "ChatRender" => _chatFullId,
            "Media" => _mediaFullId,
            "Aux" => _auxFullId,
            _ => null
        };

        // Enregistrer l'assignation en mémoire immédiatement pour une réactivité instantanée à l'écran
        _appPidOverrides[processId] = targetChannel;

        try
        {
            using var proc = Process.GetProcessById((int)processId);
            string pName = proc.ProcessName;
            if (!string.IsNullOrEmpty(pName))
            {
                _appProcessNameOverrides[pName] = targetChannel;

                // Appliquer à tous les processus frères (ex: tous les onglets/moteurs de Chrome ou Discord)
                var siblings = Process.GetProcessesByName(pName);
                foreach (var sib in siblings)
                {
                    uint sibPid = (uint)sib.Id;
                    _appPidOverrides[sibPid] = targetChannel;
                    ApplyWindowsRouting(sibPid, targetFullId);
                }
                return true;
            }
        }
        catch
        {
            // PID fermé ou non résolu
        }

        return ApplyWindowsRouting(processId, targetFullId);
    }

    private bool ApplyWindowsRouting(uint pid, string? targetFullId)
    {
        if (_comPolicyConfig == null || _setPersistedMethod == null || _flowRender == null || _roleConsole == null || _roleMultimedia == null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(targetFullId)) return false;

        IntPtr hDevId = IntPtr.Zero;
        try
        {
            WindowsCreateString(targetFullId, targetFullId.Length, out hDevId);

            // Role 0 = eConsole (Jeu, général)
            _setPersistedMethod.Invoke(_comPolicyConfig, new object[] { pid, _flowRender, _roleConsole, hDevId });

            // Role 1 = eMultimedia (Musique, vidéo)
            _setPersistedMethod.Invoke(_comPolicyConfig, new object[] { pid, _flowRender, _roleMultimedia, hDevId });

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"ApplyWindowsRouting failed for PID {pid} -> {targetFullId}: {ex.Message}");
            return false;
        }
        finally
        {
            if (hDevId != IntPtr.Zero)
            {
                WindowsDeleteString(hDevId);
            }
        }
    }

    /// <summary>
    /// Modifie le volume audio spécifique d'une application dans le mixer Windows.
    /// </summary>
    public void SetAppVolume(uint processId, float volume)
    {
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                var sm = dev.AudioSessionManager;
                if (sm == null) continue;
                for (int i = 0; i < sm.Sessions.Count; i++)
                {
                    var s = sm.Sessions[i];
                    if (s.GetProcessID == processId)
                    {
                        s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                        return;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Modifie l'état de coupure de son (mute) d'une application.
    /// </summary>
    public void SetAppMute(uint processId, bool isMuted)
    {
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                var sm = dev.AudioSessionManager;
                if (sm == null) continue;
                for (int i = 0; i < sm.Sessions.Count; i++)
                {
                    var s = sm.Sessions[i];
                    if (s.GetProcessID == processId)
                    {
                        s.SimpleAudioVolume.Mute = isMuted;
                        return;
                    }
                }
            }
        }
        catch { }
    }

    private static ImageSource? GetProcessIcon(string processPath)
    {
        if (_iconCache.TryGetValue(processPath, out var cached))
        {
            return cached;
        }

        try
        {
            if (File.Exists(processPath))
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (sysIcon != null)
                {
                    using var bmp = sysIcon.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = ms;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    _iconCache[processPath] = bi;
                    return bi;
                }
            }
        }
        catch { }

        _iconCache[processPath] = null;
        return null;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
