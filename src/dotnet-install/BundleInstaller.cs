/// <summary>
/// Installs the toolset a repo advertises via the "tools" array in
/// .dotnet-install.json. Each entry names a command (<c>name</c>) and exactly one
/// source: a repo-relative <c>project</c> to build, a NuGet <c>package</c>, or
/// another <c>repository</c>. A single entry is a one-tool repo; several form a
/// bundle, and they may mix sources freely. Installation stops at the first
/// failure, leaving already-installed tools in place (per the DotNetCliTool v3
/// bundle semantics).
/// </summary>
static class BundleInstaller
{
    /// <summary>A manifest entry resolved to something installable.</summary>
    internal abstract record Entry(string? Name)
    {
        /// <summary>How the entry is shown in bundle progress output.</summary>
        public abstract string Display { get; }
    }

    /// <summary>A project built from the advertising repo's own source tree.</summary>
    internal sealed record ProjectEntry(string? Name, string Relative, string Full) : Entry(Name)
    {
        public override string Display => Relative;
    }

    /// <summary>A tool pulled from NuGet.</summary>
    internal sealed record PackageEntry(string? Name, string Spec) : Entry(Name)
    {
        public override string Display => $"{Spec} (nuget)";
    }

    /// <summary>A tool built from another git repo.</summary>
    internal sealed record RepositoryEntry(string? Name, string Repository, string? Ref) : Entry(Name)
    {
        /// <summary>
        /// The owner/repo[@ref] spec GitSource parses. A manifest ref may name a
        /// branch, tag, or commit, so it goes through the spec string, which resolves
        /// any of the three (and pins the install) rather than asserting one kind.
        /// </summary>
        public string Spec => Ref is null ? Repository : $"{Repository}@{Ref}";

        public override string Display => $"{Spec} (repo)";
    }

    /// <summary>
    /// Repos whose manifests are currently being installed. A manifest may name
    /// another repo, which may name a third, so two repos advertising each other
    /// would clone forever without this.
    /// </summary>
    static readonly HashSet<string> InFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds and installs every tool in <paramref name="tools"/>.
    /// <paramref name="baseSource"/> is the provenance shared by entries built from
    /// the advertising repo (git repo, git URL, or local directory); each installed
    /// tool records its own project sub-path so it can be updated independently.
    /// Entries naming a package or another repository record that source instead, so
    /// they update from where they actually came from.
    /// </summary>
    public static async Task<int> InstallAsync(
        string rootDir,
        List<Tool> tools,
        string installDir,
        InstallSource baseSource,
        bool requireSourceLink = false,
        bool quiet = false)
    {
        var resolved = new List<Entry>();

        foreach (var tool in tools)
        {
            if (Resolve(tool, rootDir, tools.Count) is not { } entry)
                return 1;

            resolved.Add(entry);
        }

        if (resolved.Count == 0)
        {
            Console.Error.WriteLine("error: no tools to install");
            return 1;
        }

        bool bundle = resolved.Count > 1;
        if (!quiet && bundle)
            Console.WriteLine($"Installing bundle of {resolved.Count} tools...");

        int installed = 0;
        foreach (var entry in resolved)
        {
            if (!quiet && bundle)
            {
                Console.WriteLine();
                Console.WriteLine($"[{installed + 1}/{resolved.Count}] {entry.Display}");
            }

            int result = entry switch
            {
                ProjectEntry p => Installer.Install(
                    p.Full, installDir, WithProject(baseSource, p.Relative, p.Full),
                    requireSourceLink, quiet, commandName: p.Name),

                PackageEntry k => await Installer.InstallPackageAsync(
                    k.Spec, installDir, requireSourceLink, quiet),

                RepositoryEntry r => await InstallFromRepositoryAsync(
                    r, installDir, requireSourceLink, quiet),

                _ => 1,
            };

            if (result != 0)
            {
                Console.Error.WriteLine($"error: failed to install '{entry.Display}'; stopping.");
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
    /// Installs a manifest entry that names another repo, refusing to re-enter a repo
    /// already being installed further up the chain. <c>requireAdvertised</c> is off:
    /// naming the repo in a manifest is itself the opt-in, so a repo without its own
    /// manifest still installs its single executable project.
    /// </summary>
    static async Task<int> InstallFromRepositoryAsync(
        RepositoryEntry entry, string installDir, bool requireSourceLink, bool quiet)
    {
        if (!InFlight.Add(entry.Repository))
        {
            Console.Error.WriteLine($"error: manifest cycle: '{entry.Repository}' is already being installed.");
            return 1;
        }

        try
        {
            return await GitSource.InstallFromGitAsync(
                entry.Spec, installDir, useSsh: false, branch: null, tag: null, rev: null,
                projectOverride: null, requireSourceLink, quiet,
                requireAdvertised: false, commandName: entry.Name);
        }
        finally
        {
            InFlight.Remove(entry.Repository);
        }
    }

    /// <summary>
    /// Resolves one manifest entry, reporting to stderr and returning null when the
    /// entry names no source, names several, or points at a project that is missing.
    /// </summary>
    internal static Entry? Resolve(Tool tool, string rootDir, int toolCount)
    {
        string[] declared = tool.DeclaredSources();

        if (declared.Length > 1)
        {
            Console.Error.WriteLine(
                $"error: tool entry {Describe(tool)} names more than one source ({string.Join(", ", declared)}); pick one.");
            return null;
        }

        if (declared.Length == 0)
        {
            // A single-tool manifest may omit the source entirely and let the repo's
            // sole executable project be auto-detected. A bundle cannot: which project
            // maps to which command would be ambiguous.
            if (toolCount > 1)
            {
                Console.Error.WriteLine(
                    $"error: tool entry {Describe(tool)} names no source; give it a \"project\", \"package\", or \"repository\".");
                return null;
            }

            string? auto = GitSource.AutoDetectProject(rootDir);
            if (auto is null)
                return null;

            return new ProjectEntry(tool.Name, Path.GetRelativePath(rootDir, auto), auto);
        }

        if (tool.Package is { Length: > 0 } package)
        {
            string spec = tool.Version is { Length: > 0 } v ? $"{package}@{v}" : package;
            return new PackageEntry(tool.Name, spec);
        }

        if (tool.Repository is { Length: > 0 } repository)
            return new RepositoryEntry(tool.Name, repository, tool.Ref);

        string full = Path.GetFullPath(Path.Combine(rootDir, tool.Project!));
        if (!File.Exists(full))
        {
            Console.Error.WriteLine($"error: tool project not found: {tool.Project}");
            return null;
        }

        return new ProjectEntry(tool.Name, tool.Project!, full);
    }

    /// <summary>Identifies an entry in diagnostics, by name when it has one.</summary>
    static string Describe(Tool tool) =>
        tool.Name is { Length: > 0 } name ? $"'{name}'" : "(unnamed)";

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
