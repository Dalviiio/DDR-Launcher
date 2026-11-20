using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Data;

/// <summary>Reads pictures (textures) out of the paks the game loads, other mods included.</summary>
public static class GamePictures
{
    /// <summary>
    /// Decodes each asset found in the paks and hands it to <paramref name="found"/> (from several threads), trimmed to its visible part.
    /// Returns the assets that gave no picture: missing from the paks, or in a format it cannot read.
    /// </summary>
    /// <param name="assets">Game asset paths such as "/DakarGame/UMG2/Cars/Car_308.Car_308".</param>
    public static IReadOnlyCollection<string> Extract(GameInstall game, IReadOnlyCollection<string> assets, string[] oodleFolders,
        Action<string, DecodedImage> found, CancellationToken cancel = default)
    {
        var byPath = assets.Distinct().Where(a => PakPath(a) is not null).ToDictionary(a => PakPath(a)!, StringComparer.OrdinalIgnoreCase);
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>(assets.Distinct().Where(a => PakPath(a) is null));

        // Paks are read one file at a time; decoding (the slow part) runs on a few threads, a bounded number at once.
        using var slots = new SemaphoreSlim(Environment.ProcessorCount);
        var decoding = new List<Task>();
        var missing = GamePaks.Read(game, byPath.Keys, oodleFolders, (path, bytes) =>
        {
            slots.Wait(cancel);
            decoding.Add(Task.Run(() =>
            {
                try
                {
                    if (TextureDecoder.Decode(bytes) is { } image)
                    {
                        found(byPath[path], Trim(image));
                    }
                    else
                    {
                        failed.Add(byPath[path]);
                    }
                }
                // A picture from a mod is untrusted: one that breaks the decoder is skipped, the others still load.
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    failed.Add(byPath[path]);
                }
                finally
                {
                    slots.Release();
                }
            }, cancel));
        }, cancel);
        Task.WaitAll([.. decoding], cancel);
        return [.. failed, .. missing.Select(p => byPath[p])];
    }

    /// <summary>"/DakarGame/UMG2/X/Y.Y" -> "Dakar2Game/Plugins/Game/DakarGame/Content/UMG2/X/Y.uexp".</summary>
    internal static string? PakPath(string asset)
    {
        var dot = asset.LastIndexOf('.');
        var package = (dot > 0 ? asset[..dot] : asset).TrimStart('/');
        var slash = package.IndexOf('/');
        if (slash <= 0 || package.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var (mount, rest) = (package[..slash], package[(slash + 1)..]);
        return mount switch
        {
            "Game" => $"Dakar2Game/Content/{rest}.uexp",
            "DakarGame" => $"Dakar2Game/Plugins/Game/DakarGame/Content/{rest}.uexp",
            _ when mount.StartsWith("Dakar19Art", StringComparison.Ordinal) => $"Dakar2Game/Plugins/Art/{mount}/Content/{rest}.uexp",
            _ => null,
        };
    }

    /// <summary>Cuts the fully transparent border off, so the vehicle fills the space it is shown in.</summary>
    internal static DecodedImage Trim(DecodedImage image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Bgra[(y * image.Width + x) * 4 + 3] > 8)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (right < 0 || (left == 0 && top == 0 && right == image.Width - 1 && bottom == image.Height - 1))
        {
            return image;
        }

        int width = right - left + 1, height = bottom - top + 1;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(image.Bgra, ((top + y) * image.Width + left) * 4, pixels, y * width * 4, width * 4);
        }

        return new DecodedImage(width, height, pixels);
    }
}
