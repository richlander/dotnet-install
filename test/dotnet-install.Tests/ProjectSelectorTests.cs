namespace dotnet_install.Tests;

// The interactive project selector is opt-in: naming a path (`dotnet-install .`)
// enables it, while the bare command reports the ambiguity instead of prompting.
public class ProjectSelectorTests
{
    static (string? Result, string Stderr) SelectCapturingStderr(List<string> projects, string baseDir, bool interactive)
    {
        var original = Console.Error;
        var buffer = new StringWriter();
        Console.SetError(buffer);
        try
        {
            return (ProjectSelector.Select(projects, baseDir, interactive), buffer.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void NonInteractive_ReportsCandidates_WithoutPrompting()
    {
        List<string> projects =
        [
            Path.Combine("/repo", "src", "a", "a.csproj"),
            Path.Combine("/repo", "src", "b", "b.csproj"),
        ];

        var (result, stderr) = SelectCapturingStderr(projects, "/repo", interactive: false);

        Assert.Null(result);
        Assert.Contains("multiple executable projects found", stderr);
        Assert.Contains(Path.Combine("src", "a", "a.csproj"), stderr);
        Assert.Contains(Path.Combine("src", "b", "b.csproj"), stderr);
        // The menu must never render for a command that didn't ask for it.
        Assert.DoesNotContain("Navigate", stderr);
    }

    [Fact]
    public void TooManyCandidates_NeverPrompts_EvenWhenInteractive()
    {
        var projects = Enumerable.Range(0, 20)
            .Select(i => Path.Combine("/repo", "src", $"p{i}", $"p{i}.csproj"))
            .ToList();

        var (result, stderr) = SelectCapturingStderr(projects, "/repo", interactive: true);

        Assert.Null(result);
        Assert.Contains("too many to select interactively", stderr);
        Assert.DoesNotContain("Navigate", stderr);
    }
}
