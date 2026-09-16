namespace Dsh.Core;

/** 文件后端:项目记忆存在一个 markdown 文件里(默认 项目根/.dsh-memory.md)。 */
public sealed class FileMemoryStore(string path) : IMemoryStore
{
    public string Description => path;

    public Task<string?> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : null);

    public Task SetAsync(string text, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
        return Task.CompletedTask;
    }
}
