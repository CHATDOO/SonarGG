using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SonarGG;

public enum SonarHotkeyAction
{
    ToggleMuteMic = 101,
    ToggleMuteGame = 102,
    ToggleMuteChat = 103,
    ToggleMuteMedia = 104,
    ToggleMuteAux = 105,
    ToggleMiniMode = 106,
    VolumeUp = 107,
    VolumeDown = 108
}

public class HotkeyItem
{
    public SonarHotkeyAction Action { get; set; }
    public string NameFr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescFr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    public string DisplayText => FormatShortcut(Modifiers, VirtualKey);

    public static string FormatShortcut(uint mod, uint vk)
    {
        if (vk == 0) return "Aucun";
        var parts = new List<string>();
        if ((mod & 0x0002) != 0) parts.Add("Ctrl");
        if ((mod & 0x0001) != 0) parts.Add("Alt");
        if ((mod & 0x0004) != 0) parts.Add("Shift");

        Key key = KeyInterop.KeyFromVirtualKey((int)vk);
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}

/// <summary>
/// Service de gestion et personnalisation complète des raccourcis clavier globaux Windows.
/// Les raccourcis sont 100% éditables par l'utilisateur et persistés dans le Registre Windows.
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    private const string RegSubKey = @"Software\SonarGG\Hotkeys";

    private IntPtr _hWnd;
    private HwndSource? _source;

    public event Action<SonarHotkeyAction>? HotkeyPressed;
    public event Action? HotkeysChanged;

    private readonly Dictionary<SonarHotkeyAction, HotkeyItem> _bindings = new();

    public IReadOnlyCollection<HotkeyItem> Bindings => _bindings.Values;

    public void Initialize(IntPtr windowHandle)
    {
        _hWnd = windowHandle;
        _source = HwndSource.FromHwnd(_hWnd);
        _source?.AddHook(HwndHook);

        InitDefaultBindings();
        LoadFromRegistry();
        RegisterAll();
    }

    private void InitDefaultBindings()
    {
        _bindings[SonarHotkeyAction.ToggleMuteMic] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMuteMic,
            NameFr = "Couper / Activer le Microphone",
            NameEn = "Mute / Unmute Microphone",
            DescFr = "Bascule instantanément la coupure du micro pour Discord et les jeux",
            DescEn = "Instantly toggle microphone mute across all games & voice apps",
            Modifiers = MOD_CONTROL | MOD_SHIFT,
            VirtualKey = 0x4D // 'M'
        };

