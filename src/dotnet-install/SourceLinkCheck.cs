using SourceLinkFetch;

static class SourceLinkCheck
{
    /// <summary>
    /// Verifies that assemblies in the given directory have SourceLink metadata.
    /// Returns true if verification passes (or no DLLs found), false if SourceLink is missing.
    /// </summary>
    public static bool Verify(string directory)
    {
        var dlls = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
        if (dlls.Length == 0)
            return true;

        bool anyChecked = false;
        bool allPassed = true;

        foreach (var dll in dlls)
        {
            try
            {
                using var reader = SourceLinkReader.Open(dll);
                if (!reader.HasPdb)
                    continue;

                anyChecked = true;

                if (!reader.HasSourceLink)
                {
                    Console.Error.WriteLine($"  warning: {Path.GetFileName(dll)} has PDB but no SourceLink");
                    allPassed = false;
                    continue;
                }

                string repo = reader.RepositoryUrl ?? "unknown";
                string commit = reader.CommitHash is { Length: > 7 } c ? c[..7] : "unknown";
                Console.WriteLine($"  SourceLink: {Path.GetFileName(dll)} → {repo} ({commit})");
            }
            catch
            {
                // Not a managed assembly or unreadable — skip
            }
        }

        if (!anyChecked)
        {
            Console.Error.WriteLine("  warning: no assemblies with PDB data found for SourceLink verification");
            return false;
        }

        return allPassed;
    }
}
