// RunCat Neo for Windows — unofficial port of RunCatNeo (https://github.com/runcat-dev/RunCatNeo).
// Original Copyright 2026 Kyome22 (Takuto Nakamura), Apache License 2.0.
namespace RunCatNeo.Win;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--smoke"))
        {
            Smoke();
            return;
        }
        if (args.Contains("--preview"))
        {
            var outPath = args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault() ?? "preview.png";
            Preview(outPath);
            return;
        }
        if (args.Contains("--dash-preview"))
        {
            ApplicationConfiguration.Initialize();
            var outPath = args.SkipWhile(a => a != "--dash-preview").Skip(1).FirstOrDefault() ?? "dash.png";
            using var watcher = new MetricsWatcher();
            using var form = new DashboardForm(watcher, () => 12.3f);
            using var bmp = form.RenderToBitmap();
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"wrote {outPath} ({bmp.Width}x{bmp.Height})");
            return;
        }
        if (args.Contains("--flyout"))
        {
            ApplicationConfiguration.Initialize();
            var outPath = args.SkipWhile(a => a != "--flyout").Skip(1).FirstOrDefault() ?? "flyout.png";
            var watcher = new MetricsWatcher();

            // Register a real tray icon so we can resolve its true screen rect, just like the app does.
            using var ni = new NotifyIcon { Visible = true, Icon = SystemIcons.Application, Text = "flyout preview" };
            var settle = Environment.TickCount64 + 800;
            while (Environment.TickCount64 < settle) Application.DoEvents();
            var anchor = Native.GetTrayIconRect(ni);
            var synthetic = anchor.IsEmpty;
            if (synthetic)
            {
                var scr = Screen.PrimaryScreen!.Bounds;
                var wa = Screen.PrimaryScreen!.WorkingArea;
                anchor = new Rectangle(scr.Right - 160, wa.Bottom, 24, scr.Bottom - wa.Bottom); // fake icon on taskbar
            }

            var form = new DashboardForm(watcher, () => 12.3f);
            form.ShowNearTray(anchor);
            var deadline = Environment.TickCount64 + 500; // let the slide/fade settle
            while (Environment.TickCount64 < deadline) Application.DoEvents();

            // Capture from the flyout down to the screen bottom so the icon↔flyout alignment is visible.
            var screenBottom = Screen.FromRectangle(form.Bounds).Bounds.Bottom;
            var cx = anchor.Left + anchor.Width / 2;
            var region = Rectangle.FromLTRB(
                Math.Min(form.Left, cx - 30) - 24, form.Top - 24,
                Math.Max(form.Right, cx + 30) + 24, screenBottom);
            using var shot = new Bitmap(region.Width, region.Height);
            using (var g = Graphics.FromImage(shot))
            {
                g.CopyFromScreen(region.Location, Point.Empty, region.Size);
                // Mark the anchor (icon) center so we can eyeball alignment.
                using var pen = new Pen(Color.Lime, 2);
                var mx = cx - region.Left;
                g.DrawLine(pen, mx, region.Height - 40, mx, region.Height - 1);
            }
            shot.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"wrote {outPath}; anchor={anchor} synthetic={synthetic} flyout={form.Bounds}");
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, "RunCatNeo.Win.SingleInstance", out var isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }

    /// <summary>Headless self-test: render every built-in runner and report frame counts.</summary>
    private static void Smoke()
    {
        var failed = false;
        foreach (var runner in Runner.BuiltIns)
        {
            var frames = IconRenderer.RenderSequence(runner, Color.White, flipped: false);
            var ok = frames.Length == runner.FrameOrder.Length && frames.All(f => f.Width >= 16);
            Console.WriteLine($"{runner.Id,-14} frames={frames.Length,2}/{runner.FrameOrder.Length,2} size={frames.FirstOrDefault()?.Width}x{frames.FirstOrDefault()?.Height} {(ok ? "OK" : "FAIL")}");
            if (!ok) failed = true;
            foreach (var icon in frames.Distinct()) icon.Dispose();
        }
        var monitor = new CpuMonitor();
        monitor.Sample();
        Thread.Sleep(500);
        var cpu = monitor.Sample();
        Console.WriteLine($"cpu sample = {cpu:0.0}%  speed(normal) = {CpuMonitor.SpeedFor(cpu, false):0.0}  speed(slower) = {CpuMonitor.SpeedFor(cpu, true):0.0}");
        Environment.Exit(failed ? 1 : 0);
    }

    /// <summary>
    /// Renders a montage simulating the tray at 16px: each runner in Fit vs FillHeight, on a dark
    /// taskbar with white tint, upscaled 8x (nearest) so the true pixel appearance is visible.
    /// </summary>
    private static void Preview(string outPath)
    {
        string[] ids = ["cat", "dog", "coffee", "newton-cradle", "mochi", "engine", "slime", "drop"];
        FillMode[] modes = [FillMode.Fit, FillMode.Balanced, FillMode.FillHeight];
        const int tray = 16, zoom = 8, cell = tray * zoom, pad = 8, header = 20;

        using var montage = new Bitmap(pad + modes.Length * (cell + pad), header + ids.Length * (cell + pad));
        using (var g = Graphics.FromImage(montage))
        {
            g.Clear(Color.FromArgb(32, 32, 32)); // simulated dark taskbar
            using var font = new Font("Segoe UI", 8);
            for (var c = 0; c < modes.Length; c++)
                g.DrawString(modes[c].ToString(), font, Brushes.White, pad + c * (cell + pad), 2);

            for (var r = 0; r < ids.Length; r++)
            {
                var runner = Runner.BuiltIns.First(x => x.Id == ids[r]);
                var bitmaps = runner.LoadFrameBitmaps();
                var content = new Rectangle(0, 0, bitmaps.Values.Max(b => b.Width), bitmaps.Values.Max(b => b.Height));
                for (var c = 0; c < modes.Length; c++)
                {
                    // Render at 32 then downscale to 16 to mimic what Windows shows in the tray.
                    using var big = IconRenderer.RenderFrameBitmap(bitmaps[runner.FrameOrder[0]], content, Color.White, false, modes[c], 32);
                    using var small = new Bitmap(tray, tray);
                    using (var sg = Graphics.FromImage(small))
                    {
                        sg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        sg.DrawImage(big, 0, 0, tray, tray);
                    }
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    var dx = pad + c * (cell + pad);
                    var dy = header + r * (cell + pad);
                    g.DrawImage(small, dx, dy, cell, cell);
                    using var pen = new Pen(Color.FromArgb(80, 80, 80));
                    g.DrawRectangle(pen, dx, dy, cell, cell);
                }
                foreach (var b in bitmaps.Values) b.Dispose();
            }
        }
        montage.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"wrote {outPath}");
    }
}
