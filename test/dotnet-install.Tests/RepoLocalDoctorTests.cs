namespace dotnet_install.Tests;

// Tests for repo-local install discovery in `doctor`: finding the git root
// (worktree-aware) and idempotently gitignoring the .dotnet/ directory.
public class RepoLocalDoctorTests
{
    static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dni-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FindGitRoot_WalksUp_ToDirectoryWithGitFolder()
    {
        string root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            string nested = Path.Combine(root, "src", "app");
            Directory.CreateDirectory(nested);

            Assert.Equal(root, DoctorCommand.FindGitRoot(nested));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindGitRoot_RecognizesGitFile_ForWorktrees()
    {
        string root = NewTempDir();
        try
        {
            // Linked worktrees have a `.git` file (not a directory).
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: /somewhere/.git/worktrees/wt");

            Assert.Equal(root, DoctorCommand.FindGitRoot(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindGitRoot_ReturnsNull_WhenNotInRepo()
    {
        string dir = NewTempDir();
        try
        {
            Assert.Null(DoctorCommand.FindGitRoot(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AddToGitignore_CreatesFile_WhenMissing()
    {
        string root = NewTempDir();
        try
        {
            DoctorCommand.AddToGitignore(root);

            string content = File.ReadAllText(Path.Combine(root, ".gitignore"));
            Assert.Contains(".dotnet/", content);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AddToGitignore_AppendsWithSeparator_ToExistingFile()
    {
        string root = NewTempDir();
        try
        {
            string gitignore = Path.Combine(root, ".gitignore");
            File.WriteAllText(gitignore, "bin/"); // no trailing newline

            DoctorCommand.AddToGitignore(root);

            string content = File.ReadAllText(gitignore);
            Assert.Contains("bin/", content);
            Assert.Contains(".dotnet/", content);
            Assert.DoesNotContain("bin/.dotnet", content); // separator inserted
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
