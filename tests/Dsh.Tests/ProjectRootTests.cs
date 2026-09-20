using Dsh.Memory;

namespace Dsh.Tests;

public sealed class ProjectRootTests
{
    [Fact]
    public void Resolve_NonGit_ReturnsStartDirectory()
    {
        var root = NewTempDir();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

            Assert.Equal(nested, ProjectRoot.Resolve(nested));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_GitDirectory_ReturnsRepoRoot()
    {
        var root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var nested = Directory.CreateDirectory(Path.Combine(root, "src", "app")).FullName;

            Assert.Equal(root, ProjectRoot.Resolve(nested));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_Worktree_MergesIntoMainWorktreeRoot()
    {
        var root = NewTempDir();
        try
        {
            var main = Directory.CreateDirectory(Path.Combine(root, "main")).FullName;
            var worktreeGitDir = Directory.CreateDirectory(Path.Combine(main, ".git", "worktrees", "wt1")).FullName;
            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");
            var worktree = Directory.CreateDirectory(Path.Combine(root, "wt1")).FullName;
            File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {worktreeGitDir}");
            var nested = Directory.CreateDirectory(Path.Combine(worktree, "src")).FullName;

            Assert.Equal(main, ProjectRoot.Resolve(nested));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_GitFileWithoutCommondir_FallsBackToOwnDirectory()
    {
        var root = NewTempDir();
        try
        {
            var gitDir = Directory.CreateDirectory(Path.Combine(root, "elsewhere", ".git", "modules", "sub")).FullName;
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, ".git"), $"gitdir: {gitDir}");

            Assert.Equal(sub, ProjectRoot.Resolve(sub));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string NewTempDir()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dsh-proot-{Guid.NewGuid():N}")).FullName;
}
