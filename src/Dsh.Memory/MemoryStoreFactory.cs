using Dsh.Boot;
using Dsh.Core;

namespace Dsh.Memory;

/** 按 settings.yaml 的 memory.backend 选后端:缺省 file(项目根/.dsh-memory.md,worktree 归并到主工作树),`mongo` 时用 Mongo 文档。 */
public static class MemoryStoreFactory
{
    public const string MongoBackend = "mongo";
    public const string DefaultFileName = ".dsh-memory.md";

    public static IMemoryStore Create(MemorySettings? settings, string cwd)
    {
        if (string.Equals(settings?.Backend, MongoBackend, StringComparison.OrdinalIgnoreCase))
        {
            var mongo = settings?.Mongo ?? new MemoryMongoSettings();
            var connection = new MongoMemorySettings(mongo.ConnectionString, mongo.Database, mongo.Collection);
            var key = string.IsNullOrWhiteSpace(mongo.Key) ? "project" : mongo.Key;
            return new MongoTextStore(new MongoMemoryStore(connection), connection, key);
        }

        var configured = settings?.File;
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(ProjectRoot.Resolve(cwd), DefaultFileName)
            : Path.GetFullPath(configured, cwd);
        return new FileMemoryStore(path);
    }
}
