using Dsh.Core;

namespace Dsh.SessionQuery;

/** 会话全文检索的门面:优先 SQLite FTS5,不可用时退化为本地文本匹配(插件激活不受依赖缺失影响)。 */
public sealed class SessionQueryIndex : IDisposable
{
    private readonly ISessionTextSearch _search;

    public SessionQueryIndex(string databasePath = ":memory:")
        => _search = SessionTextSearchFactory.Create(databasePath);

    /** 当前是否走 SQLite FTS5;false 表示退化成本地匹配,仅用于诊断。 */
    public bool UsesFullText => _search is FtsSessionTextSearch;

    public void IndexSession(Session session) => _search.IndexSession(session);

    public IReadOnlyList<SessionQueryHit> Search(string query, int limit = 20) => _search.Search(query, limit);

    public void Dispose() => _search.Dispose();
}
