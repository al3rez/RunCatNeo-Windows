// DashboardForm.cs — a Windows-11-style tray flyout listing custom-metrics cards (CPU + watched JSON sources).
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RunCatNeo.Win;

public sealed class DashboardForm : Form
{
    private const int Gap = 12;            // gap between the flyout and the taskbar/screen edge
    private const int AnimSlide = 12;      // px the flyout slides in from the taskbar
    private const int CsDropShadow = 0x00020000;

    private readonly MetricsWatcher _watcher;
    private readonly Func<float> _cpuProvider;
    private readonly FlowLayoutPanel _stack;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private bool _dark = true;
    private Point _targetLocation;
    private TaskbarEdge _edge = TaskbarEdge.Bottom;
    private long _animStartTick;

    /// <summary>TickCount64 of the last hide; used to debounce tray-icon re-clicks that would reopen it.</summary>
    public long LastHiddenTick { get; private set; }

    public DashboardForm(MetricsWatcher watcher, Func<float> cpuProvider)
    {
        _watcher = watcher;
        _cpuProvider = cpuProvider;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(300, 0);
        Padding = new Padding(12);
        AutoScaleMode = AutoScaleMode.Dpi;

        BackColor = Color.Black; // pure black reads as the "glass" region under the DWM system backdrop
        _stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            AutoSize = false,
            BackColor = Color.Transparent, // let the acrylic show between/behind cards
        };
        Controls.Add(_stack);

