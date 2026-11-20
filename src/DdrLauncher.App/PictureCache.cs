using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Media;
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
        Directory.CreateDirectory(Folder);
        return Images.SavePng(image, PathFor(asset), MaxWidth);
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
            return FailedAssets.Contains(asset);
        }
    }

    public static void MarkFailed(IEnumerable<string> assets)
    {
        lock (FailedLock)
        {
            if (assets.Count(FailedAssets.Add) > 0)
            {
                Directory.CreateDirectory(Folder);
                AtomicFile.WriteText(FailedList, string.Join("\n", FailedAssets));
            }
        }
    }

    private static HashSet<string> FailedAssets => _failed ??= File.Exists(FailedList) ? [.. File.ReadAllLines(FailedList)] : [];

    private static string PathFor(string asset) =>
        Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(asset)))[..16] + ".png");
}

/// <summary>Converts between picture files, WPF bitmaps and the BGRA pixels the launcher's core works with.</summary>
internal static class Images
{
    /// <summary>Reads a picture file; scaled to <paramref name="width"/> x <paramref name="height"/> when given and different.</summary>
    public static DecodedImage Load(string path, int width = 0, int height = 0)
    {
        BitmapSource bitmap;
        using (var stream = File.OpenRead(path))
        {
            bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }

        if (width > 0 && (bitmap.PixelWidth != width || bitmap.PixelHeight != height))
        {
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform((double)width / bitmap.PixelWidth, (double)height / bitmap.PixelHeight));
        }

        if (bitmap.Format != PixelFormats.Bgra32)
        {
            bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        }

        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return new DecodedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    /// <summary>Writes the picture as PNG, at most <paramref name="maxWidth"/> wide, and returns what was written.</summary>
    public static BitmapSource SavePng(DecodedImage image, string path, int maxWidth = int.MaxValue)
    {
        var bitmap = Preview(image, maxWidth);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        AtomicFile.Write(path, encoder.Save);
        return bitmap;
    }

    /// <summary>A copy at most <paramref name="maxWidth"/> wide, for showing.</summary>
    public static BitmapSource Preview(DecodedImage image, int maxWidth)
    {
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);
        if (image.Width > maxWidth)
        {
            var scale = (double)maxWidth / image.Width;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }

        bitmap.Freeze();
        return bitmap;
    }
}
