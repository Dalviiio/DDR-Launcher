namespace DdrLauncher.Core.Safety;

/// <summary>
/// Thrown when an archive or folder cannot be imported safely. Nothing from it is kept.
/// </summary>
public sealed class ImportRejectedException : Exception
{
    public ImportRejectedException(string reason, Exception? inner = null)
        : base($"Import rejected: {reason}", inner)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
