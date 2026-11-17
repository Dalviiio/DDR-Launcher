namespace DdrLauncher.Core.Safety;

/// <summary>
/// Thrown when a path is rejected by a safety rule. The path is never used after this is thrown.
/// </summary>
public sealed class UnsafePathException : Exception
{
    public UnsafePathException(string path, string reason)
        : base($"Unsafe path '{path}': {reason}")
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }

    public string Reason { get; }
}
