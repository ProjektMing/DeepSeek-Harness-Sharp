using Dsh.Core;

namespace Dsh.Memory;

/** 文件后端:整段 markdown 存在一个文件里(默认 .dsh-memory.md)。 */
public sealed class FileMemoryStore(string path) : IMemoryStore
{
    public string Description => path;

    public Task<string?> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : null);

    public Task SetAsync(string text, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
        return Task.CompletedTask;
    }
}
