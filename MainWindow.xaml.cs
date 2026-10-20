using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using Path = System.Windows.Shapes.Path;
using HAlign = System.Windows.HorizontalAlignment;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DragDrop = System.Windows.DragDrop;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using DataFormats = System.Windows.DataFormats;
using Cursors = System.Windows.Input.Cursors;
using System.Windows.Controls.Primitives;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Key = System.Windows.Input.Key;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;

namespace SonarGG;

/// <summary>
/// Logique d'interaction pour MainWindow.xaml — SonarGG
/// Table de Mixage & Contrôleur Audio Pro
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApoController _apo = new();
    private readonly PeakMeterService _peakMeter = new();
    private readonly DispatcherTimer _meterTimer = new();
    private readonly AppRoutingService _routing = new();
    private readonly DispatcherTimer _appScanTimer = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly VstHostService _vstHost = new();
    private readonly MicrophoneEngine _micEngine = new();
    private bool _isMicMonitoring = false;

    // Hotkey Editing state
    private SonarHotkeyAction? _editingHotkeyAction;
    private Button? _editingHotkeyButton;

    private Point _dragStartPoint;
    private bool _isDraggingApp = false;
    private string _lastAppSignature = "";
    private bool _forceRefresh = false;

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isSyncingUi = true;
    private bool _isRealExit = false;

    // Mini Mode state & restore geometry
    private bool _isMiniMode = false;
    private double _prevWidth = 1220;
    private double _prevHeight = 840;
    private double _prevLeft = 100;
    private double _prevTop = 100;

    // Sliders & Labels pour l'égaliseur 10 bandes
    private readonly Slider[] _eqSliders = new Slider[10];
    private readonly TextBlock[] _eqValLabels = new TextBlock[10];

    private static readonly float[] DefaultFrequencies = {
        32f, 64f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
    };

    private static readonly string[] FreqLabels = {
        "32 Hz", "64 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz"
    };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Configuration timer VU-mètres (60 FPS balistique fluide)
        _meterTimer.Interval = TimeSpan.FromMilliseconds(16);
        _meterTimer.Tick += MeterTimer_Tick;

        // Molette de souris sur tous les sliders (pas de 5% ou 1dB)
        EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((s, ev) =>
        {
            if (s is Slider slider && slider.IsEnabled)
            {
                double range = slider.Maximum - slider.Minimum;
                double step = range > 10 ? 1.0 : range * 0.05;
                if (ev.Delta > 0)
                    slider.Value = Math.Min(slider.Maximum, slider.Value + step);
                else
                    slider.Value = Math.Max(slider.Minimum, slider.Value - step);
                ev.Handled = true;
            }
        }));
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            BuildEqualizerBands();

            bool ok = _apo.Initialize();
            if (!ok)
            {
                MessageBox.Show(
                    "Impossible d'initialiser le pilote Sonar APO direct.\n\n" +
                    "Vérifiez que le pilote virtuel SteelSeries Sonar est bien installé sur ce système.",
                    "Attention - Pilote SonarGG",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Démarrage du moteur de mesure des crêtes audio
            _peakMeter.Initialize();
            _meterTimer.Start();

            // Configuration System Tray
            InitSystemTray();

            // Vérification auto-démarrage Windows
            ChkAutoStart.IsChecked = IsAutoStartEnabled();

            // Auto-purge des processus SteelSeries si cochée
            if (ChkAutoPurgeGG.IsChecked == true)
            {
                ApoController.KillGGProcesses();
            }

            PopulateDevices();

            if (ComboEqChannel != null)
            {
                ComboEqChannel.SelectedIndex = 0;
            }
            LoadPresetsForChannel("Game");
            SyncAllFromApo();
            DrawEqCurve();

            // Initialisation de la langue (FR / EN)
            LocalizationService.LoadLanguagePreference();
            LocalizationService.LanguageChanged += ApplyLanguage;
            ApplyLanguage();

            // Initialisation des raccourcis globaux éditables
            _hotkeyService.Initialize(new WindowInteropHelper(this).Handle);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.HotkeysChanged += UpdateHotkeysUi;
            UpdateHotkeysUi();

            // Configuration DSP du moteur Microphone ultra-basse latence
            _micEngine.Volume = (float)SliderMic.Value;
            _micEngine.IsMuted = _apo.GetMute("ChatCapture");
            _micEngine.IsNoiseGateEnabled = SliderNoiseGate != null && SliderNoiseGate.Value > -59.0;
            _micEngine.NoiseGateThresholdDb = SliderNoiseGate != null ? (float)SliderNoiseGate.Value : -44f;
            _micEngine.IsCompressorEnabled = ChkCompressor?.IsChecked == true;
            _micEngine.CompressorThresholdDb = SliderCompressor != null ? (float)SliderCompressor.Value : -18f;
            _micEngine.PeakLevelChanged += (peak) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isMiniMode)
                    {
                        if (MiniVuMic != null) MiniVuMic.Value = peak;
                    }
                    else
                    {
                        if (VuBarMic != null) VuBarMic.Height = Math.Clamp(peak * 190f, 0, 190f);
                    }
                }, DispatcherPriority.Render);
            };

            // Initialisation du rack VST3 Microphone
            _vstHost.Initialize();
            _vstHost.VstRackChanged += RefreshVstRackUi;
            RefreshVstRackUi();

            // Initialisation du service de routage des applications multi-sources
            _routing.Initialize();
            RefreshAppsList();
            _appScanTimer.Interval = TimeSpan.FromSeconds(3);
            _appScanTimer.Tick += (s, ev) => RefreshAppsList();
            _appScanTimer.Start();

            // Vérification automatique des mises à jour GitHub en arrière-plan
            Task.Run(CheckUpdatesSilently);
        }
        catch (Exception ex)
        {
            App.Log("Exception in MainWindow_Loaded: " + ex);
            MessageBox.Show("Erreur au chargement : " + ex.Message, "Erreur SonarGG");
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isRealExit && ChkMinimizeToTray?.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(1200, "SonarGG Actif", "SonarGG continue de gérer l'audio DSP en tâche de fond.", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }

        _meterTimer.Stop();
        _appScanTimer.Stop();
        _micEngine.Dispose();
        _peakMeter.Dispose();
        _hotkeyService.Dispose();
        _routing.Dispose();
        _apo.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Mode enregistrement d'un raccourci personnalisé
        if (_editingHotkeyAction != null)
        {
            if (e.Key == Key.Escape)
            {
                _editingHotkeyAction = null;
                _editingHotkeyButton = null;
                UpdateHotkeysUi();
                e.Handled = true;
                return;
            }

            // Ignorer si appui sur une touche de modification seule
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }

            uint mods = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= HotkeyService.MOD_CONTROL;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= HotkeyService.MOD_ALT;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= HotkeyService.MOD_SHIFT;

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(actualKey);

            if (vk > 0)
            {
                _hotkeyService.UpdateBinding(_editingHotkeyAction.Value, mods, (uint)vk);
                _editingHotkeyAction = null;
                _editingHotkeyButton = null;
                UpdateHotkeysUi();
                e.Handled = true;
                return;
            }
        }

        // Ctrl + M : Basculer Mode Mini / Mode Normal
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
        {
            ToggleMiniMode(!_isMiniMode);
            e.Handled = true;
            return;
        }

        // Ctrl + L : Basculer Langue FR / EN
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            string newLang = LocalizationService.CurrentLanguage == "fr" ? "en" : "fr";
            LocalizationService.SetLanguage(newLang);
            e.Handled = true;
            return;
        }

        // Onglets 1 à 5
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D5)
        {
            int tabIndex = (int)e.Key - (int)Key.D1;
            if (MainTabControl != null && tabIndex < MainTabControl.Items.Count)
            {
                MainTabControl.SelectedIndex = tabIndex;
                if (_isMiniMode) ToggleMiniMode(false);
                e.Handled = true;
                return;
            }
        }

        // Escape : En mode mini, quitte le mode mini
        if (e.Key == Key.Escape && _isMiniMode)
        {
            ToggleMiniMode(false);
            e.Handled = true;
            return;
        }
    }

    // =====================================================================
    // System Tray Integration
    // =====================================================================
    private void InitSystemTray()
    {
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            System.Drawing.Icon icon;
            if (File.Exists(iconPath))
            {
                icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "SonarGG — Contrôleur Audio DSP",
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => RestoreWindow();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("🎚️ Ouvrir SonarGG", null, (s, e) => RestoreWindow());
            menu.Items.Add("📱 Mode Mini (Ctrl+M)", null, (s, e) => ToggleMiniMode(!_isMiniMode));
            menu.Items.Add("🔇 Muter / Démuter Tout", null, (s, e) => ToggleMasterMute());
            menu.Items.Add("⚖️ Centrer ChatMix (50/50)", null, (s, e) =>
            {
                SliderChatMix.Value = 0;
                MiniSliderChatMix.Value = 0;
            });
            menu.Items.Add("🗑️ Fermer les processus GG", null, (s, e) => ApoController.KillGGProcesses());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("❌ Quitter SonarGG", null, (s, e) =>
            {
                _isRealExit = true;
                Close();
            });

            _trayIcon.ContextMenuStrip = menu;
        }
        catch (Exception ex)
        {
            App.Log("Tray Init Warning: " + ex.Message);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ToggleMasterMute()
    {
        bool isMuted = _apo.GetMute("Game");
        bool next = !isMuted;
        _apo.SetMute("Game", next);
        _apo.SetMute("ChatRender", next);
        _apo.SetMute("Media", next);
        _apo.SetMute("Aux", next);

        UpdateMuteButton(BtnMuteGame, next);
        UpdateMiniMuteButton(MiniBtnMuteGame, next);
        UpdateMuteButton(BtnMuteChat, next);
        UpdateMiniMuteButton(MiniBtnMuteChat, next);
        UpdateMuteButton(BtnMuteMedia, next);
        UpdateMiniMuteButton(MiniBtnMuteMedia, next);
        UpdateMuteButton(BtnMuteAux, next);
        UpdateMiniMuteButton(MiniBtnMuteAux, next);
    }

    // =====================================================================
    // VU-Mètres Temps Réel (Timer Tick)
    // =====================================================================
    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible) return;

        _peakMeter.Update();

        if (_isMiniMode)
        {
            MiniVuGame.Value = Math.Clamp(_peakMeter.SmoothedGame, 0f, 1f);
            MiniVuChat.Value = Math.Clamp(_peakMeter.SmoothedChat, 0f, 1f);
            MiniVuMedia.Value = Math.Clamp(_peakMeter.SmoothedMedia, 0f, 1f);
            MiniVuAux.Value = Math.Clamp(_peakMeter.SmoothedAux, 0f, 1f);
            MiniVuMic.Value = Math.Clamp(_peakMeter.SmoothedMic, 0f, 1f);
        }
        else
        {
            const float maxH = 190f;
            VuBarGame.Height = Math.Clamp(_peakMeter.SmoothedGame * maxH, 0, maxH);
            VuBarChat.Height = Math.Clamp(_peakMeter.SmoothedChat * maxH, 0, maxH);
            VuBarMedia.Height = Math.Clamp(_peakMeter.SmoothedMedia * maxH, 0, maxH);
            VuBarAux.Height = Math.Clamp(_peakMeter.SmoothedAux * maxH, 0, maxH);
            VuBarMic.Height = Math.Clamp(_peakMeter.SmoothedMic * maxH, 0, maxH);
        }
    }

    // =====================================================================
    // Mode Mini / Full Mode Switching
    // =====================================================================
    private void BtnMiniMode_Click(object sender, RoutedEventArgs e)
    {
        ToggleMiniMode(true);
    }

    private void BtnRestorePro_Click(object sender, RoutedEventArgs e)
    {
        ToggleMiniMode(false);
    }

    private void BtnPinMini_Click(object sender, RoutedEventArgs e)
    {
        Topmost = BtnPinMini.IsChecked == true;
    }

    private void ToggleMiniMode(bool enable)
    {
        _isMiniMode = enable;
        if (_isMiniMode)
        {
            _prevWidth = Width;
            _prevHeight = Height;
            _prevLeft = Left;
            _prevTop = Top;

            WindowState = WindowState.Normal;
            MinWidth = 360;
            MinHeight = 250;
            Width = 420;
            Height = 310;

            FullViewContainer.Visibility = Visibility.Collapsed;
            MiniViewContainer.Visibility = Visibility.Visible;
        }
        else
        {
            MinWidth = 1040;
            MinHeight = 720;
            Width = _prevWidth > 800 ? _prevWidth : 1220;
            Height = _prevHeight > 600 ? _prevHeight : 840;
            Left = _prevLeft;
            Top = _prevTop;

            MiniViewContainer.Visibility = Visibility.Collapsed;
            FullViewContainer.Visibility = Visibility.Visible;
        }

        // Synchroniser tous les contrôles
        SyncAllFromApo();
    }

    // =====================================================================
    // Window Caption Controls (Frameless Title Bar)
    // =====================================================================
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (ChkMinimizeToTray.IsChecked == true)
        {
            Hide();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMiniMode) return;

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            BtnMaximize.Content = "🗖";
        }
        else
        {
            WindowState = WindowState.Maximized;
            BtnMaximize.Content = "🗗";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (ChkMinimizeToTray.IsChecked == true)
        {
            Hide();
            _trayIcon?.ShowBalloonTip(1000, "SonarGG Actif", "Réduit dans la zone de notification.", System.Windows.Forms.ToolTipIcon.Info);
        }
        else
        {
            _isRealExit = true;
            Close();
        }
    }

    // =====================================================================
    // Dynamic Equalizer Band Sliders & Parametric Curve
    // =====================================================================
    private void BuildEqualizerBands()
    {
        GridEqBands.Children.Clear();
        Style? vStyle = TryFindResource("VerticalSliderStyle") as Style;

        for (int i = 0; i < 10; i++)
        {
            int bandIndex = i;

            var bandPanel = new StackPanel
            {
                HorizontalAlignment = HAlign.Center,
                Margin = new Thickness(4)
            };

            var lblFreq = new TextBlock
            {
                Text = FreqLabels[i],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HAlign.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var slider = new Slider
            {
                Style = vStyle,
                Orientation = Orientation.Vertical,
                Height = 135,
                Minimum = -12,
                Maximum = 12,
                Value = 0,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                HorizontalAlignment = HAlign.Center
            };

            var lblVal = new TextBlock
            {
                Text = "0.0 dB",
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 26)),
                HorizontalAlignment = HAlign.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            slider.ValueChanged += (s, ev) =>
            {
                lblVal.Text = $"{slider.Value:+0.0;-0.0;0.0} dB";
                DrawEqCurve();
                if (!IsLoaded || _isSyncingUi) return;

                string ch = GetSelectedEqChannel();
                _apo.SetEqFilter(ch, bandIndex + 1, true, (float)slider.Value, DefaultFrequencies[bandIndex]);
            };

            _eqSliders[i] = slider;
            _eqValLabels[i] = lblVal;

            bandPanel.Children.Add(lblFreq);
            bandPanel.Children.Add(slider);
            bandPanel.Children.Add(lblVal);

            GridEqBands.Children.Add(bandPanel);
        }
    }

    private void CanvasEqCurve_Loaded(object sender, RoutedEventArgs e)
    {
        DrawEqCurve();
    }

    private void CanvasEqCurve_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawEqCurve();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;

        if (TabEq != null && TabEq.IsSelected)
        {
            Dispatcher.InvokeAsync(DrawEqCurve, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static readonly string[] FreqShortLabels = { "32Hz", "64Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };

    private void DrawEqCurve()
    {
        if (CanvasEqCurve == null) return;
        double w = CanvasEqCurve.ActualWidth > 0 ? CanvasEqCurve.ActualWidth : 800;
        double h = CanvasEqCurve.ActualHeight > 0 ? CanvasEqCurve.ActualHeight : 142;
        if (w <= 0 || h <= 0) return;

        CanvasEqCurve.Children.Clear();
        double centerY = h / 2.0;

        // 1. Lignes de référence horizontales en dB (+12, +6, 0 dB, -6, -12 dB)
        double[] dbLevels = { 12, 6, 0, -6, -12 };
        foreach (var db in dbLevels)
        {
            double y = centerY - (db / 12.0) * (centerY - 16);
            bool isCenter = Math.Abs(db) < 0.1;

            var hLine = new Line
            {
                X1 = 36, Y1 = y,
                X2 = w - 10, Y2 = y,
                Stroke = isCenter ? new SolidColorBrush(Color.FromArgb(90, 255, 107, 0)) : new SolidColorBrush(Color.FromArgb(35, 148, 163, 184)),
                StrokeThickness = isCenter ? 1.5 : 1,
                StrokeDashArray = isCenter ? null : new DoubleCollection { 3, 3 }
            };
            CanvasEqCurve.Children.Add(hLine);

            var lbl = new TextBlock
            {
                Text = $"{db:+0;-0;0} dB",
                FontSize = 9,
                FontWeight = isCenter ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isCenter ? new SolidColorBrush(Color.FromRgb(255, 107, 0)) : new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(lbl, 4);
            Canvas.SetTop(lbl, y - 7);
            CanvasEqCurve.Children.Add(lbl);
        }

        // 2. Lignes de grille verticales et libellés des 10 fréquences
        Point[] points = new Point[12];
        points[0] = new Point(40, centerY);

        double usableWidth = w - 60;
        for (int i = 0; i < 10; i++)
        {
            double x = 40 + (usableWidth / 11.0) * (i + 1);
            double gain = _eqSliders[i]?.Value ?? 0.0;
            double y = centerY - (gain / 12.0) * (centerY - 16);
            points[i + 1] = new Point(x, y);

            var vLine = new Line
            {
                X1 = x, Y1 = 8,
                X2 = x, Y2 = h - 18,
                Stroke = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                StrokeThickness = 1
            };
            CanvasEqCurve.Children.Add(vLine);

            var freqLbl = new TextBlock
            {
                Text = FreqShortLabels[i],
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(100, 148, 163, 184)),
                HorizontalAlignment = HAlign.Center
            };
            Canvas.SetLeft(freqLbl, x - 12);
            Canvas.SetTop(freqLbl, h - 16);
            CanvasEqCurve.Children.Add(freqLbl);
        }
        points[11] = new Point(w - 10, centerY);

        // 3. Courbe de réponse splinée Bezier lissée & Remplissage lumineux
        var pathFigure = new PathFigure { StartPoint = points[0], IsClosed = false };
        var fillFigure = new PathFigure { StartPoint = points[0], IsClosed = true };

        for (int i = 0; i < points.Length - 1; i++)
        {
            Point ptCurrent = points[i];
            Point ptNext = points[i + 1];
            double ctrlX = (ptCurrent.X + ptNext.X) / 2.0;
            var bezier = new BezierSegment(
                new Point(ctrlX, ptCurrent.Y),
                new Point(ctrlX, ptNext.Y),
                ptNext,
                true);
            pathFigure.Segments.Add(bezier);
            fillFigure.Segments.Add(bezier);
        }

        fillFigure.Segments.Add(new LineSegment(new Point(points[11].X, h - 18), false));
        fillFigure.Segments.Add(new LineSegment(new Point(points[0].X, h - 18), false));

        var fillGeometry = new PathGeometry();
        fillGeometry.Figures.Add(fillFigure);

        var fillPath = new Path
        {
            Fill = new LinearGradientBrush(
                Color.FromArgb(45, 255, 107, 0),
                Color.FromArgb(0, 255, 107, 0),
                90.0),
            Data = fillGeometry
        };
        CanvasEqCurve.Children.Add(fillPath);

        var curveGeometry = new PathGeometry();
        curveGeometry.Figures.Add(pathFigure);

        var curvePath = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
            StrokeThickness = 2.5,
            Data = curveGeometry
        };
        CanvasEqCurve.Children.Add(curvePath);

        // 4. Nœuds interactifs lumineux sur chaque bande
        for (int i = 0; i < 10; i++)
        {
            Point pt = points[i + 1];
            var nodeGlow = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 107, 0))
            };
            Canvas.SetLeft(nodeGlow, pt.X - 7);
            Canvas.SetTop(nodeGlow, pt.Y - 7);
            CanvasEqCurve.Children.Add(nodeGlow);

            var nodeCenter = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
                StrokeThickness = 2,
                ToolTip = $"{FreqShortLabels[i]} : {_eqSliders[i]?.Value:+0.0;-0.0;0.0} dB",
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(nodeCenter, pt.X - 4);
            Canvas.SetTop(nodeCenter, pt.Y - 4);
            CanvasEqCurve.Children.Add(nodeCenter);
        }
    }

    private void SyncAllFromApo()
    {
        _isSyncingUi = true;
        try
        {
            // Volumes & Mute Game
            float vGame = _apo.GetVolume("Game");
            bool mGame = _apo.GetMute("Game");
            SliderGameCasque.Value = vGame;
            MiniSliderGame.Value = vGame;
            TxtGameCasqueVal.Text = $"{(int)(vGame * 100)}%";
            TxtMiniGameVal.Text = $"{(int)(vGame * 100)}%";
            UpdateMuteButton(BtnMuteGame, mGame);
            UpdateMiniMuteButton(MiniBtnMuteGame, mGame);

            // Chat
            float vChat = _apo.GetVolume("ChatRender");
            bool mChat = _apo.GetMute("ChatRender");
            SliderChatCasque.Value = vChat;
            MiniSliderChat.Value = vChat;
            TxtChatCasqueVal.Text = $"{(int)(vChat * 100)}%";
            TxtMiniChatVal.Text = $"{(int)(vChat * 100)}%";
            UpdateMuteButton(BtnMuteChat, mChat);
            UpdateMiniMuteButton(MiniBtnMuteChat, mChat);

            // Media
            float vMedia = _apo.GetVolume("Media");
            bool mMedia = _apo.GetMute("Media");
            SliderMediaCasque.Value = vMedia;
            MiniSliderMedia.Value = vMedia;
            TxtMediaCasqueVal.Text = $"{(int)(vMedia * 100)}%";
            TxtMiniMediaVal.Text = $"{(int)(vMedia * 100)}%";
            UpdateMuteButton(BtnMuteMedia, mMedia);
            UpdateMiniMuteButton(MiniBtnMuteMedia, mMedia);

            // Aux
            float vAux = _apo.GetVolume("Aux");
            bool mAux = _apo.GetMute("Aux");
            SliderAuxCasque.Value = vAux;
            MiniSliderAux.Value = vAux;
            TxtAuxCasqueVal.Text = $"{(int)(vAux * 100)}%";
            TxtMiniAuxVal.Text = $"{(int)(vAux * 100)}%";
            UpdateMuteButton(BtnMuteAux, mAux);
            UpdateMiniMuteButton(MiniBtnMuteAux, mAux);

            // Mic
            float vMic = _apo.GetVolume("ChatCapture");
            bool mMic = _apo.GetMute("ChatCapture");
            SliderMic.Value = vMic;
            MiniSliderMic.Value = vMic;
            TxtMicVal.Text = $"{(int)(vMic * 100)}%";
            TxtMiniMicVal.Text = $"{(int)(vMic * 100)}%";
            UpdateMuteButton(BtnMuteMic, mMic);
            UpdateMiniMuteButton(MiniBtnMuteMic, mMic);

            // ChatMix
            float chatMix = _apo.GetChatMix();
            SliderChatMix.Value = chatMix;
            MiniSliderChatMix.Value = chatMix;
            UpdateChatMixLabels(chatMix);

            // Désactivation forcée de l'IA ClearCast & Impact Noise (Zero AI, 100% DSP/VST)
            _apo.SetClearCastAi(false, 0f);
            _apo.SetImpactNoise(false, 0f);

            // Porte Anti-Bruit & Compresseur Hardware
            _apo.GetNoiseGate(out bool ngOn, out float ngThresh);
            SliderNoiseGate.Value = ngThresh;
            if (TxtNoiseGateVal != null)
                TxtNoiseGateVal.Text = $"{(int)ngThresh} dB";

            _apo.GetCaptureCompressor(out bool compOn, out float compThresh);
            if (ChkCompressor != null)
                ChkCompressor.IsChecked = compOn;
            if (SliderCompressor != null)
                SliderCompressor.Value = compThresh;
            if (TxtCompressorVal != null)
                TxtCompressorVal.Text = $"{(int)compThresh} dB (Seuil)";

            _apo.GetVirtualSurround("Game", out bool spOn, out float spReverb);
            ChkSpatialAudio.IsChecked = spOn;
            SliderReverbGain.Value = spReverb;
            TxtReverbGainVal.Text = $"{(int)spReverb} dB";
        }
        catch (Exception ex)
        {
            App.Log("SyncAllFromApo error: " + ex.Message);
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private string GetSelectedEqChannel()
    {
        if (ComboEqChannel?.SelectedItem is ComboBoxItem item && item.Tag is string s)
        {
            return s;
        }
        return "Game";
    }

    // =====================================================================
    // Purge Processus SteelSeries
    // =====================================================================
    private void BtnPurgeGG_Click(object sender, RoutedEventArgs e)
    {
        int killed = ApoController.KillGGProcesses();
        MessageBox.Show(
            $"{killed} processus d'arrière-plan SteelSeries ont été fermés.\n\n" +
            "SonarGG continue de gérer l'audio DSP matériel et le pilote virtuel en toute autonomie !",
            "Purge des processus — SonarGG",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // =====================================================================
    // Tab 1: Faders de Mixage (Mixer)
    // =====================================================================
    private void SliderGameCasque_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtGameCasqueVal != null)
            TxtGameCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (MiniSliderGame != null && Math.Abs(MiniSliderGame.Value - e.NewValue) > 0.005)
            MiniSliderGame.Value = e.NewValue;
        if (TxtMiniGameVal != null)
            TxtMiniGameVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Game", (float)e.NewValue);
    }

    private void BtnMuteGame_Click(object sender, RoutedEventArgs e)
    {
        bool current = _apo.GetMute("Game");
        _apo.SetMute("Game", !current);
        UpdateMuteButton(BtnMuteGame, !current);
        UpdateMiniMuteButton(MiniBtnMuteGame, !current);
    }

    private void SliderChatCasque_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtChatCasqueVal != null)
            TxtChatCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (MiniSliderChat != null && Math.Abs(MiniSliderChat.Value - e.NewValue) > 0.005)
            MiniSliderChat.Value = e.NewValue;
        if (TxtMiniChatVal != null)
            TxtMiniChatVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("ChatRender", (float)e.NewValue);
    }

    private void BtnMuteChat_Click(object sender, RoutedEventArgs e)
    {
        bool current = _apo.GetMute("ChatRender");
        _apo.SetMute("ChatRender", !current);
        UpdateMuteButton(BtnMuteChat, !current);
        UpdateMiniMuteButton(MiniBtnMuteChat, !current);
    }

    private void SliderMediaCasque_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMediaCasqueVal != null)
            TxtMediaCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (MiniSliderMedia != null && Math.Abs(MiniSliderMedia.Value - e.NewValue) > 0.005)
            MiniSliderMedia.Value = e.NewValue;
        if (TxtMiniMediaVal != null)
            TxtMiniMediaVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Media", (float)e.NewValue);
    }

    private void BtnMuteMedia_Click(object sender, RoutedEventArgs e)
    {
        bool current = _apo.GetMute("Media");
        _apo.SetMute("Media", !current);
        UpdateMuteButton(BtnMuteMedia, !current);
        UpdateMiniMuteButton(MiniBtnMuteMedia, !current);
    }

    private void SliderAuxCasque_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtAuxCasqueVal != null)
            TxtAuxCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (MiniSliderAux != null && Math.Abs(MiniSliderAux.Value - e.NewValue) > 0.005)
            MiniSliderAux.Value = e.NewValue;
        if (TxtMiniAuxVal != null)
            TxtMiniAuxVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Aux", (float)e.NewValue);
    }

    private void BtnMuteAux_Click(object sender, RoutedEventArgs e)
    {
        bool current = _apo.GetMute("Aux");
        _apo.SetMute("Aux", !current);
        UpdateMuteButton(BtnMuteAux, !current);
        UpdateMiniMuteButton(MiniBtnMuteAux, !current);
    }

    private void SliderMic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMicVal != null)
            TxtMicVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (MiniSliderMic != null && Math.Abs(MiniSliderMic.Value - e.NewValue) > 0.005)
            MiniSliderMic.Value = e.NewValue;
        if (TxtMiniMicVal != null)
            TxtMiniMicVal.Text = $"{(int)(e.NewValue * 100)}%";
        _micEngine.Volume = (float)e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("ChatCapture", (float)e.NewValue);
    }

    private void BtnMuteMic_Click(object sender, RoutedEventArgs e)
    {
        bool current = _apo.GetMute("ChatCapture");
        _apo.SetMute("ChatCapture", !current);
        _micEngine.IsMuted = !current;
        UpdateMuteButton(BtnMuteMic, !current);
        UpdateMiniMuteButton(MiniBtnMuteMic, !current);
    }

    // Mini Sliders
    private void MiniSliderGame_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMiniGameVal != null)
            TxtMiniGameVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (TxtGameCasqueVal != null)
            TxtGameCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (SliderGameCasque != null && Math.Abs(SliderGameCasque.Value - e.NewValue) > 0.005)
            SliderGameCasque.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Game", (float)e.NewValue);
    }

    private void MiniSliderChat_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMiniChatVal != null)
            TxtMiniChatVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (TxtChatCasqueVal != null)
            TxtChatCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (SliderChatCasque != null && Math.Abs(SliderChatCasque.Value - e.NewValue) > 0.005)
            SliderChatCasque.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("ChatRender", (float)e.NewValue);
    }

    private void MiniSliderMedia_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMiniMediaVal != null)
            TxtMiniMediaVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (TxtMediaCasqueVal != null)
            TxtMediaCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (SliderMediaCasque != null && Math.Abs(SliderMediaCasque.Value - e.NewValue) > 0.005)
            SliderMediaCasque.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Media", (float)e.NewValue);
    }

    private void MiniSliderAux_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMiniAuxVal != null)
            TxtMiniAuxVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (TxtAuxCasqueVal != null)
            TxtAuxCasqueVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (SliderAuxCasque != null && Math.Abs(SliderAuxCasque.Value - e.NewValue) > 0.005)
            SliderAuxCasque.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("Aux", (float)e.NewValue);
    }

    private void MiniSliderMic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMiniMicVal != null)
            TxtMiniMicVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (TxtMicVal != null)
            TxtMicVal.Text = $"{(int)(e.NewValue * 100)}%";
        if (SliderMic != null && Math.Abs(SliderMic.Value - e.NewValue) > 0.005)
            SliderMic.Value = e.NewValue;
        _micEngine.Volume = (float)e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVolume("ChatCapture", (float)e.NewValue);
    }

    private void MiniSliderChatMix_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SliderChatMix != null && Math.Abs(SliderChatMix.Value - e.NewValue) > 0.005)
            SliderChatMix.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetChatMix((float)e.NewValue);
    }

    private static void UpdateMuteButton(Button btn, bool isMuted)
    {
        bool isEn = LocalizationService.CurrentLanguage == "en";
        if (isMuted)
        {
            btn.Content = isEn ? "🔇 MUTED" : "🔇 COUPÉ";
            btn.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            btn.Foreground = Brushes.White;
        }
        else
        {
            btn.Content = isEn ? "🔊 ACTIVE" : "🔊 ACTIF";
            btn.Background = new SolidColorBrush(Color.FromRgb(22, 27, 40));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 45, 63));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }

    private static void UpdateMiniMuteButton(Button btn, bool isMuted)
    {
        if (isMuted)
        {
            btn.Content = "🔇";
            btn.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            btn.Foreground = Brushes.White;
        }
        else
        {
            btn.Content = "🔊";
            btn.Background = new SolidColorBrush(Color.FromRgb(22, 27, 40));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 45, 63));
            btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }

    // =====================================================================
    // Tab 2: ChatMix
    // =====================================================================
    private void SliderChatMix_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateChatMixLabels(e.NewValue);
        if (MiniSliderChatMix != null && Math.Abs(MiniSliderChatMix.Value - e.NewValue) > 0.005)
            MiniSliderChatMix.Value = e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetChatMix((float)e.NewValue);
    }

    private void UpdateChatMixLabels(double balance)
    {
        if (TxtBalanceVal == null) return;

        if (balance <= 0)
        {
            int chatPct = (int)Math.Round((1.0 + balance) * 100);
            TxtBalanceVal.Text = $"100% / {chatPct}%";
        }
        else
        {
            int gamePct = (int)Math.Round((1.0 - balance) * 100);
            TxtBalanceVal.Text = $"{gamePct}% / 100%";
        }
    }

    private void BtnResetChatMix_Click(object sender, RoutedEventArgs e)
    {
        SliderChatMix.Value = 0;
        MiniSliderChatMix.Value = 0;
    }

    // =====================================================================
    // Tab 3: EQ Controls & Presets
    // =====================================================================
    private void ComboEqChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        string ch = GetSelectedEqChannel();
        LoadPresetsForChannel(ch);
    }

    private void LoadPresetsForChannel(string ch)
    {
        ComboEqPresets.Items.Clear();
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "Défaut (Plat)", Tag = "flat", IsSelected = true });
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "🎮 CS2 / Footsteps Boost", Tag = "cs2" });
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "🎯 Valorant Pro", Tag = "valorant" });
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "🪖 Call of Duty / Warzone", Tag = "warzone" });
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "🎵 Deep Bass Boost", Tag = "bass" });
        ComboEqPresets.Items.Add(new ComboBoxItem { Content = "💬 Clarté de Voix Podcast", Tag = "podcast" });
    }

    private void ComboEqPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        if (ComboEqPresets.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ApplyPresetTag(tag);
        }
    }

    private void ApplyPresetTag(string tag)
    {
        float[] gains = tag switch
        {
            "cs2" => new float[] { -2f, -1f, 1f, 3f, 4f, 6f, 7f, 5f, 3f, 1f },
            "valorant" => new float[] { -3f, 0f, 2f, 4f, 5f, 6f, 6f, 4f, 2f, 0f },
            "warzone" => new float[] { 2f, 1f, 0f, 2f, 4f, 6f, 5f, 3f, 1f, 0f },
            "bass" => new float[] { 7f, 6f, 5f, 3f, 1f, 0f, 0f, 0f, 0f, 0f },
            "podcast" => new float[] { -4f, -2f, 1f, 3f, 5f, 4f, 2f, 1f, 0f, -1f },
            _ => new float[10]
        };

        string ch = GetSelectedEqChannel();
        for (int i = 0; i < 10; i++)
        {
            if (_eqSliders[i] != null)
            {
                _eqSliders[i].Value = gains[i];
                _apo.SetEqFilter(ch, i + 1, true, gains[i], DefaultFrequencies[i]);
            }
        }
        DrawEqCurve();
    }

    private void BtnResetEqFlat_Click(object sender, RoutedEventArgs e)
    {
        ApplyPresetTag("flat");
    }

    // =====================================================================
    // Tab 4: Studio Micro & VST (Zero AI)
    // =====================================================================
    private void SliderNoiseGate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtNoiseGateVal != null)
            TxtNoiseGateVal.Text = $"{(int)e.NewValue} dB";
        _micEngine.NoiseGateThresholdDb = (float)e.NewValue;
        if (TxtGateStatus != null)
            TxtGateStatus.Text = e.NewValue > -40 ? "🔴 SEUIL ÉLEVÉ (Voix forte requise)" : "🟢 SEUIL NORMAL (Voix sensible)";
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetNoiseGate(true, (float)e.NewValue);
    }

    private void ChkCompressor_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetCaptureCompressor(true, (float)SliderCompressor.Value);
    }

    private void ChkCompressor_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetCaptureCompressor(false, (float)SliderCompressor.Value);
    }

    private void SliderCompressor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCompressorVal != null)
            TxtCompressorVal.Text = $"{(int)e.NewValue} dB (Seuil)";
        _micEngine.CompressorThresholdDb = (float)e.NewValue;
        if (!IsLoaded || _isSyncingUi) return;
        if (ChkCompressor != null && ChkCompressor.IsChecked == true)
        {
            _apo.SetCaptureCompressor(true, (float)e.NewValue);
        }
    }

    private void ChkHighPass_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetCaptureHighPass(1.0f);
    }

    private void ChkHighPass_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetCaptureHighPass(0.0f);
    }

    private void BtnScanVst_Click(object sender, RoutedEventArgs e)
    {
        var found = _vstHost.ScanInstalledPlugins();
        if (found.Count > 0)
        {
            foreach (var f in found)
            {
                _vstHost.AddPlugin(f);
            }
            MessageBox.Show($"{found.Count} plugin(s) VST détecté(s) et ajouté(s) à la chaîne vocale !", "Scan VST Terminé", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Aucun plugin VST standard trouvé dans les répertoires par défaut (Common Files/VST3).\nUtilisez le bouton '➕ Ajouter VST3...' pour sélectionner votre fichier manuellement.", "Scan VST", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnAddVst_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sélectionner un Plugin Audio VST (Microphone)",
            Filter = "Plugins Audio (*.vst3;*.dll)|*.vst3;*.dll|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _vstHost.AddPlugin(dlg.FileName);
        }
    }

    private void BtnPresetBroadcast_Click(object sender, RoutedEventArgs e)
    {
        SliderNoiseGate.Value = -44;
        SliderCompressor.Value = -18;
        ChkCompressor.IsChecked = true;
        ChkHighPass.IsChecked = true;
        _apo.SetNoiseGate(true, -44f);
        _apo.SetCaptureCompressor(true, -18f);
        _apo.SetCaptureHighPass(1.0f);
        MessageBox.Show("Profil '🎙️ Broadcast Pro' activé :\nPorte -44dB, Compresseur -18dB, Coupe-bas 80Hz.", "Profil Vocal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnPresetDiscord_Click(object sender, RoutedEventArgs e)
    {
        SliderNoiseGate.Value = -38;
        SliderCompressor.Value = -14;
        ChkCompressor.IsChecked = true;
        ChkHighPass.IsChecked = true;
        _apo.SetNoiseGate(true, -38f);
        _apo.SetCaptureCompressor(true, -14f);
        _apo.SetCaptureHighPass(1.0f);
        MessageBox.Show("Profil '🎮 Discord Clear' activé :\nPorte -38dB (bloque claviers mécaniques), Compresseur rapide -14dB.", "Profil Vocal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnPresetPodcast_Click(object sender, RoutedEventArgs e)
    {
        SliderNoiseGate.Value = -48;
        SliderCompressor.Value = -22;
        ChkCompressor.IsChecked = true;
        ChkHighPass.IsChecked = true;
        _apo.SetNoiseGate(true, -48f);
        _apo.SetCaptureCompressor(true, -22f);
        _apo.SetCaptureHighPass(1.0f);
        MessageBox.Show("Profil '📻 Podcast Chaud' activé :\nDynamique douce, présence des basses préservée.", "Profil Vocal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnPresetBypass_Click(object sender, RoutedEventArgs e)
    {
        SliderNoiseGate.Value = -60;
        ChkCompressor.IsChecked = false;
        ChkHighPass.IsChecked = false;
        _apo.SetNoiseGate(false, -60f);
        _apo.SetCaptureCompressor(false, 0f);
        _apo.SetCaptureHighPass(0.0f);
        MessageBox.Show("Effets désactivés : Signal brut du micro transmis directement (Bypass).", "Profil Vocal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SliderSidetone_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSidetoneVal != null)
            TxtSidetoneVal.Text = $"{(int)e.NewValue}%";
    }

    private void BtnSidetoneToggle_Click(object sender, RoutedEventArgs e)
    {
        _isMicMonitoring = !_isMicMonitoring;
        _micEngine.IsSidetoneEnabled = _isMicMonitoring;
        BtnSidetoneToggle.Content = _isMicMonitoring ? "🔴 STOP ÉCOUTE" : "🎧 ÉCOUTER";
        if (BtnTestMicMonitor != null)
        {
            BtnTestMicMonitor.Content = _isMicMonitoring ? "🔴 Arrêter l'écoute" : "🎧 Tester mon micro (Écoute)";
        }
    }

    private void RefreshVstRackUi()
    {
        if (PanelVstRack == null) return;
        PanelVstRack.Children.Clear();

        if (_vstHost.ActivePlugins.Count == 0)
        {
            var emptyBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 16, 20, 31)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 42, 53, 74)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var sp = new StackPanel { HorizontalAlignment = HAlign.Center };
            sp.Children.Add(new TextBlock
            {
                Text = "🔌 Aucun plugin VST3 configuré pour le micro",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HAlign.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Cliquez sur \"➕ Ajouter VST3...\" ou \"🔍 Scanner PC\" pour insérer vos effets vocaux préférés (ReaEQ, FabFilter, Noise Gate, etc.)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HAlign.Center
            });
            emptyBorder.Child = sp;
            PanelVstRack.Children.Add(emptyBorder);
            return;
        }

        for (int i = 0; i < _vstHost.ActivePlugins.Count; i++)
        {
            int index = i;
            var plugin = _vstHost.ActivePlugins[i];

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(80, 20, 26, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 47, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleSp = new StackPanel { Orientation = Orientation.Horizontal };
            titleSp.Children.Add(new TextBlock
            {
                Text = $"🎛️ {plugin.Name}",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 107, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = plugin.Format,
                FontSize = 9,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White
            };
            titleSp.Children.Add(badge);
            infoSp.Children.Add(titleSp);

            infoSp.Children.Add(new TextBlock
            {
                Text = plugin.FilePath,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420
            });
            grid.Children.Add(infoSp);

            var actionsSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(actionsSp, 1);

            var chkActive = new System.Windows.Controls.CheckBox
            {
                Content = "Actif",
                IsChecked = plugin.IsEnabled,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            chkActive.Click += (s, ev) => _vstHost.TogglePlugin(index, chkActive.IsChecked == true);
            actionsSp.Children.Add(chkActive);

            if (index > 0)
            {
                var btnUp = new Button
                {
                    Content = "▲",
                    Width = 26, Height = 26,
                    Margin = new Thickness(0, 0, 4, 0),
                    Style = (Style)FindResource("ActionButton")
                };
                btnUp.Click += (s, ev) => _vstHost.MovePlugin(index, -1);
                actionsSp.Children.Add(btnUp);
            }

            if (index < _vstHost.ActivePlugins.Count - 1)
            {
                var btnDown = new Button
                {
                    Content = "▼",
                    Width = 26, Height = 26,
                    Margin = new Thickness(0, 0, 4, 0),
                    Style = (Style)FindResource("ActionButton")
                };
                btnDown.Click += (s, ev) => _vstHost.MovePlugin(index, 1);
                actionsSp.Children.Add(btnDown);
            }

            var btnDel = new Button
            {
                Content = "🗑️",
                Width = 28, Height = 26,
                Style = (Style)FindResource("ActionButton")
            };
            btnDel.Click += (s, ev) => _vstHost.RemovePlugin(index);
            actionsSp.Children.Add(btnDel);

            grid.Children.Add(actionsSp);
            card.Child = grid;
            PanelVstRack.Children.Add(card);
        }
    }

    // =====================================================================
    // Tab 5: Spatial Audio 7.1
    // =====================================================================
    private void ChkSpatialAudio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVirtualSurround("Game", true, (float)SliderReverbGain.Value);
    }

    private void ChkSpatialAudio_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        _apo.SetVirtualSurround("Game", false, (float)SliderReverbGain.Value);
    }

    private void SliderReverbGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtReverbGainVal != null)
            TxtReverbGainVal.Text = $"{(int)e.NewValue} dB";
        if (!IsLoaded || _isSyncingUi) return;
        if (ChkSpatialAudio.IsChecked == true)
        {
            _apo.SetVirtualSurround("Game", true, (float)e.NewValue);
        }
    }

    // =====================================================================
    // Tab 6: Périphériques Audio & Tests
    // =====================================================================
    private void PopulateDevices()
    {
        var playbacks = DeviceManager.GetPlaybackDevices();
        ComboOutputDevice.ItemsSource = playbacks;
        ComboChatOutputDevice.ItemsSource = playbacks;
        ComboMediaOutputDevice.ItemsSource = playbacks;

        string currentOut = _apo.GetOutputDevice("Game");
        string currentChat = _apo.GetOutputDevice("ChatRender");
        string currentMedia = _apo.GetOutputDevice("Media");

        if (!string.IsNullOrEmpty(currentOut))
        {
            foreach (var d in playbacks)
            {
                if (d.Id.Equals(currentOut, StringComparison.OrdinalIgnoreCase))
                {
                    ComboOutputDevice.SelectedItem = d;
                    break;
                }
            }
        }
        if (ComboOutputDevice.SelectedIndex < 0 && playbacks.Count > 0)
        {
            ComboOutputDevice.SelectedIndex = 0;
        }

        if (!string.IsNullOrEmpty(currentChat))
        {
            foreach (var d in playbacks)
            {
                if (d.Id.Equals(currentChat, StringComparison.OrdinalIgnoreCase))
                {
                    ComboChatOutputDevice.SelectedItem = d;
                    break;
                }
            }
        }
        if (ComboChatOutputDevice.SelectedIndex < 0 && playbacks.Count > 0)
        {
            ComboChatOutputDevice.SelectedIndex = 0;
        }

        if (!string.IsNullOrEmpty(currentMedia))
        {
            foreach (var d in playbacks)
            {
                if (d.Id.Equals(currentMedia, StringComparison.OrdinalIgnoreCase))
                {
                    ComboMediaOutputDevice.SelectedItem = d;
                    break;
                }
            }
        }
        if (ComboMediaOutputDevice.SelectedIndex < 0 && playbacks.Count > 0)
        {
            ComboMediaOutputDevice.SelectedIndex = 0;
        }

        var captures = DeviceManager.GetCaptureDevices();
        ComboInputDevice.ItemsSource = captures;

        string savedIn = Registry.CurrentUser.OpenSubKey(@"Software\SonarGG")?.GetValue("SelectedInputDevice") as string ?? "";
        if (!string.IsNullOrEmpty(savedIn))
        {
            foreach (var d in captures)
            {
                if (d.Id.Equals(savedIn, StringComparison.OrdinalIgnoreCase))
                {
                    ComboInputDevice.SelectedItem = d;
                    break;
                }
            }
        }
        if (ComboInputDevice.SelectedIndex < 0 && captures.Count > 0)
        {
            ComboInputDevice.SelectedIndex = 0;
        }

        // Démarrage immédiat du flux direct Microphone vers Sonar Virtual Audio Device
        if (ComboInputDevice.SelectedItem is AudioDeviceInfo selIn)
        {
            string? selOut = ComboOutputDevice.SelectedItem is AudioDeviceInfo selD ? selD.Id : null;
            _micEngine.Start(selIn.Id, selOut);
            _apo.SetCaptureInputDevice(selIn.Id);
            _apo.GetSetting("ChatCapture", "kSet_StreamRedirectionState")?.SetBoolSetting(true);
            _apo.GetSetting("ChatCapture", "kSet_StreamRedirectionDeviceId")?.SetStringSetting(selIn.Id);
            _apo.GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionState")?.SetBoolSetting(true);
            _apo.GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionDeviceId")?.SetStringSetting(selIn.Id);
        }
    }

    private void ComboOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        if (ComboOutputDevice.SelectedItem is AudioDeviceInfo dev)
        {
            _apo.SetOutputDevice("Game", dev.Id);
            _apo.SetOutputDevice("Aux", dev.Id);
        }
    }

    private void ComboChatOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        if (ComboChatOutputDevice.SelectedItem is AudioDeviceInfo dev)
        {
            _apo.SetOutputDevice("ChatRender", dev.Id);
        }
    }

    private void ComboMediaOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isSyncingUi) return;
        if (ComboMediaOutputDevice.SelectedItem is AudioDeviceInfo dev)
        {
            _apo.SetOutputDevice("Media", dev.Id);
        }
    }

    private void ComboInputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboInputDevice.SelectedItem is AudioDeviceInfo dev)
        {
            _apo.SetCaptureInputDevice(dev.Id);
            _apo.GetSetting("ChatCapture", "kSet_StreamRedirectionState")?.SetBoolSetting(true);
            _apo.GetSetting("ChatCapture", "kSet_StreamRedirectionDeviceId")?.SetStringSetting(dev.Id);
            _apo.GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionState")?.SetBoolSetting(true);
            _apo.GetSetting("ChatCapture", "kSet_CaptureStreamRedirectionDeviceId")?.SetStringSetting(dev.Id);

            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\SonarGG");
                k?.SetValue("SelectedInputDevice", dev.Id);
            }
            catch { }

            string? selOut = ComboOutputDevice.SelectedItem is AudioDeviceInfo selD ? selD.Id : null;
            _micEngine.Start(dev.Id, selOut);
        }
    }

    private void BtnTestAudioTone_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                var waveOut = new NAudio.Wave.WaveOutEvent();
                int sampleRate = 48000;
                var samples = new List<float>();

                // Tone 1: 523 Hz (C5) Left ear only (0.22s)
                int len1 = (int)(sampleRate * 0.22);
                for (int i = 0; i < len1; i++)
                {
                    float t = (float)i / sampleRate;
                    float env = MathF.Sin(MathF.PI * (float)i / len1);
                    float val = MathF.Sin(2 * MathF.PI * 523.25f * t) * env * 0.35f;
                    samples.Add(val); // L
                    samples.Add(0f);  // R
                }

                // Tone 2: 659 Hz (E5) Right ear only (0.22s)
                int len2 = (int)(sampleRate * 0.22);
                for (int i = 0; i < len2; i++)
                {
                    float t = (float)i / sampleRate;
                    float env = MathF.Sin(MathF.PI * (float)i / len2);
                    float val = MathF.Sin(2 * MathF.PI * 659.25f * t) * env * 0.35f;
                    samples.Add(0f);  // L
                    samples.Add(val); // R
                }

                // Tone 3: 784 Hz (G5) Stereo Both ears (0.3s)
                int len3 = (int)(sampleRate * 0.3);
                for (int i = 0; i < len3; i++)
                {
                    float t = (float)i / sampleRate;
                    float env = MathF.Sin(MathF.PI * (float)i / len3);
                    float val = MathF.Sin(2 * MathF.PI * 783.99f * t) * env * 0.35f;
                    samples.Add(val); // L
                    samples.Add(val); // R
                }

                var waveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
                var buffer = new byte[samples.Count * 4];
                Buffer.BlockCopy(samples.ToArray(), 0, buffer, 0, buffer.Length);
                var ms = new System.IO.MemoryStream(buffer);
                var rawSource = new NAudio.Wave.RawSourceWaveStream(ms, waveFormat);

                waveOut.Init(rawSource);
                waveOut.Play();
                while (waveOut.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                {
                    System.Threading.Thread.Sleep(40);
                }
                waveOut.Dispose();
            }
            catch (Exception ex)
            {
                App.Log("PlayTestTone failed: " + ex.Message);
            }
        });
    }

    private void BtnTestMicMonitor_Click(object sender, RoutedEventArgs e)
    {
        BtnSidetoneToggle_Click(sender, e);
    }
    // =====================================================================
    // Tab 7: Paramètres & Démarrage Windows
    // =====================================================================
    private void ChkAutoStart_Checked(object sender, RoutedEventArgs e) => SetAutoStart(true);
    private void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e) => SetAutoStart(false);

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("SonarGG") != null;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue("SonarGG", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("SonarGG", false);
            }
        }
        catch (Exception ex)
        {
            App.Log("AutoStart Error: " + ex.Message);
        }
    }

    // =====================================================================
    // BILINGUAL LOCALIZATION (FR / EN)
    // =====================================================================
    private void BtnLangToggle_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.CurrentLanguage = LocalizationService.CurrentLanguage == "fr" ? "en" : "fr";
    }

    private void BtnLangFr_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.CurrentLanguage = "fr";
    }

    private void BtnLangEn_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.CurrentLanguage = "en";
    }

    private void ApplyLanguage()
    {
        bool isEn = LocalizationService.CurrentLanguage == "en";
        BtnLangToggle.Content = isEn ? "🌐 EN" : "🌐 FR";
        BtnMiniLang.Content = isEn ? "EN" : "FR";

        // Title & Badges
        TxtAppSubtitle.Text = LocalizationService.Get("AppSubtitle");
        TxtEngineBadge.Text = LocalizationService.Get("EngineActive");
        TxtEngineSub.Text = LocalizationService.Get("EngineSub");
        BtnPurgeGG.Content = LocalizationService.Get("BtnPurge");
        BtnPurgeGG.ToolTip = LocalizationService.Get("BtnPurgeTip");
        BtnMiniMode.Content = LocalizationService.Get("BtnMiniMode");
        BtnMiniMode.ToolTip = LocalizationService.Get("BtnMiniModeTip");

        // Tabs
        TabMixer.Header = LocalizationService.Get("TabMixer");
        TabChatMix.Header = LocalizationService.Get("TabChatMix");
        TabEq.Header = LocalizationService.Get("TabEq");
        TabMic.Header = LocalizationService.Get("TabMic");
        TabSpatial.Header = LocalizationService.Get("TabSpatial");
        TabDevices.Header = LocalizationService.Get("TabDevices");
        TabSettings.Header = LocalizationService.Get("TabSettings");
        TabShortcuts.Header = LocalizationService.Get("TabShortcuts");

        // Mixer Tab
        TxtMixerTitle.Text = LocalizationService.Get("MixerTitle");
        TxtMixerSub.Text = LocalizationService.Get("MixerSub");
        TxtAppRouterTitle.Text = LocalizationService.Get("AppRouterTitle");
        TxtAppRouterHint.Text = LocalizationService.Get("AppRouterHint");
        BtnRefreshApps.Content = LocalizationService.Get("BtnRefreshApps");

        // Channel Headers
        TxtGameHeader.Text = isEn ? "🎮 GAMING" : "🎮 JEU (GAME)";
        TxtChatHeader.Text = isEn ? "💬 VOICE CHAT" : "💬 CHAT VOCAL";
        TxtMediaHeader.Text = isEn ? "🎵 MEDIA" : "🎵 MÉDIA";
        TxtAuxHeader.Text = isEn ? "🎧 AUXILIARY" : "🎧 AUXILIAIRE";
        TxtMicHeader.Text = isEn ? "🎙️ MICROPHONE" : "🎙️ MICROPHONE";

        // Mini Mode Labels
        TxtMiniGameLbl.Text = isEn ? "Game" : "Jeu";
        TxtMiniChatLbl.Text = isEn ? "Chat" : "Chat";
        TxtMiniMediaLbl.Text = isEn ? "Media" : "Média";
        TxtMiniAuxLbl.Text = isEn ? "Aux" : "Aux";
        TxtMiniMicLbl.Text = isEn ? "Mic" : "Micro";

        // Shortcuts Tab
        TxtHkHeader.Text = LocalizationService.Get("HotkeysTitle");
        TxtHkSub.Text = LocalizationService.Get("HotkeysDesc");
        TxtHkStatus.Text = LocalizationService.Get("HkGlobalNotice");
        TxtHkMicTitle.Text = LocalizationService.Get("HkMuteMic");
        TxtHkGameTitle.Text = LocalizationService.Get("HkMuteGame");
        TxtHkChatTitle.Text = LocalizationService.Get("HkMuteChat");
        TxtHkMediaTitle.Text = LocalizationService.Get("HkMuteMedia");
        TxtHkAuxTitle.Text = LocalizationService.Get("HkMuteAux");
        TxtHkMiniTitle.Text = LocalizationService.Get("HkToggleMini");
        if (TxtHkVolUpTitle != null) TxtHkVolUpTitle.Text = LocalizationService.Get("HkVolUp");
        if (TxtHkVolDownTitle != null) TxtHkVolDownTitle.Text = LocalizationService.Get("HkVolDown");
        if (TxtHkClickHelp != null) TxtHkClickHelp.Text = LocalizationService.Get("HkClickToEdit");
        if (BtnResetHotkeys != null) BtnResetHotkeys.Content = LocalizationService.Get("BtnResetHotkeys");
        if (TxtSettingsUpdateTitle != null) TxtSettingsUpdateTitle.Text = LocalizationService.Get("SettingsUpdateTitle");
        if (BtnCheckUpdates != null) BtnCheckUpdates.Content = LocalizationService.Get("BtnCheckUpdates");
        if (BtnInstallUpdate != null) BtnInstallUpdate.Content = LocalizationService.Get("BtnInstallUpdate");
        if (TxtAppVersion != null) TxtAppVersion.Text = $"v{UpdateService.CurrentVersion}";

        // Refresh Mute buttons text
        if (BtnMuteGame != null) UpdateMuteButton(BtnMuteGame, _apo.GetMute("Game"));
        if (BtnMuteChat != null) UpdateMuteButton(BtnMuteChat, _apo.GetMute("ChatRender"));
        if (BtnMuteMedia != null) UpdateMuteButton(BtnMuteMedia, _apo.GetMute("Media"));
        if (BtnMuteAux != null) UpdateMuteButton(BtnMuteAux, _apo.GetMute("Aux"));
        if (BtnMuteMic != null) UpdateMuteButton(BtnMuteMic, _apo.GetMute("ChatCapture"));

        // Refresh active apps list
        _forceRefresh = true;
        RefreshAppsList();
    }

    // =====================================================================
    // GLOBAL HOTKEYS DISPATCHER
    // =====================================================================
        // =====================================================================
    // GESTION & ÉDITION DES RACCOURCIS CLAVIER GLOBAUX
    // =====================================================================
    private void UpdateHotkeysUi()
    {
        if (BtnHkMic == null) return;
        var mic = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMuteMic);
        if (mic != null) BtnHkMic.Content = mic.DisplayText;

        var game = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMuteGame);
        if (game != null) BtnHkGame.Content = game.DisplayText;

        var chat = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMuteChat);
        if (chat != null) BtnHkChat.Content = chat.DisplayText;

        var media = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMuteMedia);
        if (media != null) BtnHkMedia.Content = media.DisplayText;

        var aux = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMuteAux);
        if (aux != null) BtnHkAux.Content = aux.DisplayText;

        var mini = _hotkeyService.GetBinding(SonarHotkeyAction.ToggleMiniMode);
        if (mini != null) BtnHkMini.Content = mini.DisplayText;

        var volUp = _hotkeyService.GetBinding(SonarHotkeyAction.VolumeUp);
        if (volUp != null) BtnHkVolUp.Content = volUp.DisplayText;

        var volDown = _hotkeyService.GetBinding(SonarHotkeyAction.VolumeDown);
        if (volDown != null) BtnHkVolDown.Content = volDown.DisplayText;
    }

    private void BtnEditHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<SonarHotkeyAction>(tag, out var action))
        {
            if (_editingHotkeyButton != null && _editingHotkeyButton != btn)
            {
                UpdateHotkeysUi();
            }

            _editingHotkeyAction = action;
            _editingHotkeyButton = btn;
            bool isEn = LocalizationService.CurrentLanguage == "en";
            btn.Content = isEn ? "⌨️ Press keys..." : "⌨️ Appuyez sur vos touches...";
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00));
            btn.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x1E, 0x12));
        }
    }

    private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyService.ResetToDefaults();
        UpdateHotkeysUi();
        bool isEn = LocalizationService.CurrentLanguage == "en";
        MessageBox.Show(
            isEn ? "All keyboard shortcuts have been reset to factory defaults." : "Tous les raccourcis clavier ont été réinitialisés aux valeurs par défaut.",
            "SonarGG", MessageBoxButton.OK, MessageBoxImage.Information);
    }