        _watcher.Changed += OnSourcesChanged;
        _clock.Tick += (_, _) => RefreshRelativeTimes();
        _anim.Tick += (_, _) => StepAnimation();
        Deactivate += (_, _) => Hide();
    }

    // Give the borderless flyout a native drop shadow (like the volume/network flyouts).
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyRoundedCorners(Handle);
        Native.ApplyAcrylicBackdrop(Handle, _dark);
    }

    /// <summary>Builds and renders the popover to a bitmap by briefly showing it off-screen (for preview/verification).</summary>
    public Bitmap RenderToBitmap()
    {
        ApplyTheme();
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        Width = 320;
        Rebuild();
        Height = Math.Max(140, _stack.PreferredSize.Height + Padding.Vertical + 4);
        Show();
        for (var i = 0; i < 5; i++) Application.DoEvents();
        var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        Hide();
        return bmp;
    }

    /// <param name="anchor">Screen rect of the tray icon the flyout should point at (empty → corner fallback).</param>
    public void ShowNearTray(Rectangle anchor = default)
    {
        ApplyTheme();
        Rebuild();

        var (bounds, edge) = Native.GetTaskbarInfo();
        _edge = edge;
        var wa = Screen.FromRectangle(bounds).WorkingArea;
        Width = 320;
        Height = Math.Min(wa.Height - 2 * Gap, Math.Max(140, _stack.PreferredSize.Height + Padding.Vertical + 4));

        _targetLocation = AnchoredLocation(anchor, wa, edge);
        Location = SlideStart();
        Show();
        Activate();
        _clock.Start();
        _animStartTick = Environment.TickCount64;
        _anim.Start();
    }

    // Places the flyout against the taskbar edge, centered on the icon along the taskbar's length
    // (like the ChatGPT / iCUE tray popups), clamped to stay fully on-screen.
    private Point AnchoredLocation(Rectangle anchor, Rectangle wa, TaskbarEdge edge)
    {
        // Corner fallback when we couldn't locate the icon.
        if (anchor.IsEmpty)
        {
            return edge switch
            {
                TaskbarEdge.Left => new Point(wa.Left + Gap, wa.Bottom - Height - Gap),
                TaskbarEdge.Top => new Point(wa.Right - Width - Gap, wa.Top + Gap),
                _ => new Point(wa.Right - Width - Gap, wa.Bottom - Height - Gap),
            };
        }

        var cx = anchor.Left + anchor.Width / 2;
        var cy = anchor.Top + anchor.Height / 2;
        return edge switch
        {
            TaskbarEdge.Left => new Point(wa.Left + Gap, Clamp(cy - Height / 2, wa.Top, wa.Bottom - Height)),
            TaskbarEdge.Right => new Point(wa.Right - Width - Gap, Clamp(cy - Height / 2, wa.Top, wa.Bottom - Height)),
            TaskbarEdge.Top => new Point(Clamp(cx - Width / 2, wa.Left, wa.Right - Width), wa.Top + Gap),
            _ => new Point(Clamp(cx - Width / 2, wa.Left, wa.Right - Width), wa.Bottom - Height - Gap),
        };
    }

    private static int Clamp(int v, int lo, int hi) => hi < lo ? lo : Math.Max(lo, Math.Min(v, hi));

    // Offset the start position so the flyout slides in *from* the taskbar edge.
    private Point SlideStart() => _edge switch
    {
        TaskbarEdge.Left => _targetLocation with { X = _targetLocation.X - AnimSlide },
        TaskbarEdge.Top => _targetLocation with { Y = _targetLocation.Y - AnimSlide },
        TaskbarEdge.Right => _targetLocation with { X = _targetLocation.X + AnimSlide },
        _ => _targetLocation with { Y = _targetLocation.Y + AnimSlide },
    };

    private void StepAnimation()
    {
        const float durationMs = 130f;
        var t = Math.Clamp((Environment.TickCount64 - _animStartTick) / durationMs, 0f, 1f);
        var eased = 1f - (float)Math.Pow(1 - t, 3); // ease-out cubic
        // Slide only (no opacity fade): an Opacity < 1 makes this a layered window, which suppresses
        // the DWM acrylic backdrop. Slide-in keeps the window non-layered so the glass renders.
        var start = SlideStart();
        Location = new Point(
            (int)(start.X + (_targetLocation.X - start.X) * eased),
            (int)(start.Y + (_targetLocation.Y - start.Y) * eased));
        if (t >= 1f)
        {
            _anim.Stop();
            Location = _targetLocation;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide instead of destroy so it can be reopened cheaply.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _clock.Stop();
            _anim.Stop();
            LastHiddenTick = Environment.TickCount64;
        }
    }

    private void OnSourcesChanged()
    {
        if (Visible) Rebuild();
    }

    private void ApplyTheme()
    {
        _dark = !IconRenderer.IsSystemLightTheme();
        BackColor = Color.Black; // glass key — the acrylic tint (dark/light) is set on the backdrop itself
        if (IsHandleCreated) Native.ApplyAcrylicBackdrop(Handle, _dark);
    }

    private void Rebuild()
    {
        _stack.SuspendLayout();
        foreach (Control c in _stack.Controls) c.Dispose();
        _stack.Controls.Clear();

        // Built-in CPU card, always first.
        var cpu = _cpuProvider();
        _stack.Controls.Add(new MetricCard(_dark, _stack.ClientSize.Width, new CustomMetricsSnapshot
        {
            Title = "CPU",
            MetricsBarValue = $"{cpu:0.0}%",
            Metrics = [new CustomMetric { Title = "Usage", FormattedValue = $"{cpu:0.0}%", NormalizedValue = cpu / 100.0 }],
            LastUpdatedDate = DateTimeOffset.Now,
        }, failed: false));

        foreach (var source in _watcher.Sources.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (source.Snapshot is null && !source.Failed) continue;
            _stack.Controls.Add(new MetricCard(_dark, _stack.ClientSize.Width,
                source.Snapshot ?? new CustomMetricsSnapshot { Title = source.DisplayName }, source.Failed));
        }

        if (_watcher.Sources.Count == 0)
        {
            _stack.Controls.Add(new HintLabel(_dark, _stack.ClientSize.Width));
        }
        _stack.ResumeLayout();
    }

    private void RefreshRelativeTimes()
    {
        foreach (Control c in _stack.Controls)
            if (c is MetricCard card) card.RefreshTime();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Changed -= OnSourcesChanged;
            _clock.Dispose();
            _anim.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal enum TaskbarEdge { Left, Top, Right, Bottom }

/// <summary>Win32 interop for tray-flyout look &amp; placement.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TABBEDWINDOW = 4; // "Mica Alt" — darker, flatter, more solid native glass

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Turns the borderless flyout into a native Windows 11 acrylic (glass) surface: extends the DWM
    /// frame across the whole client so the black background composites as glass, then selects the
    /// acrylic system backdrop and the dark/light tint. No-op on Windows 10 (attributes unsupported).
    /// </summary>
    public static void ApplyAcrylicBackdrop(IntPtr hwnd, bool dark)
    {
        try
        {
            var darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            var sheet = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref sheet);

            var backdrop = int.TryParse(Environment.GetEnvironmentVariable("RUNCAT_BACKDROP"), out var b)
                ? b : DWMSBT_TABBEDWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch (DllNotFoundException) { /* pre-Win10 dwmapi */ }
    }

    /// <summary>
    /// Brings the NotifyIcon's hidden host window to the foreground so a manually-shown context menu
    /// dismisses on click-away (a tray app otherwise has no foreground window to lose focus from).
    /// </summary>
    public static void FocusTrayWindow(NotifyIcon icon)
    {
        var handle = GetNotifyIconWindowHandle(icon);
        if (handle != IntPtr.Zero) SetForegroundWindow(handle);
    }

    private static IntPtr GetNotifyIconWindowHandle(NotifyIcon icon)
    {
        try
        {
            var t = typeof(NotifyIcon);
            var windowField = t.GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            return windowField?.GetValue(icon) is NativeWindow w ? w.Handle : IntPtr.Zero;
        }
        catch (Exception e) when (e is TargetException or FieldAccessException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Screen rectangle of a WinForms NotifyIcon's tray button, via Shell_NotifyIconGetRect. Reaches
    /// the icon's hidden host window + id by reflection (WinForms doesn't expose them). Empty on failure
    /// or when the icon is tucked in the overflow flyout.
    /// </summary>
    public static Rectangle GetTrayIconRect(NotifyIcon icon)
    {
        try
        {
            var t = typeof(NotifyIcon);
            var windowField = t.GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            var idField = t.GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? t.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            if (windowField?.GetValue(icon) is not NativeWindow window || idField?.GetValue(icon) is not int id)
                return Rectangle.Empty;
            if (window.Handle == IntPtr.Zero) return Rectangle.Empty;

            var ident = new NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = window.Handle,
                uID = (uint)id,
            };
            if (Shell_NotifyIconGetRect(ref ident, out var r) == 0) // S_OK
            {
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width > 0 && rect.Height > 0) return rect;
            }
        }
        catch (Exception e) when (e is TargetException or FieldAccessException or DllNotFoundException) { }
        return Rectangle.Empty;
    }

    /// <summary>Opts the borderless window into Windows 11 rounded corners (no-op before Win11).</summary>
    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        var pref = DWMWCP_ROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch (DllNotFoundException) { /* older Windows: dwmapi missing the attribute */ }
    }

    /// <summary>Returns the taskbar's screen rectangle and which edge it's docked to.</summary>
    public static (Rectangle bounds, TaskbarEdge edge) GetTaskbarInfo()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != IntPtr.Zero)
        {
            var r = data.rc;
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            var edge = data.uEdge switch
            {
                0 => TaskbarEdge.Left,
                1 => TaskbarEdge.Top,
                2 => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };
            return (bounds, edge);
        }
        // Fallback: assume a bottom taskbar on the primary screen.
        var scr = Screen.PrimaryScreen!.Bounds;
        return (new Rectangle(scr.Left, scr.Bottom - 48, scr.Width, 48), TaskbarEdge.Bottom);
    }
}

