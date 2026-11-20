using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DdrLauncher.Core;

/// <summary>A newer version of the launcher published on GitHub.</summary>
/// <param name="Version">"0.5.0".</param>
/// <param name="Notes">Release notes as the author wrote them (shown as plain text).</param>
public sealed record AvailableUpdate(string Version, Uri InstallerUrl, string InstallerName, Uri SignatureUrl, string Notes);

/// <summary>
/// Signatures of published installers (ECDSA P-256 over the file's SHA-256 and its name). The author signs each installer
/// with a private key kept on their own computer; the launcher only holds the public key and refuses any file whose signature
/// does not match, even from the official release page. The name holds the version, so an old signed installer cannot pass
/// for a newer one.
/// </summary>
public static class ReleaseSignature
{
    /// <summary>Signature of a file, base64 (written next to it as "&lt;file&gt;.sig").</summary>
    public static string Sign(string filePath, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignHash(Hash(filePath), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <summary>True only when <paramref name="signatureBase64"/> is a valid signature of the file by the key.</summary>
    public static bool Verify(string filePath, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            return key.VerifyHash(Hash(filePath), Convert.FromBase64String(signatureBase64.Trim()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static byte[] Hash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var name = Encoding.UTF8.GetBytes("DalviiioDDRLauncher release\n" + Path.GetFileName(filePath) + "\n");
        return SHA256.HashData([.. name, .. SHA256.HashData(stream)]);
    }
}

/// <summary>
/// Looks for a newer version on the repository's GitHub releases, downloads its installer and keeps it only when its
/// signature is valid. Used only when the user agreed. Nothing about the user is sent: a plain request for the release list.
/// </summary>
/// <param name="repository">"Owner/Name" of the GitHub repository.</param>
/// <param name="publicKey">The author's public signing key (base64 SubjectPublicKeyInfo).</param>
public sealed class UpdateService(string repository, string publicKey, string downloadFolder, HttpMessageHandler? handler = null)
{
    // An installer is about 80 MB: anything far bigger is not one.
    private const long MaxInstallerSize = 300L << 20;

    /// <summary>The newest published release (betas included) when it is newer than <paramref name="currentVersion"/>; else null.</summary>
    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, CancellationToken cancel = default)
    {
        using var http = Client();
        var json = await http.GetStringAsync(new Uri($"https://api.github.com/repos/{repository}/releases?per_page=10"), cancel);
        var latest = Newest(json);
        return latest is not null && Version.Parse(latest.Version) > Version.Parse(currentVersion) ? latest : null;
    }

    /// <summary>Downloads the installer and its signature; returns the installer's path only when the signature is valid.</summary>
    public async Task<string> DownloadVerifiedAsync(AvailableUpdate update, CancellationToken cancel = default)
    {
        // Installers of earlier updates are not needed any more.
        if (Directory.Exists(downloadFolder))
        {
            Directory.Delete(downloadFolder, recursive: true);
        }

        Directory.CreateDirectory(downloadFolder);
        var path = Path.Combine(downloadFolder, update.InstallerName);
        using var http = Client();
        using (var response = await http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxInstallerSize)
            {
                throw new InvalidOperationException("The update is too big to be the launcher's installer.");
            }

            await using var file = File.Create(path);
            await response.Content.CopyToAsync(file, cancel);
        }

        var signature = await http.GetStringAsync(update.SignatureUrl, cancel);
        if (!ReleaseSignature.Verify(path, signature, publicKey))
        {
            File.Delete(path);
            throw new InvalidOperationException("The downloaded update is not signed by the author of the launcher, so it was deleted and not installed.");
        }

        return path;
    }

    /// <summary>
    /// The newest non-draft release with a version tag ("v0.5.0" or "v0.5.0-beta") and its signed installer
    /// ("DalviiioDDRLauncher-Setup-0.5.0.exe" with ".sig"). Null when there is none.
    /// </summary>
    public static AvailableUpdate? Newest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return document.RootElement.EnumerateArray()
                .Where(r => !(r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True))
                .Select(Release)
                .OfType<AvailableUpdate>()
                .MaxBy(u => Version.Parse(u.Version));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AvailableUpdate? Release(JsonElement release)
    {
        var version = Text(release, "tag_name")?.TrimStart('v', 'V').Split('-')[0];
        if (!Version.TryParse(version, out _) || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var name = $"DalviiioDDRLauncher-Setup-{version}.exe";
        Uri? Asset(string file) => assets.EnumerateArray()
            .Where(a => string.Equals(Text(a, "name"), file, StringComparison.OrdinalIgnoreCase))
            .Select(a => Uri.TryCreate(Text(a, "browser_download_url"), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null)
            .FirstOrDefault(u => u is not null);
        return Asset(name) is { } installer && Asset(name + ".sig") is { } signature
            ? new AvailableUpdate(version!, installer, name, signature, Text(release, "body") ?? "")
            : null;
    }

    private HttpClient Client()
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);

        // GitHub's API refuses requests without a User-Agent; a plain product name says nothing about the user.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DalviiioDDRLauncher", "1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
