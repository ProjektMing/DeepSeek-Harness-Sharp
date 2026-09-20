namespace Dsh.Memory;

/** 解析项目根:git 仓库取仓库根,worktree 归并到主工作树根,非 git 目录用起始目录本身。 */
public static class ProjectRoot
{
    private const string GitDirMarkerPrefix = "gitdir:";

    public static string Resolve(string cwd)
    {
        var start = Path.GetFullPath(cwd);
        var dir = start;
        while (true)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git))
                return dir;
            if (File.Exists(git))
                return ResolveWorktreeRoot(dir, git) ?? dir;
            var parent = Directory.GetParent(dir);
            if (parent is null)
                return start;
            dir = parent.FullName;
        }
    }

    /** worktree 的 .git 文件指向主仓库下的 .git/worktrees/名,其 commondir 文件指回主仓库的 .git 目录。 */
    private static string? ResolveWorktreeRoot(string worktreeDir, string gitFile)
    {
        string marker;
        try
        {
            marker = File.ReadAllText(gitFile).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        if (!marker.StartsWith(GitDirMarkerPrefix, StringComparison.Ordinal))
            return null;
        var gitDir = Path.GetFullPath(marker[GitDirMarkerPrefix.Length..].Trim(), worktreeDir);
        var commondirFile = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commondirFile))
            return null;
        string common;
        try
        {
            common = File.ReadAllText(commondirFile).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        if (common.Length == 0)
            return null;
        var commonDir = Path.GetFullPath(common, gitDir);
        return Directory.GetParent(commonDir)?.FullName;
    }
}
