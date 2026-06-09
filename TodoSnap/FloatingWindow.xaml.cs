using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TodoSnap.Helpers;
using TodoSnap.Models;
using TodoSnap.Services;

namespace TodoSnap;

/// <summary>
/// The always-on-top floating to-do bar. Two states:
///   • Expanded — resizable list (up to <see cref="MaxVisible"/> items + "+N 更多").
///   • Collapsed — a thin strip docked to a screen edge, showing a count badge.
/// Dragging the bar to an edge collapses it; clicking the strip expands it. Adding
/// a task while collapsed briefly "peeks" the list open, then auto-collapses.
/// </summary>
public partial class FloatingWindow : Window
{
    private const int MaxVisible = 5;

    // Collapsed-strip geometry and edge-snap sensitivity.
    private const double StripThick = 18;
    private const double StripLen = 90;
    private const double SnapThreshold = 24;

    private readonly DataService _data;

    private readonly ObservableCollection<TaskItem> _all = new();
    private readonly ObservableCollection<TaskItem> _visible = new();

    private bool _expanded;   // list "+N 更多" expander state (not the dock state)
    private bool _loaded;

    private bool _collapsed;          // docked-to-edge state
    private string _dockEdge = "None";
    private Rect _expandedBounds;      // geometry to restore when expanding

    // Peek (add-feedback) auto-collapse timer.
    private readonly DispatcherTimer _peekTimer;
    private bool _peeking;
    private string _peekReturnEdge = "None"; // edge to re-dock to after the peek

    // Debounced persistence of user resize.
    private readonly DispatcherTimer _saveSizeTimer;

    // Bounds tween (render-thread driven, vsync-aligned).
    private bool _animating;
    private Stopwatch? _animClock;
    private TimeSpan _animDuration = TimeSpan.FromMilliseconds(180);
    private double _lFrom, _tFrom, _wFrom, _hFrom;
    private double _lTo, _tTo, _wTo, _hTo;
    private Action? _animDone;
    private EventHandler? _animTick;

    public FloatingWindow(DataService data)
    {
        _data = data;
        InitializeComponent();

        List.ItemsSource = _visible;

        foreach (var t in _data.LoadTasks())
            _all.Add(t);

        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
        _peekTimer.Tick += PeekTimer_Tick;

        _saveSizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveSizeTimer.Tick += (_, _) =>
        {
            _saveSizeTimer.Stop();
            if (!_collapsed && !IsTweening)
            {
                _data.Config.FloatingWidth = Width;
                _data.Config.FloatingHeight = Height;
                _data.SaveConfig();
            }
        };

        ApplyTransparency(_data.Config.BackgroundOpacity);
        RefreshVisible();
    }