private void OnHotkeyPressed(SonarHotkeyAction action)
    {
        Dispatcher.Invoke(() =>
        {
            switch (action)
            {
                case SonarHotkeyAction.ToggleMuteMic:
                    BtnMuteMic_Click(this, new RoutedEventArgs());
                    break;
                case SonarHotkeyAction.ToggleMuteGame:
                    BtnMuteGame_Click(this, new RoutedEventArgs());
                    break;
                case SonarHotkeyAction.ToggleMuteChat:
                    BtnMuteChat_Click(this, new RoutedEventArgs());
                    break;
                case SonarHotkeyAction.ToggleMuteMedia:
                    BtnMuteMedia_Click(this, new RoutedEventArgs());
                    break;
                case SonarHotkeyAction.ToggleMuteAux:
                    BtnMuteAux_Click(this, new RoutedEventArgs());
                    break;
                case SonarHotkeyAction.ToggleMiniMode:
                    ToggleMiniMode(!_isMiniMode);
                    break;
                case SonarHotkeyAction.VolumeUp:
                    AdjustMasterVolume(0.05f);
                    break;
                case SonarHotkeyAction.VolumeDown:
                    AdjustMasterVolume(-0.05f);
                    break;
            }
        });
    }

    private void AdjustMasterVolume(float delta)
    {
        float current = _apo.GetVolume("Game");
        float next = Math.Clamp(current + delta, 0f, 1f);
        _apo.SetVolume("Game", next);
        SliderGameCasque.Value = next;
        MiniSliderGame.Value = next;
    }

    // =====================================================================
    // =====================================================================
    // Tab 1 (Mixer): Routage des Applications & Multi-Sources (App Routing)
    // =====================================================================
    private void ScrollApps_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ScrollApps != null)
        {
            ScrollApps.ScrollToHorizontalOffset(ScrollApps.HorizontalOffset - (e.Delta * 0.9));
            e.Handled = true;
        }
    }

    private void BtnScrollAppsLeft_Click(object sender, RoutedEventArgs e)
    {
        ScrollApps?.ScrollToHorizontalOffset(ScrollApps.HorizontalOffset - 220);
    }

    private void BtnScrollAppsRight_Click(object sender, RoutedEventArgs e)
    {
        ScrollApps?.ScrollToHorizontalOffset(ScrollApps.HorizontalOffset + 220);
    }

    private void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
    {
        _forceRefresh = true;
        RefreshAppsList();
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(AudioAppItem)) || e.Data.GetDataPresent(typeof(AudioAppItem).FullName ?? ""))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void CardGame_Drop(object sender, DragEventArgs e) => HandleAppDrop(e, "Game");
    private void CardChat_Drop(object sender, DragEventArgs e) => HandleAppDrop(e, "Chat");
    private void CardMedia_Drop(object sender, DragEventArgs e) => HandleAppDrop(e, "Media");
    private void CardAux_Drop(object sender, DragEventArgs e) => HandleAppDrop(e, "Aux");

    private void HandleAppDrop(DragEventArgs e, string channel)
    {
        try
        {
            var app = e.Data.GetData(typeof(AudioAppItem)) as AudioAppItem
                   ?? e.Data.GetData(typeof(AudioAppItem).FullName ?? "") as AudioAppItem;

            if (app != null)
            {
                _routing.RouteApp(app.ProcessId, channel);
                _forceRefresh = true;
                RefreshAppsList();
            }
        }
        catch (Exception ex)
        {
            App.Log("HandleAppDrop Error: " + ex.Message);
        }
    }

    private void RefreshAppsList()
    {
        if (_isDraggingApp || Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            var apps = _routing.GetActiveApplications();
            bool isEn = LocalizationService.CurrentLanguage == "en";
            TxtAppCount.Text = isEn 
                ? $"{apps.Count} active application{(apps.Count > 1 ? "s" : "")}"
                : $"{apps.Count} application{(apps.Count > 1 ? "s" : "")} active{(apps.Count > 1 ? "s" : "")}";

            var sb = new System.Text.StringBuilder();
            foreach (var a in apps)
            {
                sb.Append(a.ProcessId).Append(':')
                  .Append(a.CurrentChannel).Append(':')
                  .Append(a.IsMuted).Append(':')
                  .Append((int)(a.Volume * 100)).Append(';');
            }
            string sig = sb.ToString();

            if (!_forceRefresh && sig == _lastAppSignature)
            {
                return;
            }

            _forceRefresh = false;
            _lastAppSignature = sig;

            ItemsActiveApps.Items.Clear();
            PanelAppsGame.Children.Clear();
            PanelAppsChat.Children.Clear();
            PanelAppsMedia.Children.Clear();
            PanelAppsAux.Children.Clear();

            int countGame = 0, countChat = 0, countMedia = 0, countAux = 0;

            foreach (var app in apps)
            {
                var card = CreateAppCard(app);
                ItemsActiveApps.Items.Add(card);

                var chip = CreateAppChip(app);
                switch (app.CurrentChannel)
                {
                    case "Game":
                        PanelAppsGame.Children.Add(chip);
                        countGame++;
                        break;
                    case "Chat":
                        PanelAppsChat.Children.Add(chip);
                        countChat++;
                        break;
                    case "Media":
                        PanelAppsMedia.Children.Add(chip);
                        countMedia++;
                        break;
                    case "Aux":
                        PanelAppsAux.Children.Add(chip);
                        countAux++;
                        break;
                }
            }

            if (countGame == 0) AddEmptyPlaceholder(PanelAppsGame);
            if (countChat == 0) AddEmptyPlaceholder(PanelAppsChat);
            if (countMedia == 0) AddEmptyPlaceholder(PanelAppsMedia);
            if (countAux == 0) AddEmptyPlaceholder(PanelAppsAux);
        }
        catch (Exception ex)
        {
            App.Log("RefreshAppsList Error: " + ex.Message);
        }
    }

    private Border CreateAppCard(AudioAppItem app)
    {
        bool isEn = LocalizationService.CurrentLanguage == "en";
        var card = new Border
        {
            Width = 196,
            Background = new SolidColorBrush(Color.FromRgb(17, 22, 33)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(32, 42, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 10, 2),
            Cursor = Cursors.Hand,
            ToolTip = $"{app.DisplayName} (PID: {app.ProcessId}) — " + 
                      (isEn ? "Drag to a column or click a channel" : "Glisser vers une colonne ou cliquer sur un canal")
        };

        // Support Glisser-Déposer
        card.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _dragStartPoint = e.GetPosition(card);
            _isDraggingApp = false;
        };

        card.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingApp)
            {
                Point p = e.GetPosition(card);
                if (Math.Abs(p.X - _dragStartPoint.X) > 6 || Math.Abs(p.Y - _dragStartPoint.Y) > 6)
                {
                    if (e.OriginalSource is not (Button or Slider or Path or Thumb))
                    {
                        _isDraggingApp = true;
                        DragDrop.DoDragDrop(card, app, DragDropEffects.Move);
                        _isDraggingApp = false;
                    }
                }
            }
        };

        var rootPanel = new StackPanel();

        // 1. Top row: Icon + App Name + Channel Badge
        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var appInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (app.Icon != null)
        {
            appInfo.Children.Add(new Image { Source = app.Icon, Width = 19, Height = 19, Margin = new Thickness(0, 0, 7, 0) });
        }
        else
        {
            appInfo.Children.Add(new TextBlock { Text = "🔊", FontSize = 12, Margin = new Thickness(0, 0, 7, 0) });
        }

        string shortName = app.ProcessName.Length > 11 ? app.ProcessName[..10] + "…" : app.ProcessName;
        appInfo.Children.Add(new TextBlock
        {
            Text = shortName,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(appInfo, 0);
        topGrid.Children.Add(appInfo);

        // Badge canal lumineux
        (string chLabel, Color chColor, Color chBg) = app.CurrentChannel switch
        {
            "Game" => (isEn ? "GAME" : "JEU", Color.FromRgb(255, 85, 0), Color.FromRgb(45, 23, 13)),
            "Chat" => ("CHAT", Color.FromRgb(0, 180, 216), Color.FromRgb(13, 35, 48)),
            "Media" => (isEn ? "MEDIA" : "MÉDIA", Color.FromRgb(192, 132, 252), Color.FromRgb(36, 20, 52)),
            "Aux" => ("AUX", Color.FromRgb(52, 211, 153), Color.FromRgb(12, 38, 29)),
            _ => (isEn ? "DEF" : "DÉF", Color.FromRgb(100, 116, 139), Color.FromRgb(22, 28, 40))
        };

        var chBadge = new Border
        {
            Background = new SolidColorBrush(chBg),
            BorderBrush = new SolidColorBrush(chColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        chBadge.Child = new TextBlock
        {
            Text = chLabel,
            FontSize = 9,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(chColor)
        };
        Grid.SetColumn(chBadge, 1);
        topGrid.Children.Add(chBadge);
        rootPanel.Children.Add(topGrid);

        // 2. Segmented channel buttons: [🎮] [💬] [🎵] [🎧] (Illuminated & Ultra-Clean)
        var btnRow = new UniformGrid { Columns = 4, Margin = new Thickness(0, 7, 0, 7) };

        void AddIconBtn(string targetCh, string icon, string title, Color col)
        {
            bool isCurrent = app.CurrentChannel.Equals(targetCh, StringComparison.OrdinalIgnoreCase);
            var b = new Button
            {
                Content = icon,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Background = isCurrent ? new SolidColorBrush(col) : new SolidColorBrush(Color.FromRgb(24, 30, 44)),
                Foreground = isCurrent ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderBrush = isCurrent ? new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) : new SolidColorBrush(Color.FromRgb(38, 48, 68)),
                BorderThickness = new Thickness(isCurrent ? 1.5 : 1),
                Cursor = Cursors.Hand,
                ToolTip = $"{icon} {title} — {(isEn ? "Click to route here" : "Cliquer pour assigner à ce canal")}"
            };

            if (isCurrent)
            {
                b.Effect = new DropShadowEffect
                {
                    Color = col,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }

            b.Click += (s, e) =>
            {
                _routing.RouteApp(app.ProcessId, targetCh);
                _forceRefresh = true;
                RefreshAppsList();
            };
            btnRow.Children.Add(b);
        }

        AddIconBtn("Game", "🎮", isEn ? "Game" : "Jeu", Color.FromRgb(255, 85, 0));
        AddIconBtn("Chat", "💬", isEn ? "Chat" : "Chat Vocal", Color.FromRgb(0, 180, 216));
        AddIconBtn("Media", "🎵", isEn ? "Media" : "Média", Color.FromRgb(192, 132, 252));
        AddIconBtn("Aux", "🎧", isEn ? "Aux" : "Auxiliaire", Color.FromRgb(52, 211, 153));
        rootPanel.Children.Add(btnRow);

        // 3. Mini Volume row: [🔊] Slider [100%]
        var volGrid = new Grid();
        volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnMute = new Button
        {
            Content = app.IsMuted ? "🔇" : "🔊",
            FontSize = 10,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Background = app.IsMuted ? new SolidColorBrush(Color.FromRgb(225, 29, 72)) : new SolidColorBrush(Color.FromRgb(24, 30, 44)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 48, 68)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = app.IsMuted ? (isEn ? "Unmute" : "Réactiver le son") : (isEn ? "Mute" : "Couper le son")
        };
        btnMute.Click += (s, e) =>
        {
            _routing.SetAppMute(app.ProcessId, !app.IsMuted);
            _forceRefresh = true;
            RefreshAppsList();
        };
        Grid.SetColumn(btnMute, 0);
        volGrid.Children.Add(btnMute);

        var volSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = app.Volume,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 18
        };
        var lblVol = new TextBlock
        {
            Text = $"{(int)(app.Volume * 100)}%",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Width = 28
        };
        volSlider.ValueChanged += (s, e) =>
        {
            lblVol.Text = $"{(int)(volSlider.Value * 100)}%";
            _routing.SetAppVolume(app.ProcessId, (float)volSlider.Value);
        };
        Grid.SetColumn(volSlider, 1);
        Grid.SetColumn(lblVol, 2);
        volGrid.Children.Add(volSlider);
        volGrid.Children.Add(lblVol);
        rootPanel.Children.Add(volGrid);

        card.Child = rootPanel;
        return card;
    }

    private Border CreateAppChip(AudioAppItem app)
    {
        bool isEn = LocalizationService.CurrentLanguage == "en";
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 29, 44)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(40, 52, 76)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = $"{app.DisplayName} (PID: {app.ProcessId}) — " + 
                      (isEn ? "Drag to another column" : "Glisser vers une autre colonne")
        };

        chip.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _dragStartPoint = e.GetPosition(chip);
            _isDraggingApp = false;
        };

        chip.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingApp)
            {
                Point p = e.GetPosition(chip);
                if (Math.Abs(p.X - _dragStartPoint.X) > 6 || Math.Abs(p.Y - _dragStartPoint.Y) > 6)
                {
                    _isDraggingApp = true;
                    DragDrop.DoDragDrop(chip, app, DragDropEffects.Move);
                    _isDraggingApp = false;
                }
            }
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (app.Icon != null)
        {
            sp.Children.Add(new Image { Source = app.Icon, Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            sp.Children.Add(new TextBlock { Text = "🔊", FontSize = 10, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        string name = app.ProcessName.Length > 10 ? app.ProcessName[..9] + "…" : app.ProcessName;
        sp.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            VerticalAlignment = VerticalAlignment.Center
        });

        chip.Child = sp;
        return chip;
    }

    private static void AddEmptyPlaceholder(WrapPanel panel)
    {
        bool isEn = LocalizationService.CurrentLanguage == "en";
        var tb = new TextBlock
        {
            Text = isEn ? "Drop an app here" : "Glisser une app ici",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(4)
        };
        panel.Children.Add(tb);
    }

    // =====================================================================
    // GITHUB AUTO-UPDATE SYSTEM
    // =====================================================================
    private UpdateInfo? _latestUpdate;

    private async void CheckUpdatesSilently()
    {
        try
        {
            var update = await UpdateService.CheckForUpdatesAsync();
            if (update != null)
            {
                _latestUpdate = update;
                Dispatcher.Invoke(() => ShowUpdateBanner(update));
            }
        }
        catch { }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        TxtUpdateStatus.Text = LocalizationService.Get("CheckingUpdates");

        try
        {
            var update = await UpdateService.CheckForUpdatesAsync();
            if (update != null)
            {
                _latestUpdate = update;
                ShowUpdateBanner(update);
            }
            else
            {
                BorderUpdateAction.Visibility = Visibility.Collapsed;
                TxtUpdateStatus.Text = LocalizationService.Get("NoUpdateAvailable");
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "Erreur: " + ex.Message;
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private void ShowUpdateBanner(UpdateInfo update)
    {
        TxtUpdateStatus.Text = LocalizationService.Get("UpdateAvailable");
        TxtUpdateAvailableTitle.Text = $"🎉 SonarGG {update.TagName} ({update.Title})";
        TxtUpdateChangelog.Text = !string.IsNullOrWhiteSpace(update.Changelog)
            ? update.Changelog.Replace("\r", "").Split('\n')[0]
            : "Cliquez sur installer pour télécharger et mettre à jour automatiquement.";
        BorderUpdateAction.Visibility = Visibility.Visible;
    }

    private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate == null) return;

        if (!_latestUpdate.HasAsset)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _latestUpdate.HtmlUrl,
                UseShellExecute = true
            });
            return;
        }

        BtnInstallUpdate.IsEnabled = false;
        ProgressDownload.Visibility = Visibility.Visible;
        ProgressDownload.Value = 0;

        var progress = new Progress<int>(pct =>
        {
            ProgressDownload.Value = pct;
            TxtUpdateStatus.Text = string.Format(LocalizationService.Get("DownloadingUpdate"), pct);
        });

        bool success = await UpdateService.DownloadAndApplyUpdateAsync(_latestUpdate.DownloadUrl, progress);
        if (!success)
        {
            BtnInstallUpdate.IsEnabled = true;
            ProgressDownload.Visibility = Visibility.Collapsed;
            TxtUpdateStatus.Text = LocalizationService.Get("UpdateDownloadError");
        }
    }

}
