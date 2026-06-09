using System.Threading;
using System.Windows;
using TodoSnap.Helpers;
using TodoSnap.Services;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;

namespace TodoSnap;

/// <summary>
/// Application entry point. Owns the single-instance mutex, the shared services,
/// the tray icon and the two windows (main + floating bar).
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private WinForms.NotifyIcon? _tray;

    private DataService _data = null!;
    private AnalysisService _analysis = null!;
    private HotkeyService _hotkey = null!;
    private MainWindow _main = null!;
    private FloatingWindow _floating = null!;
    private SettingsWindow? _settings;

    /// <summary>Set to true only when the user really wants to quit (tray → 退出).</summary>
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- Single instance guard ---------------------------------------
        _mutex = new Mutex(initiallyOwned: true, "TodoSnap_SingleInstance_2D6F8A1E", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TodoSnap 已在运行。", "TodoSnap",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // --- Shared services ---------------------------------------------
        _data = new DataService();
        _analysis = new AnalysisService(_data.Directory);
        _hotkey = new HotkeyService();

        // Apply the saved theme before any window is created so first paint is correct.
        ThemeManager.Apply(_data.Config.Theme);

        // --- Windows ------------------------------------------------------
        _floating = new FloatingWindow(_data);
        _main = new MainWindow(_data, _analysis, _floating);

        // The main window hosts the global hotkey (its HWND survives Hide()).
        _main.HandleReady += hwnd =>
        {
            _hotkey.Attach(hwnd);
            var (mods, key) = HotkeyService.Parse(_data.Config.HotkeyMods, _data.Config.HotkeyKey);
            _hotkey.TryRegister(mods, key);
        };
        _hotkey.Triggered += ShowMain;
        _main.SettingsRequested += ShowSettings;

        // Closing the main window only hides it; real exit goes through the tray.
        _main.Closing += (s, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;
            _main.Hide();
        };

        if (_data.Config.FloatingVisible)
            _floating.Show();

        // --- Tray icon ----------------------------------------------------
        BuildTray();

        // Start with the main window visible so the user can paste right away.
        ShowMain();
    }

    private void BuildTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (s, e) => ShowMain());
        menu.Items.Add("设置", null, (s, e) => ShowSettings());
        menu.Items.Add("显示/隐藏待办栏", null, (s, e) => ToggleFloating());

        var autoStart = new WinForms.ToolStripMenuItem("开机自启")
        {
            Checked = StartupHelper.IsEnabled(),
            CheckOnClick = true
        };
        autoStart.CheckedChanged += (s, e) => StartupHelper.SetEnabled(autoStart.Checked);
        menu.Items.Add(autoStart);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ExitApp());

        _tray = new WinForms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Visible = true,
            Text = "TodoSnap",
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                ShowMain();
        };
    }

    private void ShowMain()
    {
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.FocusEditor();
    }

    private void ToggleFloating() => SetFloatingVisible(!_floating.IsVisible);

    private void SetFloatingVisible(bool visible)
    {
        if (visible) _floating.Show();
        else _floating.Hide();
        _data.Config.FloatingVisible = visible;
        _data.SaveConfig();
    }

    private void ShowSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(
                _data, _analysis, _hotkey,
                applyOpacity: o => { _main.ApplyTransparency(o); _floating.ApplyTransparency(o); },
                applyFloatingVisible: SetFloatingVisible,
                resetFloating: () => _floating.ResetPosition());
            _settings.Closed += (s, e) => _settings = null;
            _settings.Show();
        }
        else
        {
            _settings.Activate();
        }
    }

    private void ExitApp()
    {
        _exiting = true;
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _hotkey?.Dispose();
        _settings?.Close();
        _floating?.Close();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
