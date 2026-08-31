using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Strata.Core;

namespace Strata.App;

/// <summary>
/// Drives the real scan, layout and render path with no window on screen, so a
/// build can be checked on a machine nobody is looking at. Writes a PNG of the
/// treemap so the result can also be looked at afterwards.
/// </summary>
public static class SelfTest
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static readonly List<string> Log = [];

    private static void Say(string line)
    {
        Log.Add(line);
        Console.WriteLine(line);
    }

    public static int Run()
    {
        // A WinExe has no console of its own; borrow the one that launched it.
        AttachConsole(-1);

        int pass = 0, fail = 0;
        void Ok(string name, bool condition, string detail = "")
        {
            if (condition) { pass++; Say($"  PASS  {name}{(detail.Length > 0 ? "  " + detail : "")}"); }
            else { fail++; Say($"  FAIL  {name}{(detail.Length > 0 ? "  " + detail : "")}"); }
        }

        var sandbox = Path.Combine(Path.GetTempPath(), "strata-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, "Movies"));
            Directory.CreateDirectory(Path.Combine(sandbox, "Photos"));
            Directory.CreateDirectory(Path.Combine(sandbox, "code", "app", "node_modules"));
            File.WriteAllBytes(Path.Combine(sandbox, "Movies", "holiday.mp4"), new byte[400_000]);
            File.WriteAllBytes(Path.Combine(sandbox, "Movies", "wedding.mov"), new byte[300_000]);
            File.WriteAllBytes(Path.Combine(sandbox, "Photos", "img1.jpg"), new byte[110_000]);
            File.WriteAllBytes(Path.Combine(sandbox, "code", "app", "node_modules", "react.js"), new byte[123_000]);
            File.WriteAllBytes(Path.Combine(sandbox, "notes.txt"), new byte[500]);

            Say("\nSTRATA APP — the window's own scan and render path");

            var scan = new Scanner().Scan(sandbox);
            var tree = TreeNode.Build(scan.Files, sandbox, "Sandbox");
            Ok("the scan finds every file", scan.Files.Count == 5, $"{scan.Files.Count}");
            Ok("and totals them", tree.Size == 933_500, $"{tree.Size}");

            var items = tree.Children();
            Ok("the top level ranks biggest first", items[0].Name == "Movies", items[0].Name);
            Ok("a folder is coloured by what fills it", items[0].Category == "Video", items[0].Category);

            const int W = 900, H = 500;
            var view = new TreemapView();
            view.SetItems(items);
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));
            view.UpdateLayout();

            var bitmap = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);

            var pixels = new byte[W * H * 4];
            bitmap.CopyPixels(pixels, W * 4, 0);

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int key = pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            var ranked = counts.OrderByDescending(kv => kv.Value).ToList();
            double topShare = (double)ranked[0].Value / (W * H) * 100;

            Ok("the treemap actually paints", counts.Count > 3, $"{counts.Count} distinct colours");
            // Every folder drawn the same colour was the bug in the web version:
            // the picture showed where the space went but not what it was.
            Ok("and is not one flat slab of a single colour", topShare < 90, $"largest colour {topShare:F1}%");

            var video = ColourShare(ranked, W * H, "#e11d48");
            Ok("the video folder dominates the picture, as it does the disk",
                video is > 60 and < 90, $"video {video:F1}% of the map");

            var png = Path.Combine(Path.GetTempPath(), "strata-selftest-map.png");
            using (var fs = File.Create(png))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fs);
            }
            Ok("a picture of the map was written", File.Exists(png), png);

            var guard = SafetyGuard.ForThisMachine();
            Ok("the guard still refuses the Windows folder",
                !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Allowed);
            Ok("and allows an ordinary file", guard.Check(Path.Combine(sandbox, "notes.txt")).Allowed);

            // The whole window, built and laid out off screen. This is what
            // catches a XAML file that parses but cannot actually render.
            var window = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                Width = 1200,
                Height = 780,
                ShowInTaskbar = false,
            };
            window.Show();
            window.LoadForSelfTest(sandbox);
            window.UpdateLayout();

            var content = (FrameworkElement)window.Content;
            Ok("the window's layout resolves", content.ActualWidth > 500 && content.ActualHeight > 400,
                $"{content.ActualWidth:F0}x{content.ActualHeight:F0}");
            Ok("the headline total reaches the window",
                window.FindName("StatTotal") is System.Windows.Controls.TextBlock { Text: "934 KB" },
                (window.FindName("StatTotal") as System.Windows.Controls.TextBlock)?.Text);
            Ok("the largest-files list is populated",
                window.FindName("ListBig") is System.Windows.Controls.ListView { Items.Count: 5 },
                $"{(window.FindName("ListBig") as System.Windows.Controls.ListView)?.Items.Count}");
            Ok("the rebuildable list found node_modules",
                window.FindName("ListRebuild") is System.Windows.Controls.ListView { Items.Count: 1 });
            Ok("the cleanup button starts disabled, so nothing can be removed by accident",
                window.FindName("BtnClean") is System.Windows.Controls.Button { IsEnabled: false });
            Ok("every tab is present",
                window.FindName("Tabs") is System.Windows.Controls.TabControl { Items.Count: 5 });

            var shot = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            shot.Render(content);
            var windowPng = Path.Combine(Path.GetTempPath(), "strata-selftest-window.png");
            using (var fs = File.Create(windowPng))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(shot));
                enc.Save(fs);
            }
            Ok("and a picture of the window was written", File.Exists(windowPng), windowPng);
            window.Close();
        }
        catch (Exception ex)
        {
            fail++;
            Say($"  FAIL  self test threw  {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { }
        }

        Say($"\n{pass} passed, {fail} failed");
        try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "strata-selftest.log"), Log); } catch { }
        return fail > 0 ? 1 : 0;
    }

    private static double ColourShare(List<KeyValuePair<int, int>> ranked, int total, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        long hits = 0;
        foreach (var (key, count) in ranked)
        {
            int b = key & 255, g = (key >> 8) & 255, r = (key >> 16) & 255;
            if (Math.Abs(r - c.R) < 30 && Math.Abs(g - c.G) < 30 && Math.Abs(b - c.B) < 30) hits += count;
        }
        return (double)hits / total * 100;
    }
}
