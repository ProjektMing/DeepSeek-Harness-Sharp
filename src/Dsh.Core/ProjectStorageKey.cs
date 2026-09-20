using System.Text;

namespace Dsh.Core;

/** 项目级存储键:把项目路径编码成可读且文件系统安全的目录名,供会话日志、检查点等按项目分目录存储。 */
public static class ProjectStorageKey
{
    public const int MaxSlugLength = 251;
    private const string Fallback = "root";

    public static string Of(string cwd)
    {
        if (cwd.Length == 0) throw new ArgumentException("cannot encode an empty project path", nameof(cwd));
        var readable = new StringBuilder(cwd.Length);
        var separatorRun = false;
        foreach (var ch in cwd)
        {
            if (ch is '/' or '\\' or ':')
            {
                if (!separatorRun) readable.Append('-');
                separatorRun = true;
            }
            else if (ch != '~' && IsSafeSegmentChar(ch))
            {
                readable.Append(ch);
                separatorRun = false;
            }
            else
            {
                readable.Append($"~{(int)ch:X4}");
                separatorRun = false;
            }
        }
        var slug = readable.ToString().TrimStart('-');
        if (slug.Length == 0) slug = Fallback;
        return $"--{slug[..Math.Min(MaxSlugLength, slug.Length)]}--";
    }

    public static bool IsSafeSegmentChar(char ch)
        => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
}
