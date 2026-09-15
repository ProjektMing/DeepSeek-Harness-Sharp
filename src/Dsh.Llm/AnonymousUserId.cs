namespace Dsh.Llm;

/** 客户端匿名标识:落盘在 home 下,供适配器上报用户维度统计。 */
public static class AnonymousUserId
{
    private const string FileName = ".anonymous-user-id";

    public static string Resolve(string homeRoot)
    {
        if (string.IsNullOrWhiteSpace(homeRoot))
            return Guid.NewGuid().ToString();
        var path = Path.Combine(homeRoot, FileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Guid.TryParse(existing, out _))
                return existing;
        }
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(path, id + '\n');
        return id;
    }
}
