using Foliant.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Search;

/// <summary>
/// Слой 5 кэша: общий полнотекстовый индекс по всем недавно открытым документам.
/// SQLite FTS5 (unicode61 + remove_diacritics). См. план, разделы 5.1, 6 (S6, S7).
/// </summary>
public sealed class SqliteFtsIndex(string dbPath, ILogger<SqliteFtsIndex> log) : IFtsIndex, IDisposable
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public async Task IndexDocumentAsync(
        string docFingerprint,
        string path,
        IAsyncEnumerable<TextLayer> pages,
        CancellationToken ct)
    {
        EnsureInitialized();
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            // Документ существует? Если да — переиндексация, удалить старые pages.
            var docId = ResolveOrInsertDocument(conn, tx, docFingerprint, path);
            DeletePagesOf(conn, tx, docId);

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO pages_fts(doc_id, page_index, text) VALUES($d, $p, $t)";
            var pD = ins.Parameters.Add("$d", SqliteType.Integer);
            var pP = ins.Parameters.Add("$p", SqliteType.Integer);
            var pT = ins.Parameters.Add("$t", SqliteType.Text);

            var pageCount = 0;
            await foreach (var layer in pages.WithCancellation(ct).ConfigureAwait(false))
            {
                pD.Value = docId;
                pP.Value = layer.PageIndex;
                pT.Value = layer.ToPlainText();
                ins.ExecuteNonQuery();
                pageCount++;
            }

            UpdateDocumentMeta(conn, tx, docId, pageCount);
            tx.Commit();

            log.LogInformation("Проиндексирован {Path}: {PageCount} страниц", path, pageCount);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        EnsureInitialized();
        if (query.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);
        }

        var hits = new List<SearchHit>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT d.fp, d.path, p.page_index,
                   snippet(pages_fts, 2, '[', ']', ' … ', 32),
                   bm25(pages_fts)
            FROM pages_fts AS p
            JOIN documents AS d ON d.id = p.doc_id
            WHERE pages_fts MATCH $q
        """;
        if (query.RestrictToDocFingerprint is not null)
        {
            sql += " AND d.fp = $fp";
        }
        sql += " ORDER BY bm25(pages_fts) ASC LIMIT $lim";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$q", query.Text);
        cmd.Parameters.AddWithValue("$lim", query.MaxResults);
        if (query.RestrictToDocFingerprint is not null)
        {
            cmd.Parameters.AddWithValue("$fp", query.RestrictToDocFingerprint);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new SearchHit(
                DocFingerprint: reader.GetString(0),
                Path: reader.GetString(1),
                PageIndex: reader.GetInt32(2),
                Snippet: reader.GetString(3),
                Rank: reader.GetDouble(4)));
        }
        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    public async Task<bool> RemoveDocumentAsync(string docFingerprint, CancellationToken ct)
    {
        EnsureInitialized();
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            var docId = TryFindDocId(conn, tx, docFingerprint);
            if (docId is null)
            {
                return false;
            }

            DeletePagesOf(conn, tx, docId.Value);

            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM documents WHERE id = $id";
            del.Parameters.AddWithValue("$id", docId.Value);
            del.ExecuteNonQuery();

            tx.Commit();
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<IReadOnlyList<IndexedDocument>> ListAsync(CancellationToken ct)
    {
        EnsureInitialized();
        var list = new List<IndexedDocument>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT fp, path, page_count, last_indexed FROM documents ORDER BY last_indexed DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new IndexedDocument(
                Fingerprint: reader.GetString(0),
                Path: reader.GetString(1),
                PageCount: reader.GetInt32(2),
                LastIndexed: new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero)));
        }
        return Task.FromResult<IReadOnlyList<IndexedDocument>>(list);
    }

    public void Dispose()
    {
        _writeGate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
              id           INTEGER PRIMARY KEY AUTOINCREMENT,
              fp           TEXT    NOT NULL UNIQUE,
              path         TEXT    NOT NULL,
              page_count   INTEGER NOT NULL DEFAULT 0,
              last_indexed INTEGER NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS pages_fts USING fts5(
              doc_id UNINDEXED,
              page_index UNINDEXED,
              text,
              tokenize = 'unicode61 remove_diacritics 2'
            );
        """;
        cmd.ExecuteNonQuery();

        _initialized = true;
    }

    private static long ResolveOrInsertDocument(SqliteConnection conn, SqliteTransaction tx, string fp, string path)
    {
        var existing = TryFindDocId(conn, tx, fp);
        if (existing is not null)
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE documents SET path = $p WHERE id = $id";
            upd.Parameters.AddWithValue("$p", path);
            upd.Parameters.AddWithValue("$id", existing.Value);
            upd.ExecuteNonQuery();
            return existing.Value;
        }

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO documents(fp, path, page_count, last_indexed) VALUES($fp, $p, 0, $ts);
            SELECT last_insert_rowid();
        """;
        ins.Parameters.AddWithValue("$fp", fp);
        ins.Parameters.AddWithValue("$p", path);
        ins.Parameters.AddWithValue("$ts", DateTime.UtcNow.Ticks);
        return (long)ins.ExecuteScalar()!;
    }

    private static long? TryFindDocId(SqliteConnection conn, SqliteTransaction tx, string fp)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM documents WHERE fp = $fp";
        cmd.Parameters.AddWithValue("$fp", fp);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt64(v);
    }

    private static void DeletePagesOf(SqliteConnection conn, SqliteTransaction tx, long docId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM pages_fts WHERE doc_id = $id";
        cmd.Parameters.AddWithValue("$id", docId);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateDocumentMeta(SqliteConnection conn, SqliteTransaction tx, long docId, int pageCount)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE documents SET page_count = $pc, last_indexed = $ts WHERE id = $id";
        cmd.Parameters.AddWithValue("$pc", pageCount);
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.Ticks);
        cmd.Parameters.AddWithValue("$id", docId);
        cmd.ExecuteNonQuery();
    }
}
