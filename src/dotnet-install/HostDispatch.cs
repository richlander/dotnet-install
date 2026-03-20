using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Runtime host dispatch for managed tools (BusyBox model).
/// When dotnet-install is invoked via a symlink named after a tool,
/// this module resolves the tool's metadata, checks runtime compatibility,
/// and execs into `dotnet exec`.
/// </summary>
static unsafe class HostDispatch
{
    public static int Run(string toolName, string[] args)
    {
        // The symlink (e.g. dotnetsay) and dotnet-install live in the same directory.
        // ProcessPath resolves the symlink to the actual binary, giving us the install dir.
        // We can't use GetCommandLineArgs()[0] because argv[0] may be a bare name
        // (e.g. "dotnetsay") which would resolve relative to CWD, not the install dir.
        string? installDir = Path.GetDirectoryName(Environment.ProcessPath);

        if (installDir is null)
        {
            Console.Error.WriteLine($"error: unable to determine install directory");
            return 1;
        }

        string appDir = Path.Combine(installDir, $"_{toolName}");

        if (!Directory.Exists(appDir))
        {
            Console.Error.WriteLine($"error: tool '{toolName}' is not installed (missing {appDir})");
            return 1;
        }

        // Read tool metadata
        var manifest = ToolMetadata.Read(appDir);
        if (manifest is null)
        {
            Console.Error.WriteLine($"error: tool '{toolName}' is missing metadata (.tool.json)");
            return 1;
        }

        string? entryPoint = manifest.EntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
        {
            Console.Error.WriteLine($"error: tool '{toolName}' has no entry point in .tool.json");
            return 1;
        }

        string entryDll = Path.Combine(appDir, entryPoint);
        if (!File.Exists(entryDll))
        {
            Console.Error.WriteLine($"error: tool entry point not found: {manifest.EntryPoint}");
            return 1;
        }

        // Check runtime compatibility
        string? runtimeConfig = FindRuntimeConfig(appDir, toolName);
        if (runtimeConfig is not null)
        {
            var compat = RuntimeCompat.CheckCompatibility(runtimeConfig);
            if (!compat.CanRun)
            {
                if (manifest.RollForward && compat.RollForwardWouldHelp)
                {
                    // Roll-forward is enabled and a compatible runtime exists — proceed
                }
                else
                {
                    Console.Error.WriteLine($"error: {toolName} requires {compat.RequiredFramework} {compat.RequiredVersion} which is not installed.");
                    Console.Error.WriteLine();

                    if (compat.RollForwardWouldHelp && !manifest.RollForward)
                    {
                        Console.Error.WriteLine("A compatible runtime is available with roll-forward. Reinstall with:");
                        Console.Error.WriteLine($"  dotnet install --package {toolName} --allow-roll-forward");
                        Console.Error.WriteLine();
                    }

                    Console.Error.WriteLine($"Or install .NET {compat.RequiredVersion}: https://dot.net/download");
                    return 1;
                }
            }
        }

        // Build dotnet exec command
        string dotnetPath = FindDotnet();
        var execArgs = new List<string>();

        if (manifest.RollForward)
        {
            execArgs.Add("exec");
            execArgs.Add("--roll-forward");
            execArgs.Add("LatestMajor");
        }
        else
        {
            execArgs.Add("exec");
        }

        execArgs.Add(entryDll);
        execArgs.AddRange(args);

        // On Unix, replace this process with dotnet (like exec in shell)
        if (!OperatingSystem.IsWindows())
            return ExecReplace(dotnetPath, execArgs);

        // On Windows, start a child process
        return ExecChild(dotnetPath, execArgs);
    }

    static string FindDotnet()
    {
        // If DOTNET_ROOT is set, use the dotnet from there
        string? dotnetRoot = HostFxr.DotnetRoot;
        if (dotnetRoot is not null)
        {
            string candidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        // Fall back to PATH resolution
        return "dotnet";
    }

    static string? FindRuntimeConfig(string appDir, string toolName)
    {
        string exact = Path.Combine(appDir, $"{toolName}.runtimeconfig.json");
        if (File.Exists(exact)) return exact;

        var configs = Directory.GetFiles(appDir, "*.runtimeconfig.json");
        return configs.Length > 0 ? configs[0] : null;
    }

    /// <summary>
    /// Replace current process with dotnet via execvp (Unix only).
    /// This is the same semantics as `exec dotnet ...` in a shell script.
    /// </summary>
    static int ExecReplace(string program, List<string> args)
    {
        // Build null-terminated argv array for execvp
        var allArgs = new List<string> { program };
        allArgs.AddRange(args);

        nint[] nativeArgs = new nint[allArgs.Count + 1];
        try
        {
            for (int i = 0; i < allArgs.Count; i++)
                nativeArgs[i] = HostFxr.MarshalString(allArgs[i]);
            nativeArgs[allArgs.Count] = 0; // null terminator

            nint programPtr = HostFxr.MarshalString(program);
            try
            {
                fixed (nint* argv = nativeArgs)
                {
                    HostFxr.ExecVP(programPtr, argv);
                }

                // execvp only returns on error
                int errno = Marshal.GetLastPInvokeError();
                Console.Error.WriteLine($"error: failed to exec dotnet (errno {errno})");
                Console.Error.WriteLine("Is .NET installed? https://dot.net/download");
                return 1;
            }
            finally
            {
                HostFxr.FreeString(programPtr);
            }
        }
        finally
        {
            foreach (nint ptr in nativeArgs)
                HostFxr.FreeString(ptr);
        }
    }

    /// <summary>
    /// Start dotnet as a child process (Windows).
    /// </summary>
    static int ExecChild(string program, List<string> args)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine("error: failed to start dotnet");
            Console.Error.WriteLine("Is .NET installed? https://dot.net/download");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
}