        _bindings[SonarHotkeyAction.ToggleMuteGame] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMuteGame,
            NameFr = "Couper / Activer le Son du Jeu",
            NameEn = "Mute / Unmute Game Audio",
            DescFr = "Mute le canal Jeu Sonar sans toucher au chat vocal",
            DescEn = "Mute game channel without muting team voice chat",
            Modifiers = MOD_CONTROL | MOD_SHIFT,
            VirtualKey = 0x47 // 'G'
        };

        _bindings[SonarHotkeyAction.ToggleMuteChat] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMuteChat,
            NameFr = "Couper / Activer le Chat Vocal",
            NameEn = "Mute / Unmute Voice Chat",
            DescFr = "Coupe le son des coéquipiers Discord ou du jeu",
            DescEn = "Silence teammate voice chat instantly",
            Modifiers = MOD_CONTROL | MOD_SHIFT,
            VirtualKey = 0x43 // 'C'
        };

        _bindings[SonarHotkeyAction.ToggleMuteMedia] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMuteMedia,
            NameFr = "Couper / Activer la Musique (Média)",
            NameEn = "Mute / Unmute Media",
            DescFr = "Silence immédiat pour Spotify, YouTube ou Chrome",
            DescEn = "Instant silence for Spotify, YouTube or web audio",
            Modifiers = MOD_CONTROL | MOD_SHIFT,
            VirtualKey = 0x44 // 'D'
        };

        _bindings[SonarHotkeyAction.ToggleMuteAux] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMuteAux,
            NameFr = "Couper / Activer le Canal Auxiliaire",
            NameEn = "Mute / Unmute Aux Channel",
            DescFr = "Mute rapide du canal secondaire ou des alertes",
            DescEn = "Quick mute for alerts or secondary streams",
            Modifiers = MOD_CONTROL | MOD_SHIFT,
            VirtualKey = 0x41 // 'A'
        };

        _bindings[SonarHotkeyAction.ToggleMiniMode] = new HotkeyItem
        {
            Action = SonarHotkeyAction.ToggleMiniMode,
            NameFr = "Basculer Mode Mini / Plein Écran",
            NameEn = "Toggle Mini Mode / Studio Mode",
            DescFr = "Passe instantanément d'une vue à l'autre en un éclair",
            DescEn = "Switch between ultra-compact mini widget and full studio",
            Modifiers = MOD_CONTROL,
            VirtualKey = 0x4D // 'M'
        };

        _bindings[SonarHotkeyAction.VolumeUp] = new HotkeyItem
        {
            Action = SonarHotkeyAction.VolumeUp,
            NameFr = "Volume Général +5%",
            NameEn = "Master Volume +5%",
            DescFr = "Augmente le volume du casque en jeu",
            DescEn = "Increase headset volume without leaving your game",
            Modifiers = MOD_CONTROL | MOD_ALT,
            VirtualKey = 0x26 // VK_UP
        };

        _bindings[SonarHotkeyAction.VolumeDown] = new HotkeyItem
        {
            Action = SonarHotkeyAction.VolumeDown,
            NameFr = "Volume Général -5%",
            NameEn = "Master Volume -5%",
            DescFr = "Diminue le volume du casque en jeu",
            DescEn = "Decrease headset volume without leaving your game",
            Modifiers = MOD_CONTROL | MOD_ALT,
            VirtualKey = 0x28 // VK_DOWN
        };
    }

    public HotkeyItem? GetBinding(SonarHotkeyAction action)
    {
        return _bindings.TryGetValue(action, out var item) ? item : null;
    }

    public bool UpdateBinding(SonarHotkeyAction action, uint modifiers, uint virtualKey)
    {
        if (!_bindings.TryGetValue(action, out var item)) return false;

        UnregisterHotKey(_hWnd, (int)action);

        item.Modifiers = modifiers;
        item.VirtualKey = virtualKey;

        bool ok = RegisterHotKey(_hWnd, (int)action, modifiers | MOD_NOREPEAT, virtualKey);
        SaveToRegistry(item);
        HotkeysChanged?.Invoke();
        return ok;
    }

    public void ResetToDefaults()
    {
        UnregisterAll();
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegSubKey, false);
        }
        catch { }

        InitDefaultBindings();
        RegisterAll();
        HotkeysChanged?.Invoke();
    }

    private void RegisterAll()
    {
        if (_hWnd == IntPtr.Zero) return;

        foreach (var kvp in _bindings)
        {
            int id = (int)kvp.Key;
            var item = kvp.Value;
            if (item.VirtualKey != 0)
            {
                RegisterHotKey(_hWnd, id, item.Modifiers | MOD_NOREPEAT, item.VirtualKey);
            }
        }
    }

    private void UnregisterAll()
    {
        if (_hWnd == IntPtr.Zero) return;
        foreach (var key in _bindings.Keys)
        {
            try { UnregisterHotKey(_hWnd, (int)key); } catch { }
        }
    }

    private void LoadFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegSubKey);
            if (key == null) return;

            foreach (var action in _bindings.Keys)
            {
                string actionName = action.ToString();
                if (key.GetValue(actionName + "_Mod") is int mod && key.GetValue(actionName + "_Key") is int vk)
                {
                    _bindings[action].Modifiers = (uint)mod;
                    _bindings[action].VirtualKey = (uint)vk;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log("LoadHotkeys error: " + ex.Message);
        }
    }

    private static void SaveToRegistry(HotkeyItem item)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegSubKey);
            if (key != null)
            {
                string prefix = item.Action.ToString();
                key.SetValue(prefix + "_Mod", (int)item.Modifiers, RegistryValueKind.DWord);
                key.SetValue(prefix + "_Key", (int)item.VirtualKey, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            App.Log("SaveHotkey error: " + ex.Message);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(SonarHotkeyAction), id))
            {
                var action = (SonarHotkeyAction)id;
                HotkeyPressed?.Invoke(action);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(HwndHook);
    }
}
