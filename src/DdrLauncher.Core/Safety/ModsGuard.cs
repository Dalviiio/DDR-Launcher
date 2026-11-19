namespace DdrLauncher.Core.Safety;

/// <summary>Rules every write to the game's mods folder follows.</summary>
public static class ModsGuard
{
    /// <summary>The game reads its mods when it starts and keeps them open: nothing in ~mods changes while it runs.</summary>
    public static void EnsureGameClosed(Func<bool> isGameRunning)
    {
        if (isGameRunning())
        {
            throw new InvalidOperationException("Close Dakar Desert Rally first: its mods cannot change while it is running.");
        }
    }
}
