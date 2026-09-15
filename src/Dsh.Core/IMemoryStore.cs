namespace Dsh.Core;

/** 项目记忆存储:后端可以是 markdown 文件或数据库文档,内容是一整段 markdown 文本。 */
public interface IMemoryStore
{
    /** 提示词里描述的来源(文件路径或库表 + key)。 */
    string Description { get; }

    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string text, CancellationToken cancellationToken = default);
}

public static class MemoryServices
{
    public const string Store = "memoryStore";
}
