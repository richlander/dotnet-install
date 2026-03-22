/// <summary>
/// Self-install helper: ensure dotnet-install binary exists in the install directory.
/// Used by DoctorCommand and install scripts.
/// </summary>
static class SetupCommand
{
    /// <summary>
    /// Ensure dotnet-install is installed locally with full pedigree (NuGet metadata,
    /// .tool.json, version tracking). If running from an external location (e.g. dotnet
    /// tool .store), self-install from NuGet so the binary is standalone and updatable.
    /// </summary>
    public static async Task<bool> EnsureLocalInstallAsync(string installDir)
    {
        string selfName = "dotnet-install";
        string targetPath = Path.Combine(installDir, selfName);

        // Already have a local binary — nothing to do
        if (File.Exists(targetPath))
        {
            string? processPath = Environment.ProcessPath;
            if (processPath is not null)
            {
                string processDir = Path.GetDirectoryName(Path.GetFullPath(processPath))!;
                if (string.Equals(processDir, installDir, StringComparison.Ordinal))
                {
                    Console.WriteLine($"✔ dotnet-install is in {DisplayPath(installDir)}");
                    return false;
                }
            }

            Console.WriteLine($"✔ dotnet-install is in {DisplayPath(installDir)}");
            return false;
        }

        // No local binary — self-install from NuGet with full pedigree
        Console.WriteLine($"Installing dotnet-install to {DisplayPath(installDir)}...");
        int result = await Installer.InstallPackageAsync(selfName, installDir);
        return result == 0;
    }

    static string DisplayPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace(home, "~");
    }
}
