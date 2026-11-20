using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.App;

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

        return FromBitmap(bitmap);
    }

    public static void SavePng(DecodedImage image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(image)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static BitmapSource ToBitmap(DecodedImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A copy at most <paramref name="maxWidth"/> wide, for showing.</summary>
    public static BitmapSource Preview(DecodedImage image, int maxWidth)
    {
        BitmapSource bitmap = ToBitmap(image);
        if (image.Width > maxWidth)
        {
            var scale = (double)maxWidth / image.Width;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            bitmap.Freeze();
        }

        return bitmap;
    }

    private static DecodedImage FromBitmap(BitmapSource bitmap)
    {
        if (bitmap.Format != PixelFormats.Bgra32)
        {
            bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        }

        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return new DecodedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
