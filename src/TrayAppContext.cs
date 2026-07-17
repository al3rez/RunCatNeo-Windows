// TrayAppContext.cs — the tray application: animation loop, CPU sampling, context menu.
using Microsoft.Win32;

namespace RunCatNeo.Win;

public sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "RunCatNeo";

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly System.Windows.Forms.Timer _cpuTimer = new();
    private readonly CpuMonitor _cpuMonitor = new();
    private readonly Settings _settings = Settings.Load();

    private Runner _runner;
    private Icon[] _frames = [];
    private int _frameIndex;
    private float _cpuPercent;
    private float _speed = 1f;
    private Color _currentTint;
    private readonly ToolStripMenuItem _cpuHeaderItem;
    private readonly ToolStripMenuItem _runnerMenu;
    private readonly ContextMenuStrip _menu;
    private readonly MetricsWatcher _metricsWatcher = new();
    private DashboardForm? _dashboard;

    public TrayAppContext()
    {
        _runner = RunnerRepository.Resolve(_settings.RunnerId);

        _cpuHeaderItem = new ToolStripMenuItem("CPU: —") { Enabled = false };
        _runnerMenu = new ToolStripMenuItem("Runner");

        // Note: ContextMenuStrip is deliberately NOT assigned to the NotifyIcon — that would hard-wire the
        // menu to right-click. We show the menu on *left*-click and the dashboard on *right*-click instead.
        _menu = BuildMenu();
        _menu.Opening += (_, _) => RefreshMenuState();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "RunCat Neo",
        };
        // Windows convention (and macOS parity): left-click opens the main UI, right-click opens the menu.
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleDashboard();
            else if (e.Button == MouseButtons.Right) ShowTrayMenu();
        };

        RebuildFrames();

        _animationTimer.Tick += (_, _) => AdvanceFrame();
        _cpuTimer.Tick += (_, _) => SampleCpu();
        _cpuTimer.Interval = _settings.UpdateIntervalSeconds * 1000;
        _cpuTimer.Start();

        _cpuMonitor.Sample(); // prime the counters so the first real sample has a delta
        ApplySpeed();
    }

    // ----- animation -----

    private void AdvanceFrame()
    {
        if (_frames.Length == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        _notifyIcon.Icon = _frames[_frameIndex];
    }

    private void SampleCpu()
    {
        _cpuPercent = _cpuMonitor.Sample();
        _notifyIcon.Text = $"CPU: {_cpuPercent:0.0}%  —  {_runner.Name}";
        ApplySpeed();

        // Follow taskbar theme changes (polled with the CPU sample; cheap registry read).
        if (_settings.IconTheme == IconTheme.Auto && _runner.IsTemplate
            && IconRenderer.TintFor(IconTheme.Auto) != _currentTint)
        {
            RebuildFrames();
        }
    }

    private void ApplySpeed()
    {
        _speed = CpuMonitor.SpeedFor(_cpuPercent, _settings.SlowerUnderLoad);
        // Base animation runs at 2 fps (500 ms/frame), scaled by speed — same as the CALayer port.
        _animationTimer.Interval = Math.Max(25, (int)(500f / _speed));
        if (!_animationTimer.Enabled) _animationTimer.Start();
    }

    private void RebuildFrames()
    {
        _currentTint = IconRenderer.TintFor(_settings.IconTheme);
        var newFrames = IconRenderer.RenderSequence(_runner, _currentTint, _settings.FlippedHorizontally);
        var oldFrames = _frames;
        _frames = newFrames;
        _frameIndex = 0;
        if (_frames.Length > 0) _notifyIcon.Icon = _frames[0];
        foreach (var icon in oldFrames.Distinct()) icon.Dispose();
    }

    // ----- menu -----

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_cpuHeaderItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_runnerMenu);

        var intervalMenu = new ToolStripMenuItem("Update interval");
        foreach (var seconds in Settings.AllowedIntervals)
        {
            intervalMenu.DropDownItems.Add(new ToolStripMenuItem($"{seconds} seconds", null, (_, _) =>
            {
                _settings.UpdateIntervalSeconds = seconds;
                _cpuTimer.Interval = seconds * 1000;
                _settings.Save();
            })
            { Tag = seconds });
        }
        menu.Items.Add(intervalMenu);

        var themeMenu = new ToolStripMenuItem("Icon color");
        foreach (var (label, theme) in new[] { ("Auto (match taskbar)", IconTheme.Auto), ("Black", IconTheme.Light), ("White", IconTheme.Dark) })
        {
            themeMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) =>
            {
                _settings.IconTheme = theme;
                _settings.Save();
                RebuildFrames();
            })
            { Tag = theme });
        }
        menu.Items.Add(themeMenu);

        menu.Items.Add(new ToolStripMenuItem("Slower under load", null, (_, _) =>
        {
            _settings.SlowerUnderLoad = !_settings.SlowerUnderLoad;
            _settings.Save();
            ApplySpeed();
        })
        { Name = "slower" });

        menu.Items.Add(new ToolStripMenuItem("Flip horizontally", null, (_, _) =>
        {
            _settings.FlippedHorizontally = !_settings.FlippedHorizontally;
            _settings.Save();
            RebuildFrames();
        })
        { Name = "flip" });

        menu.Items.Add(new ToolStripMenuItem("Launch at login", null, (_, _) => ToggleLaunchAtLogin()) { Name = "login" });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Show dashboard", null, (_, _) => ShowDashboard()));
        var metricsMenu = new ToolStripMenuItem("Custom metrics");
        metricsMenu.DropDownItems.Add(new ToolStripMenuItem("Open metrics folder", null, (_, _) =>
        {
            Directory.CreateDirectory(MetricsWatcher.MetricsDir);
            System.Diagnostics.Process.Start("explorer.exe", MetricsWatcher.MetricsDir);
        }));
        metricsMenu.DropDownItems.Add(new ToolStripMenuItem("Add JSON source…", null, (_, _) => AddMetricsSource()));
        menu.Items.Add(metricsMenu);

        menu.Items.Add(new ToolStripMenuItem("Open custom runners folder", null, (_, _) =>
        {
            Directory.CreateDirectory(RunnerRepository.CustomRunnersRoot);
            System.Diagnostics.Process.Start("explorer.exe", RunnerRepository.CustomRunnersRoot);
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
        return menu;
    }

    private void RefreshMenuState()
    {
        _cpuHeaderItem.Text = $"CPU: {_cpuPercent:0.0}%";

        // Rescan custom runners each time the menu opens so new folders show up live.
        _runnerMenu.DropDownItems.Clear();
        foreach (var runner in Runner.BuiltIns.Concat(RunnerRepository.LoadCustomRunners()))
        {
            var item = new ToolStripMenuItem(runner.Name, null, (_, _) => SelectRunner(runner))
            {
                Checked = runner.Id == _runner.Id,
            };
            _runnerMenu.DropDownItems.Add(item);
        }

        foreach (ToolStripItem item in _menu.Items)
        {
            switch (item)
            {
                case ToolStripMenuItem { Name: "slower" } m: m.Checked = _settings.SlowerUnderLoad; break;
                case ToolStripMenuItem { Name: "flip" } m: m.Checked = _settings.FlippedHorizontally; break;
                case ToolStripMenuItem { Name: "login" } m: m.Checked = IsLaunchAtLoginEnabled(); break;
                case ToolStripMenuItem { Tag: int seconds } m: m.Checked = seconds == _settings.UpdateIntervalSeconds; break;
                case ToolStripMenuItem { Tag: IconTheme theme } m: m.Checked = theme == _settings.IconTheme; break;
            }
        }
    }

    private void SelectRunner(Runner runner)
    {
        _runner = runner;
        _settings.RunnerId = runner.Id;
        _settings.Save();
        RebuildFrames();
        _notifyIcon.Text = $"CPU: {_cpuPercent:0.0}%  —  {_runner.Name}";
    }

    // ----- dashboard -----

    private DashboardForm Dashboard => _dashboard ??= new DashboardForm(_metricsWatcher, () => _cpuPercent);

    private void ShowDashboard() => Dashboard.ShowNearTray(TrayAnchor());

    /// <summary>
    /// Shows the context menu at the tray on left-click. SetForegroundWindow on the NotifyIcon's own
    /// hidden window first, so the menu closes on click-away (the standard tray-menu workaround).
    /// </summary>
    private void ShowTrayMenu()
    {
        if (_dashboard is { Visible: true }) _dashboard.Hide();
        Native.FocusTrayWindow(_notifyIcon);
        _menu.Show(Cursor.Position);
    }

    /// <summary>Screen rect to point the flyout at: the tray icon if we can locate it, else a point at the cursor.</summary>
    private Rectangle TrayAnchor()
    {
        var iconRect = Native.GetTrayIconRect(_notifyIcon);
        if (!iconRect.IsEmpty) return iconRect;
        var p = Cursor.Position; // e.g. icon is in the overflow flyout — anchor to where the user clicked
        return new Rectangle(p.X, p.Y, 1, 1);
    }

    private void ToggleDashboard()
    {
        // Clicking the tray icon while the flyout is open first fires the form's Deactivate (which hides it),
        // so by the time we get here it's already hidden. Treat a click right after a hide as "close", not "reopen".
        if (_dashboard is { Visible: true })
        {
            _dashboard.Hide();
            return;
        }
        if (_dashboard is not null && Environment.TickCount64 - _dashboard.LastHiddenTick < 250) return;
        ShowDashboard();
    }

    private void AddMetricsSource()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a custom metrics JSON file",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _metricsWatcher.AddExternalSource(dialog.FileName);
            ShowDashboard();
        }
    }

    // ----- launch at login -----

    private static bool IsLaunchAtLoginEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void ToggleLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue(RunValueName) is null)
        {
            key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    // ----- lifecycle -----

    private void ExitApp()
    {
        _animationTimer.Stop();
        _cpuTimer.Stop();
        _dashboard?.Dispose();
        _metricsWatcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _frames.Distinct()) icon.Dispose();
        ExitThread();
    }
}
