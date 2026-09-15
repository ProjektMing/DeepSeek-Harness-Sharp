using Dsh.Boot;
using Dsh.Core;

namespace Dsh.Memory;

/** 按 settings.yaml 的 memory.backend 选后端:缺省 file,`mongo` 时用 Mongo 文档存整段 markdown。 */
public static class MemoryStoreFactory
{
    public const string MongoBackend = "mongo";

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
            ? Path.Combine(cwd, ".dsh-memory.md")
            : Path.GetFullPath(configured, cwd);
        return new FileMemoryStore(path);
    }
}
