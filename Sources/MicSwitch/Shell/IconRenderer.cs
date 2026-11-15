using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MicSwitch.Shell;

internal enum MicVisual
{
    Live,
    Muted,
    Disconnected
}

/// <summary>Draws the tray and overlay icons from the Windows icon font, or from the user's own images.</summary>
internal static class IconRenderer
{
    private const string MicGlyph = "";
    private const string MicOffGlyph = "";

    // Windows 11 ships Segoe Fluent Icons; Windows 10 has the same glyphs in Segoe MDL2 Assets.
    private static readonly Typeface GlyphFont = new(
        new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Color CriticalOnDark = Color.FromRgb(0xFF, 0x99, 0xA4);
    private static readonly Color CriticalOnLight = Color.FromRgb(0xC4, 0x2B, 0x1C);

    /// <summary>True when the taskbar is light, so tray icons need to be dark.</summary>
    public static bool IsTaskbarLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    public static int TrayIconSize => Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */));

    /// <summary>A single-colour glyph for the notification area.</summary>
    public static BitmapSource RenderTray(MicVisual visual, bool lightTaskbar, int size)
    {
        var color = visual == MicVisual.Muted
            ? (lightTaskbar ? CriticalOnLight : CriticalOnDark)
            : (lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        var opacity = visual == MicVisual.Disconnected ? 0.45 : 1.0;
        return Render(size, context =>
            DrawGlyph(context, visual == MicVisual.Muted ? MicOffGlyph : MicGlyph, size, size, color, opacity));
    }

    /// <summary>A glyph on a round badge, readable on top of anything.</summary>
    public static BitmapSource RenderOverlay(MicVisual visual, int size)
    {
        var badge = visual == MicVisual.Muted ? Color.FromArgb(0xF0, 0xC4, 0x2B, 0x1C) : Color.FromArgb(0xD8, 0x20, 0x20, 0x20);
        var opacity = visual == MicVisual.Disconnected ? 0.45 : 1.0;
        return Render(size, context =>
        {
            var brush = new SolidColorBrush(badge);
            brush.Freeze();
            context.DrawEllipse(brush, null, new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);
            DrawGlyph(context, visual == MicVisual.Muted ? MicOffGlyph : MicGlyph, size, size * 0.56, Colors.White, opacity);
        });
    }

    /// <summary>Scales an image to a square bitmap.</summary>
    public static BitmapSource RenderImage(ImageSource image, int size, double opacity = 1) =>
        Render(size, context =>
        {
            var scale = Math.Min(size / image.Width, size / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            context.PushOpacity(opacity);
            context.DrawImage(image, new Rect((size - width) / 2, (size - height) / 2, width, height));
            context.Pop();
        });

    public static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load image {Path}", path);
            return null;
        }
    }

    /// <summary>Creates a Win32 icon. The caller owns it and must dispose it.</summary>
    public static NativeIcon ToIcon(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        var handle = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, bitmap.PixelWidth, bitmap.PixelHeight, 0);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create icon, error " + Marshal.GetLastWin32Error());
        }

        return new NativeIcon(handle);
    }

    private static BitmapSource Render(int size, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            draw(context);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawGlyph(DrawingContext context, string glyph, int canvasSize, double glyphSize, Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GlyphFont, glyphSize, brush, 1.0);
        context.DrawText(text, new Point((canvasSize - text.Width) / 2, (canvasSize - text.Height) / 2));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool isIcon, uint version, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

/// <summary>A Win32 icon handle that is destroyed when disposed.</summary>
internal sealed class NativeIcon : IDisposable
{
    private IntPtr handle;

    public NativeIcon(IntPtr handle)
    {
        this.handle = handle;
        Icon = System.Drawing.Icon.FromHandle(handle);
    }

    public System.Drawing.Icon Icon { get; }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (owned != IntPtr.Zero)
        {
            Icon.Dispose();
            DestroyIcon(owned);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
