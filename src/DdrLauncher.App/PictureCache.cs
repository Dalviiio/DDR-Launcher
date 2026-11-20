using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DdrLauncher.Core.Paks;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.App;

/// <summary>Game pictures saved as small PNG files in the launcher's cache, so they are decoded from the game only once.</summary>
public static class PictureCache
{
    /// <summary>Wide enough for the largest place a picture is shown (the details drawer).</summary>
    private const int MaxWidth = 420;

    private static readonly string Folder = Path.Combine(Settings.DataFolder, "Cache", "Pictures");

    // Assets the game does not have or that cannot be decoded: never looked for again.
    private static readonly string FailedList = Path.Combine(Folder, "failed.txt");
    private static readonly object FailedLock = new();
    private static HashSet<string>? _failed;

    /// <summary>The cached picture, or null when it is not cached yet.</summary>
    public static ImageSource? Load(string asset)
    {
        var path = PathFor(asset);
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Scales the picture down to the display size, writes it as PNG and returns it.</summary>
    public static ImageSource Save(string asset, DecodedImage image)
    {
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);
        if (image.Width > MaxWidth)
        {
            var scale = (double)MaxWidth / image.Width;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Folder);
        AtomicFile.Write(PathFor(asset), encoder.Save);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Forgets the failed list after the cache folder was deleted.</summary>
    public static void Clear()
    {
        lock (FailedLock)
        {
            _failed = null;
        }
    }

    public static bool Failed(string asset)
    {
        lock (FailedLock)
        {
            _failed ??= File.Exists(FailedList) ? [.. File.ReadAllLines(FailedList)] : [];
            return _failed.Contains(asset);
        }
    }

    public static void MarkFailed(IEnumerable<string> assets)
    {
        lock (FailedLock)
        {
            _failed ??= File.Exists(FailedList) ? [.. File.ReadAllLines(FailedList)] : [];
            if (assets.Count(_failed.Add) > 0)
            {
                Directory.CreateDirectory(Folder);
                AtomicFile.WriteText(FailedList, string.Join("\n", _failed));
            }
        }
    }

    private static string PathFor(string asset) =>
        Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(asset)))[..16] + ".png");
}