/// <summary>A single owner-drawn metrics card.</summary>
internal sealed class MetricCard : Panel
{
    private readonly bool _dark;
    private readonly CustomMetricsSnapshot _snapshot;
    private readonly bool _failed;

    private Color Fg => _dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(20, 20, 20);
    private Color Muted => _dark ? Color.FromArgb(165, 165, 165) : Color.FromArgb(100, 100, 100);
    // Faint hairline that separates a card from the acrylic without becoming an opaque box.
    private Color CardBorder => _dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0);
    private Color BarBg => _dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0);
    private static readonly Color Accent = Color.FromArgb(255, 122, 60); // RunCat orange

    // Shared fonts (app-lifetime) so measurement and painting stay in lockstep.
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font RowFont = new("Segoe UI", 9.5f);
    private static readonly Font FooterFont = new("Segoe UI", 8f);

    // Layout constants (logical px, DPI-scaled by the form's AutoScale).
    private const int TitleGap = 8, BarGap = 5, BarH = 6, RowGap = 11, FooterH = 18;

    private static int MeasureHeight(CustomMetricsSnapshot s)
    {
        var h = 10 + TitleFont.Height + TitleGap;
        foreach (var m in s.Metrics)
            h += RowFont.Height + (m.NormalizedValue is not null ? BarGap + BarH : 0) + RowGap;
        return h + FooterH + 8;
    }

    public MetricCard(bool dark, int width, CustomMetricsSnapshot snapshot, bool failed)
    {
        _dark = dark;
        _snapshot = snapshot;
        _failed = failed;

        Width = Math.Max(260, width - 6);
        Margin = new Padding(3, 3, 3, 6);
        Padding = new Padding(14, 10, 14, 10);
        DoubleBuffered = true;
        BackColor = Color.Black; // glass: black composites as the acrylic backdrop, like the rest of the flyout

        // Height is measured precisely in OnPaint-consistent units below.
        Height = MeasureHeight(snapshot);
    }

    public void RefreshTime() => Invalidate(); // footer time is drawn in OnPaint; just repaint

    private string FooterText()
    {
        if (_failed) return "Last updated: Failed";
        if (_snapshot.LastUpdatedDate is not { } dt) return "";
        return $"Last updated: {Relative(dt)}";
    }

    private static string Relative(DateTimeOffset then)
    {
        var s = (DateTimeOffset.Now - then).TotalSeconds;
        if (s < 5) return "just now";
        if (s < 60) return $"{(int)s} sec ago";
        if (s < 3600) return $"{(int)(s / 60)} min ago";
        if (s < 86400) return $"{(int)(s / 3600)} h ago";
        return $"{(int)(s / 86400)} d ago";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // AntiAlias (not ClearType) blends to alpha cleanly on the glass surface — avoids black text halos.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // No opaque fill — the card interior stays glass. Just a faint rounded outline to group it.
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Rounded(r, 8))
        using (var border = new Pen(CardBorder))
            g.DrawPath(border, path);

        var x = Padding.Left;
        var y = Padding.Top;
        using var fg = new SolidBrush(Fg);
        using var muted = new SolidBrush(Muted);

        var title = string.IsNullOrEmpty(_snapshot.Symbol) ? _snapshot.Title : $"{Glyph(_snapshot.Symbol)}  {_snapshot.Title}";
        g.DrawString(title, TitleFont, fg, x, y);
        y += TitleFont.Height + TitleGap;

        var innerWidth = Width - Padding.Horizontal;
        foreach (var m in _snapshot.Metrics)
        {
            g.DrawString(m.Title, RowFont, muted, x, y);
            var valSize = g.MeasureString(m.FormattedValue, RowFont);
            g.DrawString(m.FormattedValue, RowFont, fg, x + innerWidth - valSize.Width, y);
            y += RowFont.Height;

            if (m.NormalizedValue is { } nv)
            {
                y += BarGap;
                var frac = (float)Math.Clamp(nv, 0.0, 1.0);
                var barRect = new Rectangle(x, y, innerWidth, BarH);
                using (var track = new SolidBrush(BarBg))
                using (var tp = Rounded(barRect, 3))
                    g.FillPath(track, tp);
                if (frac > 0)
                {
                    var fillRect = new Rectangle(x, y, Math.Max(BarH, (int)(innerWidth * frac)), BarH);
                    using var fill = new SolidBrush(Accent);
                    using var fp = Rounded(fillRect, 3);
                    g.FillPath(fill, fp);
                }
                y += BarH;
            }
            y += RowGap;
        }

        var footer = FooterText();
        if (footer.Length > 0)
        {
            using var footerBrush = new SolidBrush(_failed ? Color.FromArgb(235, 100, 100) : Muted);
            g.DrawString(footer, FooterFont, footerBrush, x, Height - Padding.Bottom - FooterFont.Height + 2);
        }
        base.OnPaint(e);
    }

    // Minimal glyph mapping for the SF-Symbol-style names the samples use; falls back to a bullet.
    private static string Glyph(string symbol) => symbol switch
    {
        "staroflife" or "sparkles" => "✳",
        "bolt" or "bolt.fill" => "⚡",
        "bitcoinsign.circle" => "₿",
        "thermometer" => "🌡",
        _ => "•",
    };

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Shown when no custom sources are configured yet.</summary>
internal sealed class HintLabel : Label
{
    public HintLabel(bool dark, int width)
    {
        AutoSize = false;
        Width = Math.Max(260, width - 6);
        Height = 70;
        Margin = new Padding(3);
        Padding = new Padding(10);
        Font = new Font("Segoe UI", 9f);
        ForeColor = dark ? Color.FromArgb(165, 165, 165) : Color.FromArgb(100, 100, 100);
        BackColor = Color.Transparent; // show the acrylic behind the hint text
        TextAlign = ContentAlignment.MiddleLeft;
        Text = "No custom metrics yet.\nDrop a JSON file into the Metrics folder\n(tray menu → Custom metrics → Open folder).";
    }
}
