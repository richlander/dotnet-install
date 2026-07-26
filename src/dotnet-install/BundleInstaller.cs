/// <summary>
/// Installs the toolset a repo advertises via the "tools" array in
/// .dotnet-install.json. Each entry names a command (<c>name</c>) and, in source
/// scenarios, the repo-relative <c>project</c> to build and install. A single
/// entry is a one-tool repo; several entries form a bundle. Installation stops at
/// the first failure, leaving already-installed tools in place (per the
/// DotNetCliTool v3 bundle semantics).
/// </summary>
static class BundleInstaller
{
    /// <summary>
    /// Builds and installs every tool in <paramref name="tools"/>.
    /// <paramref name="baseSource"/> is the provenance shared by all entries
    /// (git repo, git URL, or local directory); each installed tool records its
    /// own project sub-path so it can be updated independently. Each tool's
    /// <c>project</c> is resolved explicitly, or — for a single-tool manifest that
    /// omits it — auto-detected from the repo's sole executable project.
    /// </summary>
    public static int Install(
        string rootDir,
        List<Tool> tools,
        string installDir,
        InstallSource baseSource,
        bool requireSourceLink = false,
        bool quiet = false,
        InstallSource? update = null)
    {
        var resolved = new List<(string? Name, string Relative, string Full)>();

        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Project))
            {
                // A multi-tool manifest cannot auto-detect: which project maps to
                // which command is ambiguous, so every entry must name one.
                if (tools.Count > 1)
                {
                    Console.Error.WriteLine("error: each tool in a multi-tool manifest must specify a \"project\".");
                    return 1;
                }

                // Single tool with no project → auto-detect the repo's sole executable.
                string? auto = GitSource.AutoDetectProject(rootDir);
                if (auto is null)
                    return 1;

                resolved.Add((tool.Name, Path.GetRelativePath(rootDir, auto), auto));
                continue;
            }

            string full = Path.GetFullPath(Path.Combine(rootDir, tool.Project));
            if (!File.Exists(full))
            {
                Console.Error.WriteLine($"error: tool project not found: {tool.Project}");
                return 1;
            }

            resolved.Add((tool.Name, tool.Project, full));
        }

        if (resolved.Count == 0)
        {
            Console.Error.WriteLine("error: no tools to install");
            return 1;
        }

        bool bundle = resolved.Count > 1;
        if (!quiet && bundle)
            Console.WriteLine($"Installing bundle of {resolved.Count} tools...");

        // A repo-level update channel (e.g. a single NuGet package) describes one
        // advertised tool. For a multi-tool bundle it cannot map to any single
        // member, so members update from their (git/local) source instead.
        InstallSource? toolUpdate = bundle ? null : update;

        int installed = 0;
        foreach (var (name, relative, full) in resolved)
        {
            if (!quiet && bundle)
            {
                Console.WriteLine();
                Console.WriteLine($"[{installed + 1}/{resolved.Count}] {relative}");
            }

            var source = WithProject(baseSource, relative, full);
            int result = Installer.Install(full, installDir, source, requireSourceLink, quiet, update: toolUpdate, commandName: name);
            if (result != 0)
            {
                Console.Error.WriteLine($"error: failed to install '{relative}'; stopping.");
                if (installed > 0)
                    Console.Error.WriteLine($"{installed} tool{(installed == 1 ? "" : "s")} already installed; remove with 'dotnet-install rm <tool>' if needed.");
                return result;
            }

            installed++;
        }

        if (!quiet && bundle)
            Console.WriteLine($"\nInstalled {installed} tools.");

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
