/// <summary>
/// Installs a set of tools advertised by a repo via the "bundle" array in
/// .dotnet-install.json. Each entry is a repo-relative project that is built
/// and installed from source. Installation stops at the first failure, leaving
/// already-installed tools in place (per the DotNetCliTool v3 bundle semantics).
/// </summary>
static class BundleInstaller
{
    /// <summary>
    /// Builds and installs every project listed in <paramref name="bundle"/>.
    /// <paramref name="baseSource"/> is the provenance shared by all entries
    /// (git repo, git URL, or local directory); each installed tool records its
    /// own project sub-path so it can be updated independently.
    /// </summary>
    public static int Install(
        string rootDir,
        List<BundleEntry> bundle,
        string installDir,
        InstallSource baseSource,
        bool requireSourceLink = false,
        bool quiet = false)
    {
        var projects = new List<(string Relative, string Full)>();

        foreach (var entry in bundle)
        {
            if (string.IsNullOrWhiteSpace(entry.Project))
            {
                Console.Error.WriteLine("error: bundle entry is missing a \"project\" path");
                return 1;
            }

            string full = Path.GetFullPath(Path.Combine(rootDir, entry.Project));
            if (!File.Exists(full))
            {
                Console.Error.WriteLine($"error: bundle project not found: {entry.Project}");
                return 1;
            }

            projects.Add((entry.Project, full));
        }

        if (projects.Count == 0)
        {
            Console.Error.WriteLine("error: bundle is empty");
            return 1;
        }

        if (!quiet)
            Console.WriteLine($"Installing bundle of {projects.Count} tool{(projects.Count == 1 ? "" : "s")}...");

        int installed = 0;
        foreach (var (relative, full) in projects)
        {
            if (!quiet)
            {
                Console.WriteLine();
                Console.WriteLine($"[{installed + 1}/{projects.Count}] {relative}");
            }

            var source = WithProject(baseSource, relative, full);
            int result = Installer.Install(full, installDir, source, requireSourceLink, quiet);
            if (result != 0)
            {
                Console.Error.WriteLine($"error: failed to install '{relative}'; stopping.");
                if (installed > 0)
                    Console.Error.WriteLine($"{installed} tool{(installed == 1 ? "" : "s")} already installed; remove with 'dotnet-install rm <tool>' if needed.");
                return result;
            }

            installed++;
        }

        if (!quiet)
            Console.WriteLine($"\nInstalled {installed} tool{(installed == 1 ? "" : "s")}.");

        return 0;
    }

    /// <summary>
    /// Clones an InstallSource, overriding the project path. Local sources record
    /// the absolute project path; remote (git) sources record the repo-relative subpath.
    /// </summary>
    static InstallSource WithProject(InstallSource baseSource, string relative, string full) => new()
    {
        Type = baseSource.Type,
        Package = baseSource.Package,
        Version = baseSource.Version,
        Repository = baseSource.Repository,
        Ref = baseSource.Ref,
        Ssh = baseSource.Ssh,
        Commit = baseSource.Commit,
        Project = baseSource.Type == "local" ? full : relative,
        Pinned = baseSource.Pinned,
    };
}
