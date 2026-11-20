using System.Security.Cryptography;
using DdrLauncher.Core;

namespace DdrLauncher.Core.Tests;

public sealed class UpdateTests
{
    [Fact]
    public void OnlyTheAuthorsSignatureOfTheExactFilePasses()
    {
        var file = Path.Combine(Path.GetTempPath(), $"DalviiioDDRLauncher-Setup-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(file, [1, 2, 3]);
            using var author = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var someoneElse = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = Convert.ToBase64String(author.ExportSubjectPublicKeyInfo());
            var signature = ReleaseSignature.Sign(file, author);

            Assert.True(ReleaseSignature.Verify(file, signature, publicKey));
            Assert.False(ReleaseSignature.Verify(file, ReleaseSignature.Sign(file, someoneElse), publicKey));
            Assert.False(ReleaseSignature.Verify(file, "not a signature", publicKey));

            File.WriteAllBytes(file, [1, 2, 4]);
            Assert.False(ReleaseSignature.Verify(file, signature, publicKey));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ADownloadIsKeptOnlyWhenItsSignatureIsTheAuthors()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ddr-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var author = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var someoneElse = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var update = new AvailableUpdate("9.9.9", new Uri("https://github.com/x/setup.exe"), "DalviiioDDRLauncher-Setup-9.9.9.exe", new Uri("https://github.com/x/setup.exe.sig"), "");
            byte[] installer = [4, 5, 6];

            // The signature is made on a copy with the same name, as the author's tool does.
            var signed = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), update.InstallerName);
            Directory.CreateDirectory(Path.GetDirectoryName(signed)!);
            File.WriteAllBytes(signed, installer);
            var good = ReleaseSignature.Sign(signed, author);
            var bad = ReleaseSignature.Sign(signed, someoneElse);
            Directory.Delete(Path.GetDirectoryName(signed)!, recursive: true);

            var publicKey = Convert.ToBase64String(author.ExportSubjectPublicKeyInfo());
            var path = await new UpdateService("x/y", publicKey, folder, new FakeGitHub(installer, good)).DownloadVerifiedAsync(update);
            Assert.Equal(installer, File.ReadAllBytes(path));

            await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateService("x/y", publicKey, folder, new FakeGitHub(installer, bad)).DownloadVerifiedAsync(update));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void TheNewestSignedReleaseIsPickedBetasIncluded()
    {
        const string releases = """
            [
              { "tag_name": "v0.6.0", "draft": true, "assets": [] },
              { "tag_name": "v0.5.0-beta", "body": "New", "assets": [
                  { "name": "DalviiioDDRLauncher-Setup-0.5.0.exe", "browser_download_url": "https://github.com/a/b/0.5.0.exe" },
                  { "name": "DalviiioDDRLauncher-Setup-0.5.0.exe.sig", "browser_download_url": "https://github.com/a/b/0.5.0.exe.sig" } ] },
              { "tag_name": "v0.7.0", "assets": [
                  { "name": "DalviiioDDRLauncher-Setup-0.7.0.exe", "browser_download_url": "https://github.com/a/b/0.7.0.exe" } ] },
              { "tag_name": "v0.4.0", "assets": [
                  { "name": "DalviiioDDRLauncher-Setup-0.4.0.exe", "browser_download_url": "https://github.com/a/b/0.4.0.exe" },
                  { "name": "DalviiioDDRLauncher-Setup-0.4.0.exe.sig", "browser_download_url": "http://github.com/a/b/0.4.0.exe.sig" } ] }
            ]
            """;

        var update = UpdateService.Newest(releases);

        // The draft and the release without a signature are skipped; a plain http link is not trusted.
        Assert.NotNull(update);
        Assert.Equal("0.5.0", update.Version);
        Assert.Equal("DalviiioDDRLauncher-Setup-0.5.0.exe", update.InstallerName);
        Assert.Equal("New", update.Notes);
        Assert.Null(UpdateService.Newest("{ \"message\": \"Not Found\" }"));
        Assert.Null(UpdateService.Newest("not json"));
    }

    // Answers the installer and the signature requests as GitHub would.
    private sealed class FakeGitHub(byte[] installer, string signature) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal) ? new StringContent(signature) : new ByteArrayContent(installer),
            });
    }
}
