using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace SonarGG;

/// <summary>
/// Service de gestion bilingue (Français / English) pour SonarGG.
/// Permet un basculement instantané de la langue dans toute l'interface.
/// </summary>
public static class LocalizationService
{
    public static event Action? LanguageChanged;

    private static string _currentLang = "fr"; // "fr" ou "en"

    public static string CurrentLanguage
    {
        get => _currentLang;
        set
        {
            if (_currentLang != value)
            {
                _currentLang = value == "en" ? "en" : "fr";
                SaveLanguagePreference(_currentLang);
                LanguageChanged?.Invoke();
            }
        }
    }

    public static void SetLanguage(string lang) => CurrentLanguage = lang;

    private static readonly Dictionary<string, (string Fr, string En)> Strings = new()
    {
        // Branding & En-tête
        ["AppSubtitle"] = ("PRO", "PRO"),
        ["EngineActive"] = ("MOTEUR ULTRA-FAIBLE LATENCE (0.2 ms)", "ULTRA-LOW LATENCY ENGINE (0.2 ms)"),
        ["EngineSub"] = ("Performance Maximale · Haute Définition 64-bit", "Maximum Performance · 64-bit High Definition"),
        ["BtnPurge"] = ("🗑️ Purger GG", "🗑️ Purge GG"),
        ["BtnPurgeTip"] = ("Fermer tous les processus d'arrière-plan SteelSeries GG", "Terminate all background SteelSeries GG processes"),
        ["BtnMiniMode"] = ("📱 Mode Mini", "📱 Mini Mode"),
        ["BtnMiniModeTip"] = ("Basculer en vue compacte mini-table de mixage (Ctrl+M)", "Switch to compact mini mixer view (Ctrl+M)"),
        ["BtnShortcuts"] = ("⌨️ Raccourcis", "⌨️ Shortcuts"),

        // Onglets
        ["TabMixer"] = ("🎚️  Mixer", "🎚️  Mixer"),
        ["TabChatMix"] = ("⚖️  ChatMix", "⚖️  ChatMix"),
        ["TabEq"] = ("🎛️  Égaliseur & Profils", "🎛️  Equalizer & Profiles"),
        ["TabMic"] = ("🎙️  Studio Micro & VST", "🎙️  Studio Mic & VST"),
        ["TabSpatial"] = ("🌐  Spatial Audio 7.1", "🌐  Spatial Audio 7.1"),
        ["TabDevices"] = ("🎧  Périphériques", "🎧  Devices"),
        ["TabSettings"] = ("⚙️  Paramètres", "⚙️  Settings"),
        ["TabShortcuts"] = ("⌨️  Raccourcis", "⌨️  Hotkeys"),

        // Mixer Tab
        ["MixerTitle"] = ("Table de Mixage Virtuelle & VU-Mètres Temps Réel", "Virtual Audio Mixer & Real-Time Peak Meters"),
        ["MixerSub"] = ("Calcul balistique instantané · 60 FPS", "Instant ballistic calculation · 60 FPS"),
        ["AppRouterTitle"] = ("📦 Routage des Applications & Multi-Sources", "📦 App Routing & Multi-Source"),
        ["AppRouterHint"] = ("💡 Cliquez sur un canal ci-dessous ou glissez vos apps dans les colonnes", "💡 Click a channel below or drag & drop apps into columns"),
        ["BtnRefreshApps"] = ("🔄 Actualiser", "🔄 Refresh"),
        ["DragAppHere"] = ("Glisser une app ici", "Drop an app here"),

        // Noms des Canaux
        ["ChannelGame"] = ("Jeu (Game)", "Gaming"),
        ["ChannelChat"] = ("Chat Vocal", "Voice Chat"),
        ["ChannelMedia"] = ("Média", "Media"),
        ["ChannelAux"] = ("Auxiliaire", "Auxiliary"),
        ["ChannelMic"] = ("Microphone", "Microphone"),
        ["DefaultRoute"] = ("Défaut", "Default"),

        // Mute Buttons
        ["MuteActive"] = ("🔊 ACTIF", "🔊 ACTIVE"),
        ["MuteMuted"] = ("🔇 COUPE", "🔇 MUTED"),

        // ChatMix Tab
        ["ChatMixTitle"] = ("Équilibrage Vocal vs Jeu en Temps Réel", "Real-Time Game vs Voice Balancing"),
        ["ChatMixDesc"] = ("Ajustez la balance entre le son de votre jeu et Discord.", "Adjust the balance between your game sound and Discord."),
        ["ChatMixReset"] = ("🔄 Réinitialiser (50/50)", "🔄 Reset (50/50)"),

        // EQ Tab
        ["EqTitle"] = ("Égaliseur Paramétrique 10 Bandes", "10-Band Parametric Equalizer"),
        ["EqTargetChannel"] = ("Canal Cible :", "Target Channel:"),
        ["EqPresetLabel"] = ("Profil Prédéfini :", "Preset Profile:"),
        ["EqFlat"] = ("Réinitialiser Plat (0 dB)", "Reset Flat (0 dB)"),

        // Microphone & VST Tab
        ["MicTitle"] = ("Chaine Vocale Studio & Rack de Plugins VST3", "Studio Voice Chain & VST3 Plugin Rack"),
        ["MicSubtitle"] = ("Traitement Studio Haute Définition · Latence ultra-faible < 0.5 ms", "Studio Grade High Definition Processing · Ultra-low latency < 0.5 ms"),
        ["NoiseGate"] = ("Porte Anti-Bruit (Noise Gate)", "Noise Gate Threshold"),
        ["NoiseGateDesc"] = ("Coupe automatiquement le micro dès que vous arrêtez de parler.", "Automatically mutes microphone when you stop speaking."),
        ["Compressor"] = ("Compresseur Vocal Studio", "Studio Voice Compressor"),
        ["CompressorDesc"] = ("Égalise le volume de votre voix : chuchotements audibles et cris atténués.", "Balances your vocal dynamics: loud shouts attenuated and whispers clear."),
        ["HighPass"] = ("Filtre Coupe-Bas Anti-Pop (80 Hz)", "Anti-Pop High-Pass Filter (80 Hz)"),
        ["HighPassDesc"] = ("Élimine les bruits de frappe au clavier, coups dans le bureau et résonances sourdes.", "Eliminates desk rumbles, keyboard clatter, and microphone pop noises."),
        ["VstRackTitle"] = ("Rack de Plugins VST3 / Effets Externes", "VST3 Plugin Rack / External Effects"),
        ["BtnAddVst"] = ("➕ Ajouter un Plugin VST3...", "➕ Add VST3 Plugin..."),
        ["BtnScanVst"] = ("🔍 Scanner le PC", "🔍 Scan PC for VSTs"),
        ["Sidetone"] = ("Écoute en Direct (Sidetone / Retour Casque)", "Direct Sidetone / Headphone Monitoring"),

        // Spatial Tab
        ["SpatialTitle"] = ("Son Surround Virtuel 7.1", "7.1 Virtual Surround Sound"),
        ["SpatialToggle"] = ("Activer le Spatial Audio 7.1", "Enable 7.1 Spatial Audio"),
        ["ReverbGain"] = ("Gain de Réverbération :", "Reverb Gain:"),

        // Devices Tab
        ["DevicesTitle"] = ("Routage des Périphériques Audio Physiques", "Physical Audio Hardware Routing"),
        ["DevicesSubtitle"] = ("Choisissez précisément où SonarGG envoie chaque canal audio vers vos casques et enceintes.", "Select exactly where SonarGG directs each audio channel to your headsets and speakers."),
        ["DevHeadphones"] = ("Sortie Principale / Casque (Jeu & Général) :", "Main Output / Headset (Gaming & Master):"),
        ["DevChat"] = ("Sortie Chat Vocal Dédiée (Discord / Teams) :", "Dedicated Voice Chat Output (Discord / Teams):"),
        ["DevMedia"] = ("Sortie Média / Musique (Spotify / Navigateurs) :", "Media / Music Output (Spotify / Browsers):"),
        ["DevMic"] = ("Entrée Microphone Physique Réelle (Voix) :", "Physical Real Microphone Input (Voice):"),
        ["BtnTestTone"] = ("🔊 Tester le son (Stéréo L/R)", "🔊 Test Audio Tone (Stereo L/R)"),
        ["BtnTestMic"] = ("🎧 Écouter mon micro", "🎧 Listen to Mic"),
        ["EngineBufferTitle"] = ("Moteur Audio & Latence (Buffers)", "Audio Engine & Latency Buffers"),

        // Settings Tab
        ["SettingsTitle"] = ("Paramètres de Démarrage & Système", "Startup & System Settings"),
        ["AutoStart"] = ("Démarrer SonarGG avec Windows", "Start SonarGG with Windows"),
        ["MinimizeTray"] = ("Réduire dans la zone de notification (System Tray)", "Minimize to System Tray"),
        ["AutoPurge"] = ("Purger automatiquement SteelSeries GG au démarrage", "Auto-purge SteelSeries GG on startup"),
        ["LanguageLabel"] = ("Langue de l'interface (Language) :", "Interface Language:"),
        ["SettingsUpdateTitle"] = ("🚀 Mises à Jour & Version", "🚀 Updates & Version"),
        ["BtnCheckUpdates"] = ("🔄 Vérifier les Mises à Jour", "🔄 Check for Updates"),
        ["CheckingUpdates"] = ("Vérification des mises à jour sur GitHub...", "Checking for updates on GitHub..."),
        ["NoUpdateAvailable"] = ("Vous utilisez la version la plus récente de SonarGG.", "You are using the latest version of SonarGG."),
        ["UpdateAvailable"] = ("🎉 Nouvelle version disponible !", "🎉 New version available!"),
        ["BtnInstallUpdate"] = ("⚡ Télécharger & Mettre à jour", "⚡ Download & Install Update"),
        ["DownloadingUpdate"] = ("Téléchargement de la mise à jour... {0}%", "Downloading update... {0}%"),
        ["UpdateDownloadError"] = ("Erreur lors du téléchargement. Veuillez réessayer.", "Error downloading update. Please retry."),

        // Mini Mode
        ["MiniTitle"] = ("SonarGG Mini", "SonarGG Mini"),
        ["MiniAlwaysOnTop"] = ("Épingler au premier plan (Always on Top)", "Pin Always on Top"),
        ["MiniExpand"] = ("Agrandir en mode Studio Pro", "Expand to Studio Pro mode"),
        ["MiniRestore"] = ("Mode Pro", "Pro Mode"),

        // Hotkeys Tab
        ["HotkeysTitle"] = ("Raccourcis Clavier Globaux & En-Jeu", "Global & In-Game Keyboard Shortcuts"),
        ["HotkeysDesc"] = ("Ces raccourcis fonctionnent même quand vous êtes en plein jeu ou dans une autre application.", "These shortcuts work even when gaming in fullscreen or in another app."),
        ["HkMuteMic"] = ("Couper / Activer le Micro", "Mute / Unmute Microphone"),
        ["HkMuteGame"] = ("Couper / Activer le Son du Jeu", "Mute / Unmute Game Audio"),
        ["HkMuteChat"] = ("Couper / Activer le Chat Vocal", "Mute / Unmute Voice Chat"),
        ["HkMuteMedia"] = ("Couper / Activer la Musique / Média", "Mute / Unmute Music / Media"),
        ["HkMuteAux"] = ("Couper / Activer le canal Aux", "Mute / Unmute Aux Channel"),
        ["HkToggleMini"] = ("Basculer Mode Mini / Mode Normal", "Toggle Mini / Full Mode"),
        ["HkVolUp"] = ("Augmenter le Volume Global (+5%)", "Master Volume Up (+5%)"),
        ["HkVolDown"] = ("Diminuer le Volume Global (-5%)", "Master Volume Down (-5%)"),
        ["HkGlobalNotice"] = ("✅ Enregistrés au niveau du système d'exploitation Windows", "✅ Registered at the Windows OS kernel level"),
        ["HkClickToEdit"] = ("💡 Cliquez sur un raccourci pour le modifier. Appuyez sur votre nouvelle combinaison de touches ou Échap pour annuler.", "💡 Click on any shortcut to edit. Press your new key combination or Escape to cancel."),
        ["BtnResetHotkeys"] = ("🔄 Réinitialiser les raccourcis par défaut", "🔄 Reset to Default Shortcuts"),
        ["HkPressAnyKey"] = ("⌨️ Appuyez sur une touche...", "⌨️ Press any key...")
    };

    public static string Get(string key)
    {
        if (Strings.TryGetValue(key, out var val))
        {
            return _currentLang == "en" ? val.En : val.Fr;
        }
        return key;
    }

    public static void LoadLanguagePreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SonarGG", false);
            if (key?.GetValue("Language") is string savedLang && !string.IsNullOrEmpty(savedLang))
            {
                _currentLang = savedLang.ToLowerInvariant() == "en" ? "en" : "fr";
            }
        }
        catch { }
    }

    private static void SaveLanguagePreference(string lang)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\SonarGG");
            key?.SetValue("Language", lang);
        }
        catch { }
    }
}
