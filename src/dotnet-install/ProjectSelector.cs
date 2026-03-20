/// <summary>
/// Interactive project selector for repos with multiple executable projects.
/// Presents an arrow-key navigable menu when running interactively,
/// or lists choices and errors when piped.
/// </summary>
static class ProjectSelector
{
    const int MaxInteractiveItems = 12;

    /// <summary>
    /// Prompts the user to select a project from a list.
    /// Returns the selected project path, or null if cancelled/too many.
    /// </summary>
    public static string? Select(List<string> projects, string baseDir)
    {
        var relative = projects
            .Select(p => Path.GetRelativePath(baseDir, p))
            .ToList();

        if (projects.Count > MaxInteractiveItems || Console.IsInputRedirected)
        {
            Console.Error.WriteLine(projects.Count > MaxInteractiveItems
                ? $"error: {projects.Count} executable projects found (too many to select interactively). Use --project to specify:"
                : "error: multiple executable projects found. Use --project to specify:");

            foreach (string p in relative)
                Console.Error.WriteLine($"  {p}");

            return null;
        }

        Console.Error.WriteLine("Multiple executable projects found. Select one:");
        Console.Error.WriteLine();

        int selected = 0;

        Console.Error.Write("\x1b[?25l"); // hide cursor

        try
        {
            RenderMenu(relative, selected, firstRender: true);

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K:
                        selected = (selected - 1 + projects.Count) % projects.Count;
                        RenderMenu(relative, selected);
                        break;

                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J:
                        selected = (selected + 1) % projects.Count;
                        RenderMenu(relative, selected);
                        break;

                    case ConsoleKey.Enter:
                        // Clear the menu and show the selection
                        ClearLines(relative.Count + 1); // +1 for hint line
                        Console.Error.WriteLine($"  Selected: {relative[selected]}");
                        return projects[selected];

                    case ConsoleKey.Escape:
                    case ConsoleKey.Q:
                        ClearLines(relative.Count + 1);
                        Console.Error.WriteLine("Cancelled.");
                        return null;
                }
            }
        }
        finally
        {
            Console.Error.Write("\x1b[?25h"); // show cursor
        }
    }

    static void RenderMenu(List<string> items, int selected, bool firstRender = false)
    {
        if (!firstRender)
        {
            // Move cursor up to overwrite previous menu (+1 for hint line)
            Console.Error.Write($"\x1b[{items.Count + 1}A");
        }

        for (int i = 0; i < items.Count; i++)
        {
            // Clear line, then write
            Console.Error.Write("\x1b[2K");

            if (i == selected)
                Console.Error.WriteLine($"  \x1b[36m❯ {items[i]}\x1b[0m");
            else
                Console.Error.WriteLine($"    {items[i]}");
        }

        // Hint line
        Console.Error.Write("\x1b[2K");
        Console.Error.Write("  \x1b[90m↑↓/jk Navigate  Enter Select  Esc Cancel\x1b[0m");
    }

    static void ClearLines(int count)
    {
        Console.Error.Write($"\x1b[{count}A");
        for (int i = 0; i < count; i++)
            Console.Error.Write("\x1b[2K\n");
        Console.Error.Write($"\x1b[{count}A");
    }
}
