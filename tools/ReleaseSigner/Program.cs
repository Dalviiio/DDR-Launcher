using System.Security.Cryptography;
using DdrLauncher.Core;

// Signs releases of Dalviiio DDR Launcher.
//   keygen        creates the private key (once) and prints the public key to put in the launcher
//   sign <file>   writes <file>.sig, which the launcher checks before installing an update
//   pubkey        prints the public key again
// The private key stays on the author's computer, never in the repository (the tool refuses that).
var keyPath = Environment.GetEnvironmentVariable("DDR_SIGNING_KEY")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dalviiioddrlauncher", "release-signing-key.pem");

for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(keyPath))!); folder is not null; folder = folder.Parent)
{
    if (Directory.Exists(Path.Combine(folder.FullName, ".git")))
    {
        Console.Error.WriteLine("The private key must not be stored inside a repository.");
        return 2;
    }
}

switch (args.FirstOrDefault())
{
    case "keygen":
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine($"A key already exists at {keyPath}. Keep it: updates signed with a new key are refused by installed launchers.");
            return 1;
        }

        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Private key written to {keyPath}. Back it up somewhere safe and never share it.");
            Console.WriteLine("Public key (put it in App.UpdatePublicKey):");
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case "pubkey":
        using (var key = Load())
        {
            Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }

        return 0;

    case "sign" when args.Length == 2 && File.Exists(args[1]):
        using (var key = Load())
        {
            var signature = ReleaseSignature.Sign(args[1], key);
            File.WriteAllText(args[1] + ".sig", signature);
            if (!ReleaseSignature.Verify(args[1], signature, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())))
            {
                Console.Error.WriteLine("The signature could not be verified.");
                return 1;
            }

            Console.WriteLine($"Signed: {args[1]}.sig");
        }

        return 0;

    default:
        Console.WriteLine("Usage: ReleaseSigner keygen | pubkey | sign <file>");
        return 1;
}

ECDsa Load()
{
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(keyPath));
    return key;
}
