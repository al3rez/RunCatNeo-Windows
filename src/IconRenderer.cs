// IconRenderer.cs — turns runner frame bitmaps into tray icons (tinted, flipped, DPI-sized).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RunCatNeo.Win;

public enum FillMode
{
    /// <summary>Aspect-fit the whole sprite inside the square (may letterbox — wide sprites look short).</summary>
    Fit,
    /// <summary>Scale to fill the square's height and center horizontally; clips very wide sprites at the sides.</summary>
    FillHeight,
    /// <summary>Fill height, but cap horizontal overflow so wide sprites (dog, Newton's cradle) aren't over-clipped.</summary>
    Balanced,
}

public static class IconRenderer
{
    // Base resolution the icon is rendered at. Windows downscales this to the notification-area size,
    // so rendering well above 16 keeps the mascot crisp on Win11 trays (which render larger than 16px).
    private const int RenderSize = 32;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>True when the Windows *system* theme (taskbar) is light.</summary>
    public static bool IsSystemLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    public static Color TintFor(IconTheme theme) => theme switch
    {
        IconTheme.Light => Color.Black,          // light taskbar → dark glyph
        IconTheme.Dark => Color.White,           // dark taskbar → light glyph
        _ => IsSystemLightTheme() ? Color.Black : Color.White,
    };

    /// <summary>
    /// Renders the animation sequence as tray icons. Template runners are tinted to a flat
    /// color preserving alpha (macOS template-image behavior); color runners are drawn as-is.
    /// </summary>
    public static Icon[] RenderSequence(Runner runner, Color tint, bool flipped, FillMode fillMode = FillMode.Balanced)
    {
        var bitmaps = runner.LoadFrameBitmaps();
        try
        {
            if (bitmaps.Count == 0) return [];
            // Common content box across all frames so the mascot keeps a stable size/baseline as it animates.
            var content = UnionContentBounds(bitmaps.Values);
            var icons = new Dictionary<int, Icon>();
            foreach (var (n, bmp) in bitmaps)
            {
                icons[n] = RenderFrameIcon(bmp, content, runner.IsTemplate ? tint : null, flipped, fillMode);
            }
            var fallback = icons.Values.First();
            return runner.FrameOrder.Select(n => icons.GetValueOrDefault(n, fallback)).ToArray();
        }
        finally
        {
            foreach (var bmp in bitmaps.Values) bmp.Dispose();
        }
    }

    /// <summary>Union of the non-transparent pixel bounds across every frame (in source pixels).</summary>
    private static Rectangle UnionContentBounds(IEnumerable<Bitmap> frames)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        foreach (var bmp in frames)
        {
            var b = ContentBounds(bmp);
            if (b.IsEmpty) continue;
            minX = Math.Min(minX, b.Left);
            minY = Math.Min(minY, b.Top);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }
        if (maxX < 0) return new Rectangle(0, 0, 1, 1);
        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static Rectangle ContentBounds(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            unsafe
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        if (row[x * 4 + 3] == 0) continue; // alpha
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Icon RenderFrameIcon(Bitmap src, Rectangle content, Color? tint, bool flipped, FillMode fillMode)
    {
        using var canvas = RenderFrameBitmap(src, content, tint, flipped, fillMode, RenderSize);
        var hIcon = canvas.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>Renders a single frame to a square ARGB bitmap. Exposed for preview tooling.</summary>
    public static Bitmap RenderFrameBitmap(Bitmap src, Rectangle content, Color? tint, bool flipped, FillMode fillMode, int size)
    {
        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Balanced mode lets a sprite's content overflow the square by at most this factor per side
        // before it gets scaled down, so wide runners (dog, Newton's cradle) keep their extremities.
        const float maxOverflow = 1.30f;
        var fitScale = Math.Min((float)size / content.Width, (float)size / content.Height);
        var fillHeightScale = (float)size / content.Height;
        var scale = fillMode switch
        {
            FillMode.Fit => fitScale,
            FillMode.FillHeight => fillHeightScale,
            _ => Math.Min(fillHeightScale, maxOverflow * size / content.Width),
        };

        var w = content.Width * scale;
        var h = content.Height * scale;
        // Nudge the mascot slightly left in the tray slot. Flip mirrors the coordinate space,
        // so add the offset (instead of subtracting) when flipped to keep the shift visually leftward.
        var shift = size * 0.15f;
        var ox = flipped ? shift : -shift;
        var destRect = new RectangleF((size - w) / 2f + ox, (size - h) / 2f, w, h);

        if (flipped)
        {
            g.TranslateTransform(size, 0);
            g.ScaleTransform(-1, 1);
        }

        if (tint is { } color)
        {
            // Replace RGB with the tint color, keep the source alpha channel (template-image behavior).
            var matrix = new ColorMatrix(new float[][]
            {
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0],
                [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1],
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(matrix);
            g.DrawImage(src, PointsFor(destRect), content, GraphicsUnit.Pixel, attrs);
        }
        else
        {
            g.DrawImage(src, PointsFor(destRect), content, GraphicsUnit.Pixel);
        }
        return canvas;
    }

    // Parallelogram (upper-left, upper-right, lower-left) destination for DrawImage's source-rect overload.
    private static PointF[] PointsFor(RectangleF r) =>
    [
        new(r.Left, r.Top),
        new(r.Right, r.Top),
        new(r.Left, r.Bottom),
    ];
}
