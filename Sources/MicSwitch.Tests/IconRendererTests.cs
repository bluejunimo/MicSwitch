using System.Windows.Media.Imaging;
using MicSwitch.Shell;

namespace MicSwitch.Tests;

public class IconRendererTests
{
    [Fact]
    public void Tray_icons_are_drawn_distinctly_for_each_state_and_taskbar_colour()
    {
        RunOnStaThread(() =>
        {
            var rendered = new Dictionary<string, byte[]>();
            foreach (var visual in Enum.GetValues<MicVisual>())
            {
                foreach (var light in new[] { false, true })
                {
                    var bitmap = IconRenderer.RenderTray(visual, light, 32);
                    var pixels = Pixels(bitmap);
                    Assert.Contains(pixels, x => x != 0); // something was drawn
                    rendered[$"tray-{visual}-{(light ? "light" : "dark")}"] = pixels;
                    Save(bitmap, $"tray-{visual}-{(light ? "light" : "dark")}");

                    using var icon = IconRenderer.ToIcon(bitmap);
                    Assert.Equal(32, icon.Icon.Width);
                }
            }

            Assert.Equal(rendered.Count, rendered.Values.Select(Convert.ToBase64String).Distinct().Count());

            foreach (var visual in Enum.GetValues<MicVisual>())
            {
                Save(IconRenderer.RenderOverlay(visual, 128), $"overlay-{visual}");
            }
        });
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    // Set MICSWITCH_TEST_OUTPUT to a folder to keep the rendered icons for a look.
    private static void Save(BitmapSource bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("MICSWITCH_TEST_OUTPUT") is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