    private bool IsTweening => _animating;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clicks must not steal focus from the user's foreground app.
        WindowInterop.MakeNonActivating(this);
    }

    /// <summary>Fade only the background layer; text/badges stay fully opaque.</summary>
    public void ApplyTransparency(double opacity) => BgLayer.Opacity = opacity;

    /// <summary>Undock and restore the bar to its default size at the right-center.</summary>
    public void ResetPosition()
    {
        StopAnimation(commit: false);
        _collapsed = false;
        _dockEdge = "None";
        _peeking = false;
        _peekTimer.Stop();

        CollapsedStrip.Visibility = Visibility.Collapsed;
        ExpandedRoot.Visibility = Visibility.Visible;
        ResizeMode = ResizeMode.CanResize;

        Width = 260;
        Height = 320;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 20;
        Top = wa.Top + (wa.Height - Height) / 2;

        var cfg = _data.Config;
        cfg.FloatingWidth = Width; cfg.FloatingHeight = Height;
        cfg.FloatingLeft = Left; cfg.FloatingTop = Top;
        cfg.FloatingCollapsed = false; cfg.DockEdge = "None";
        _data.SaveConfig();

        if (!IsVisible && cfg.FloatingVisible) Show();
    }

    // ------------------------------------------------------------- positioning

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var cfg = _data.Config;
        Width = cfg.FloatingWidth > 0 ? cfg.FloatingWidth : 260;
        Height = cfg.FloatingHeight > 0 ? cfg.FloatingHeight : 320;

        if (cfg.FloatingLeft >= 0 && cfg.FloatingTop >= 0 && IsOnScreen(cfg.FloatingLeft, cfg.FloatingTop))
        {
            Left = cfg.FloatingLeft;
            Top = cfg.FloatingTop;
        }
        else
        {
            // Default: right edge, vertically centered, 20px margin.
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 20;
            Top = wa.Top + (wa.Height - Height) / 2;
        }

        _expandedBounds = new Rect(Left, Top, Width, Height);
        _loaded = true;

        // Restore a previously docked/collapsed state without animating.
        if (cfg.FloatingCollapsed && cfg.DockEdge != "None")
            EnterCollapsed(cfg.DockEdge, animate: false);
    }

    private void Window_LocationChanged(object sender, EventArgs e)
    {
        if (!_loaded || _collapsed || IsTweening) return;
        _data.Config.FloatingLeft = Left;
        _data.Config.FloatingTop = Top;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_loaded || _collapsed || IsTweening) return;
        // Persist user resize, debounced so dragging doesn't hammer the disk.
        _saveSizeTimer.Stop();
        _saveSizeTimer.Start();
    }

    private static bool IsOnScreen(double left, double top)
    {
        var vs = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return vs.Contains(new Point(left + 20, top + 20));
    }

    private static void ClampToWorkArea(ref double left, ref double top, double w, double h)
    {
        var wa = SystemParameters.WorkArea;
        left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
        top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
    }

    // ------------------------------------------------------------- drag + snap

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Complete buttons mark e.Handled, so reaching here means an empty-area press.
        if (e.ButtonState != MouseButtonState.Pressed) return;

        // A deliberate drag commits to staying expanded (cancels any pending peek).
        CancelPeek();

        try { DragMove(); } catch { /* ignore */ }

        // After the drag, snap to the nearest edge if close enough.
        string edge = NearestEdge();
        if (edge != "None")
        {
            EnterCollapsed(edge, animate: true);
        }
        else
        {
            _data.Config.FloatingLeft = Left;
            _data.Config.FloatingTop = Top;
            _expandedBounds = new Rect(Left, Top, Width, Height);
            _data.SaveConfig();
        }
    }

    /// <summary>Which work-area edge (if any) the window is currently within snapping distance of.</summary>
    private string NearestEdge()
    {
        var wa = SystemParameters.WorkArea;
        if (Left - wa.Left <= SnapThreshold) return "Left";
        if (wa.Right - (Left + Width) <= SnapThreshold) return "Right";
        if (Top - wa.Top <= SnapThreshold) return "Top";
        if (wa.Bottom - (Top + Height) <= SnapThreshold) return "Bottom";
        return "None";
    }

    // ------------------------------------------------------- collapse / expand

    private void EnterCollapsed(string edge, bool animate)
    {
        // Remember where to spring back to.
        if (!_collapsed)
            _expandedBounds = new Rect(Left, Top, Width, Height);

        _collapsed = true;
        _dockEdge = edge;
        ResizeMode = ResizeMode.NoResize;

        var (l, t, w, h) = CollapsedBounds(edge);

        void Show()
        {
            ExpandedRoot.Visibility = Visibility.Collapsed;
            CollapsedStrip.Visibility = Visibility.Visible;
            UpdateBadge();
        }

        _data.Config.FloatingCollapsed = true;
        _data.Config.DockEdge = edge;
        _data.SaveConfig();

        if (animate)
        {
            // Hide the list immediately so the shrink reads as "tucking away".
            Show();
            AnimateBounds(l, t, w, h, null);
        }
        else
        {
            Left = l; Top = t; Width = w; Height = h;
            Show();
        }
    }

    private (double l, double t, double w, double h) CollapsedBounds(string edge)
    {
        var wa = SystemParameters.WorkArea;
        // Keep the strip centred on the side it docks to, derived from current center.
        double cx = Left + Width / 2;
        double cy = Top + Height / 2;
        return edge switch
        {
            "Left" => (wa.Left, Clamp(cy - StripLen / 2, wa.Top, wa.Bottom - StripLen), StripThick, StripLen),
            "Right" => (wa.Right - StripThick, Clamp(cy - StripLen / 2, wa.Top, wa.Bottom - StripLen), StripThick, StripLen),
            "Top" => (Clamp(cx - StripLen / 2, wa.Left, wa.Right - StripLen), wa.Top, StripLen, StripThick),
            _ => (Clamp(cx - StripLen / 2, wa.Left, wa.Right - StripLen), wa.Bottom - StripThick, StripLen, StripThick),
        };
    }

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(v, hi));

    private void ExitCollapsed(bool animate)
    {
        if (!_collapsed) return;

        double l = _expandedBounds.Left, t = _expandedBounds.Top;
        double w = _expandedBounds.Width > 0 ? _expandedBounds.Width : 260;
        double h = _expandedBounds.Height > 0 ? _expandedBounds.Height : 320;
        ClampToWorkArea(ref l, ref t, w, h);

        _collapsed = false;
        _dockEdge = "None";

        CollapsedStrip.Visibility = Visibility.Collapsed;
        ExpandedRoot.Visibility = Visibility.Visible;

        _data.Config.FloatingCollapsed = false;
        _data.Config.DockEdge = "None";
        _data.Config.FloatingLeft = l;
        _data.Config.FloatingTop = t;
        _data.SaveConfig();

        void Done()
        {
            ResizeMode = ResizeMode.CanResize;
        }

        if (animate)
            AnimateBounds(l, t, w, h, Done);
        else
        {
            Left = l; Top = t; Width = w; Height = h;
            Done();
        }
    }

    private void Strip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CancelPeek();
        ExitCollapsed(animate: true);
    }

    // --------------------------------------------------------- bounds tween
    //
    // The earlier implementation drove a DispatcherTimer at ~14 ms and set
    // Left/Top/Width/Height as four separate WPF property writes per tick — each
    // one round-tripped through SetWindowPos and forced a full WPF layout pass,
    // and DispatcherTimer drift made the motion stutter.
    //
    // This version is render-thread driven (CompositionTarget.Rendering ⇒ aligned
    // with vsync) and pushes the new bounds in a single atomic SetWindowPos call,
    // collapsing four layout passes per frame into one. Lightweight and smooth.

    private void AnimateBounds(double left, double top, double width, double height, Action? done)
    {
        StopAnimation(commit: false);

        _lFrom = Left; _tFrom = Top; _wFrom = Width; _hFrom = Height;
        _lTo = left; _tTo = top; _wTo = width; _hTo = height;
        _animDone = done;

        _animClock = Stopwatch.StartNew();
        _animating = true;
        _animTick = OnAnimTick;
        CompositionTarget.Rendering += _animTick;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (!_animating || _animClock == null) return;

        double p = Math.Min(1.0, _animClock.Elapsed.TotalMilliseconds / _animDuration.TotalMilliseconds);
        double k = EaseOutCubic(p);

        double l = _lFrom + (_lTo - _lFrom) * k;
        double t = _tFrom + (_tTo - _tFrom) * k;
        double w = Math.Max(1, _wFrom + (_wTo - _wFrom) * k);
        double h = Math.Max(1, _hFrom + (_hTo - _hFrom) * k);

        SetBoundsFast(l, t, w, h);

        if (p >= 1.0) StopAnimation(commit: true);
    }

    private static double EaseOutCubic(double p) => 1 - Math.Pow(1 - p, 3);

    private void StopAnimation(bool commit)
    {
        if (!_animating) return;
        _animating = false;
        if (_animTick != null) CompositionTarget.Rendering -= _animTick;
        _animTick = null;
        _animClock?.Stop();

        if (commit)
        {
            // Sync the WPF properties to the final values so subsequent reads agree
            // with the OS window state.
            Left = _lTo; Top = _tTo;
            Width = Math.Max(1, _wTo); Height = Math.Max(1, _hTo);
            _animDone?.Invoke();
        }
        _animDone = null;
    }

    // ---- Single atomic Win32 bounds push (DIP → physical pixel conversion) ----

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private void SetBoundsFast(double l, double t, double w, double h)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Left = l; Top = t; Width = Math.Max(1, w); Height = Math.Max(1, h);
            return;
        }

        var source = PresentationSource.FromVisual(this);
        Matrix m = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        double sx = m.M11 > 0 ? m.M11 : 1.0;
        double sy = m.M22 > 0 ? m.M22 : 1.0;

        SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(l * sx), (int)Math.Round(t * sy),
            (int)Math.Round(w * sx), (int)Math.Round(h * sy),
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    // ----------------------------------------------------------------- list ops

    /// <summary>Add a new task to the top of the list and persist.</summary>
    public void AddTask(TaskItem item)
    {
        _all.Insert(0, item);
        _data.SaveTasks(_all);
        RefreshVisible();
        UpdateBadge();

        if (_data.Config.FloatingVisible && !IsVisible)
            Show();

        // Add-feedback (req. 6): if tucked away, peek the list open then auto-collapse.
        if (_collapsed)
            StartPeek();
    }

    private void RefreshVisible()
    {
        _visible.Clear();
        int limit = _expanded ? _all.Count : Math.Min(MaxVisible, _all.Count);
        for (int i = 0; i < limit; i++)
            _visible.Add(_all[i]);

        int hidden = _all.Count - limit;
        if (!_expanded && hidden > 0)
        {
            BtnMore.Content = $"+{hidden} 更多";
            BtnMore.Visibility = Visibility.Visible;
        }
        else if (_expanded && _all.Count > MaxVisible)
        {
            BtnMore.Content = "收起";
            BtnMore.Visibility = Visibility.Visible;
        }
        else
        {
            BtnMore.Visibility = Visibility.Collapsed;
        }

        EmptyHint.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBadge()
    {
        BadgeText.Text = _all.Count > 99 ? "99+" : _all.Count.ToString();
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        RefreshVisible();
    }

    // ----------------------------------------------------------- peek feedback

    private void StartPeek()
    {
        // Remember which edge to tuck back to before ExitCollapsed clears it.
        _peekReturnEdge = _dockEdge != "None" ? _dockEdge : _data.Config.DockEdge;
        _peeking = true;
        ExitCollapsed(animate: true);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    private void PeekTimer_Tick(object? sender, EventArgs e)
    {
        _peekTimer.Stop();
        if (!_peeking) return;
        if (IsMouseOver) { _peekTimer.Start(); return; } // user still looking → wait
        _peeking = false;
        EnterCollapsed(_peekReturnEdge != "None" ? _peekReturnEdge : "Right", animate: true);
    }

    /// <summary>Stop an in-progress peek and commit to the expanded state.</summary>
    private void CancelPeek()
    {
        _peeking = false;
        _peekTimer.Stop();
    }

    private void ExpandedRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_peeking) _peekTimer.Stop();
    }

    private void ExpandedRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_peeking) { _peekTimer.Stop(); _peekTimer.Start(); }
    }

    // ------------------------------------------------------------ complete + fade

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent the press from starting a window drag

        if (sender is not Button btn || btn.DataContext is not TaskItem item)
            return;

        void Finish()
        {
            _all.Remove(item);
            _data.SaveTasks(_all);
            RefreshVisible();
            UpdateBadge();
        }

        var root = FindAncestor<Border>(btn, b => (b.Tag as string) == "ItemRoot");
        if (root == null)
        {
            Finish();
            return;
        }

        var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(0.3)));
        fade.Completed += (_, _) => Finish();
        root.BeginAnimation(OpacityProperty, fade);
    }

    private static T? FindAncestor<T>(DependencyObject start, Func<T, bool> match)
        where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current != null)
        {
            if (current is T t && match(t)) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
