using System.Windows;
using System.Windows.Input;
using TodoSnap.Helpers;
using TodoSnap.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TodoSnap;

/// <summary>
/// The unified settings surface: appearance (theme + transparency), the global
/// hotkey (with conflict detection), online-AI credentials, and system toggles.
/// Changes apply live through callbacks supplied by <see cref="App"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly DataService _data;
    private readonly AnalysisService _analysis;
    private readonly HotkeyService _hotkey;

    private readonly Action<double> _applyOpacity;
    private readonly Action<bool> _applyFloatingVisible;
    private readonly Action _resetFloating;

    private bool _loading;
    private bool _capturing;

    /// <summary>True when the API key is currently shown in plain text (eye toggle).</summary>
    private bool _keyVisible;
    /// <summary>Re-entrancy guard so the password<->plain-text sync doesn't ping-pong.</summary>
    private bool _syncingKey;

    // The hotkey currently registered, so we can restore it if a new pick conflicts.
    private ModifierKeys _curMods;
    private Key _curKey;

    public SettingsWindow(
        DataService data,
        AnalysisService analysis,
        HotkeyService hotkey,
        Action<double> applyOpacity,
        Action<bool> applyFloatingVisible,
        Action resetFloating)
    {
        _data = data;
        _analysis = analysis;
        _hotkey = hotkey;
        _applyOpacity = applyOpacity;
        _applyFloatingVisible = applyFloatingVisible;
        _resetFloating = resetFloating;

        // Guard against control events firing during XAML init (e.g. Slider.ValueChanged).
        _loading = true;
        InitializeComponent();
        LoadFromConfig();

        // Reflect background probes (e.g. the main window's startup check) live.
        _analysis.OnlineStatusChanged += OnOnlineChanged;
        _analysis.LastErrorChanged += OnLastErrorChanged;
        Closed += (_, _) =>
        {
            _analysis.OnlineStatusChanged -= OnOnlineChanged;
            _analysis.LastErrorChanged -= OnLastErrorChanged;
        };
    }

    private void OnOnlineChanged(bool _ok) => Dispatcher.Invoke(UpdateApiStatus);
    private void OnLastErrorChanged(string _msg) => Dispatcher.Invoke(UpdateApiStatus);

    private void LoadFromConfig()
    {
        var cfg = _data.Config;

        ApplyTransparency(cfg.BackgroundOpacity);          // skin the settings window itself
        ChkLight.IsChecked = cfg.Theme == "light";
        SldOpacity.Value = cfg.BackgroundOpacity;

        (_curMods, _curKey) = HotkeyService.Parse(cfg.HotkeyMods, cfg.HotkeyKey);
        TxtHotkey.Text = HotkeyService.Describe(_curMods, _curKey);

        var (key, endpoint, model) = _analysis.ReadApiKey();
        _syncingKey = true;
        PwApiKey.Password = key;
        TxtApiKey.Text = key;
        _syncingKey = false;
        UpdateApiKeyHint();

        TxtEndpoint.Text = endpoint == "https://api.openai.com/v1/chat/completions" ? "" : endpoint;
        TxtModel.Text = model == "gpt-4o-mini" ? "" : model;
        UpdateApiStatus();

        ChkStartup.IsChecked = StartupHelper.IsEnabled();
        ChkFloating.IsChecked = cfg.FloatingVisible;

        _loading = false;
    }

    public void ApplyTransparency(double opacity) => BgLayer.Opacity = opacity;

    private void UpdateApiStatus()
    {
        if (!_analysis.OnlineConfigured)
        {
            TxtApiStatus.Text = "当前：离线 OCR";
            TxtApiError.Visibility = Visibility.Collapsed;
            return;
        }

        string err = _analysis.LastError;
        if (_analysis.OnlineAvailable)
        {
            TxtApiStatus.Text = "在线模式：测试通过";
            TxtApiError.Visibility = Visibility.Collapsed;
        }
        else if (!string.IsNullOrEmpty(err))
        {
            TxtApiStatus.Text = "在线模式：调用失败";
            TxtApiError.Text = err;
            TxtApiError.Visibility = Visibility.Visible;
        }
        else
        {
            TxtApiStatus.Text = "在线模式：检测中…";
            TxtApiError.Visibility = Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------------ chrome

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore */ }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_capturing) Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // --------------------------------------------------------------- appearance

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string theme = ChkLight.IsChecked == true ? "light" : "dark";
        ThemeManager.Apply(theme);
        _data.Config.Theme = theme;
        _data.SaveConfig();
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        double o = e.NewValue;
        ApplyTransparency(o);   // this window
        _applyOpacity(o);       // main + floating
        _data.Config.BackgroundOpacity = o;
        _data.SaveConfig();
    }

    // ----------------------------------------------------------------- hotkey

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        TxtHotkey.Text = "请按下组合键…";
        TxtHotkeyHint.Visibility = Visibility.Collapsed;
        TxtHotkey.Focus();
    }

    private void Hotkey_GotFocus(object sender, RoutedEventArgs e) { }

    private void Hotkey_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            _capturing = false;
            TxtHotkey.Text = HotkeyService.Describe(_curMods, _curKey);
        }
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return; // wait for the non-modifier key

        var mods = Keyboard.Modifiers;
        ApplyHotkey(mods, key);
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
          or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private void ApplyHotkey(ModifierKeys mods, Key key)
    {
        _capturing = false;

        if (_hotkey.TryRegister(mods, key))
        {
            _curMods = mods;
            _curKey = key;
            var (m, k) = HotkeyService.ToConfig(mods, key);
            _data.Config.HotkeyMods = m;
            _data.Config.HotkeyKey = k;
            _data.SaveConfig();

            TxtHotkey.Text = HotkeyService.Describe(mods, key);
            TxtHotkeyHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Conflict or invalid → restore the previous binding and warn.
            _hotkey.TryRegister(_curMods, _curKey);
            TxtHotkey.Text = HotkeyService.Describe(_curMods, _curKey);
            TxtHotkeyHint.Text = $"“{HotkeyService.Describe(mods, key)}” 已被占用，请换一个";
            TxtHotkeyHint.Visibility = Visibility.Visible;
        }
    }

    // -------------------------------------------------------------------- API

    private async void SaveApi_Click(object sender, RoutedEventArgs e)
    {
        string key = CurrentApiKey();
        _analysis.SaveApiKey(key, TxtEndpoint.Text, TxtModel.Text);
        UpdateApiStatus();
        // Issue a real chat-completions ping so endpoint URL / auth / model name are all
        // validated and any failure reason flows into the visible error label.
        await _analysis.CheckConnectivityAsync();
        UpdateApiStatus();
    }

    // ---------------------------------------------------------- API key masking

    private string CurrentApiKey() => _keyVisible ? TxtApiKey.Text : PwApiKey.Password;

    private void ApiKey_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingKey) return;
        _syncingKey = true;
        TxtApiKey.Text = PwApiKey.Password;
        _syncingKey = false;
        UpdateApiKeyHint();
    }

    private void ApiKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingKey) return;
        _syncingKey = true;
        PwApiKey.Password = TxtApiKey.Text;
        _syncingKey = false;
        UpdateApiKeyHint();
    }

    private void RevealKey_Click(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;
        if (_keyVisible)
        {
            PwApiKey.Visibility = Visibility.Collapsed;
            TxtApiKey.Visibility = Visibility.Visible;
            BtnRevealKey.Content = "隐藏";
            TxtApiKey.Focus();
            TxtApiKey.CaretIndex = TxtApiKey.Text.Length;
        }
        else
        {
            TxtApiKey.Visibility = Visibility.Collapsed;
            PwApiKey.Visibility = Visibility.Visible;
            BtnRevealKey.Content = "显示";
            PwApiKey.Focus();
        }
        UpdateApiKeyHint();
    }

    private void UpdateApiKeyHint()
    {
        bool empty = string.IsNullOrEmpty(CurrentApiKey());
        TxtApiKeyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----------------------------------------------------------------- system

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        StartupHelper.SetEnabled(ChkStartup.IsChecked == true);
    }

    private void Floating_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _applyFloatingVisible(ChkFloating.IsChecked == true);
    }

    private void ResetFloating_Click(object sender, RoutedEventArgs e) => _resetFloating();
}
