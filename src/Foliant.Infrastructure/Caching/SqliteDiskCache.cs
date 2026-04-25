using Foliant.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Persistent disk cache: файлы в <c>{root}/pages/</c>, метаданные в SQLite (WAL).
/// Атомарная запись через .tmp + Move(overwrite). Concurrent-safe для разных ключей.
/// </summary>
public sealed class SqliteDiskCache : IDiskCache, IAsyncDisposable
{
    private readonly string _root;
    private readonly string _pagesDir;
    private readonly string _connectionString;
    private readonly ILogger<SqliteDiskCache> _log;
    private readonly SemaphoreSlim _writeGate = new(initialCount: 1, maxCount: 1);

    public SqliteDiskCache(string root, ILogger<SqliteDiskCache> log)
    {
        _root = root;
        _pagesDir = Path.Combine(root, "pages");
        _log = log;

        Directory.CreateDirectory(_pagesDir);
        var dbPath = Path.Combine(root, "metadata.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        InitSchema();
    }

    public long CurrentSizeBytes
    {
        get
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(size), 0) FROM entries";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public async Task<byte[]?> TryGetAsync(CacheKey key, CancellationToken ct)
    {
        var fileName = key.ToFileName();
        var path = Path.Combine(_pagesDir, fileName);

        if (!File.Exists(path))
        {
            return null;
        }

        await UpdateAccessTimeAsync(fileName, ct).ConfigureAwait(false);

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // Гонка: запись удалили между Exists и Read.
            return null;
        }
    }

    public async Task PutAsync(CacheKey key, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        var fileName = key.ToFileName();
        var path = Path.Combine(_pagesDir, fileName);
        var tmp = path + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);

        await UpsertEntryAsync(fileName, bytes.Length, key.DocFingerprint, ct).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(CacheKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        var fileName = key.ToFileName();
        var path = Path.Combine(_pagesDir, fileName);
        var existed = File.Exists(path);
        try { File.Delete(path); } catch (IOException) { /* concurrent delete OK */ }

        await DeleteEntryAsync(fileName, ct).ConfigureAwait(false);
        return existed;
    }

    public async Task<int> InvalidateDocumentAsync(string docFingerprint, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            string[] keys;
            using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT key FROM entries WHERE doc_fp = $fp";
                sel.Parameters.AddWithValue("$fp", docFingerprint);
                keys = ReadKeys(sel);
            }

            foreach (var key in keys)
            {
                TryDelete(Path.Combine(_pagesDir, key));
            }

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM entries WHERE doc_fp = $fp";
                del.Parameters.AddWithValue("$fp", docFingerprint);
                del.ExecuteNonQuery();
            }
            tx.Commit();
            return keys.Length;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<int> EvictToTargetAsync(long targetBytes, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var sumCmd = conn.CreateCommand();
            sumCmd.CommandText = "SELECT COALESCE(SUM(size), 0) FROM entries";
            var current = (long)(sumCmd.ExecuteScalar() ?? 0L);
            if (current <= targetBytes)
            {
                return 0;
            }

            var evicted = 0;
            using var sel = conn.CreateCommand();
            sel.CommandText = "SELECT key, size FROM entries ORDER BY last_access ASC";
            using var reader = sel.ExecuteReader();
            var toDelete = new List<(string Key, long Size)>();
            while (reader.Read() && current > targetBytes)
            {
                var key = reader.GetString(0);
                var size = reader.GetInt64(1);
                toDelete.Add((key, size));
                current -= size;
            }
            reader.Close();

            using var tx = conn.BeginTransaction();
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM entries WHERE key = $k";
            var p = del.Parameters.Add("$k", SqliteType.Text);

            foreach (var (key, _) in toDelete)
            {
                TryDelete(Path.Combine(_pagesDir, key));
                p.Value = key;
                del.ExecuteNonQuery();
                evicted++;
            }
            tx.Commit();
            return evicted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM entries";
            cmd.ExecuteNonQuery();

            foreach (var f in Directory.EnumerateFiles(_pagesDir))
            {
                TryDelete(f);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeGate.Dispose();
        SqliteConnection.ClearAllPools();
        return ValueTask.CompletedTask;
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

    private void InitSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS entries (
              key         TEXT PRIMARY KEY,
              size        INTEGER NOT NULL,
              last_access INTEGER NOT NULL,
              doc_fp      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_entries_last_access ON entries(last_access);
            CREATE INDEX IF NOT EXISTS ix_entries_doc_fp      ON entries(doc_fp);
        """;
        cmd.ExecuteNonQuery();
    }

    private async Task UpsertEntryAsync(string fileName, int size, string docFingerprint, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO entries(key, size, last_access, doc_fp) VALUES($k, $sz, $ts, $fp)
                ON CONFLICT(key) DO UPDATE SET size=$sz, last_access=$ts, doc_fp=$fp
            """;
            cmd.Parameters.AddWithValue("$k", fileName);
            cmd.Parameters.AddWithValue("$sz", size);
            cmd.Parameters.AddWithValue("$ts", NowTicks());
            cmd.Parameters.AddWithValue("$fp", docFingerprint);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task UpdateAccessTimeAsync(string fileName, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE entries SET last_access = $ts WHERE key = $k";
            cmd.Parameters.AddWithValue("$ts", NowTicks());
            cmd.Parameters.AddWithValue("$k", fileName);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task DeleteEntryAsync(string fileName, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM entries WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", fileName);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* concurrent delete / locked — best effort */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    private static string[] ReadKeys(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }
        return [.. list];
    }

    private static long NowTicks() => DateTime.UtcNow.Ticks;
}
