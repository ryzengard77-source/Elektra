/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Configuration;
using Elektra.Knowledge.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Elektra.Knowledge.Caching;

public sealed class TwoTierKnowledgeCache : IDisposable
{
    private const int MaxDiskEntries = 10_000;

    private readonly IMemoryCache _ramCache;
    private readonly string _connectionString;
    private readonly TimeSpan _ttl;
    private readonly ILogger<TwoTierKnowledgeCache> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private int _diskCacheAvailable;

    public TwoTierKnowledgeCache(
        IMemoryCache ramCache,
        IOptions<AssistantOptions> options,
        ILogger<TwoTierKnowledgeCache> logger)
    {
        _ramCache = ramCache;
        _logger = logger;
        int configuredTtlHours = options.Value.CacheTtlHours;
        int ttlHours = Math.Clamp(configuredTtlHours, 1, 24 * 365);
        if (configuredTtlHours != ttlHours)
            _logger.LogWarning("CacheTtlHours value {Configured} is out of range; using {Effective} hours.", configuredTtlHours, ttlHours);
        _ttl = TimeSpan.FromHours(ttlHours);

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Elektra");
        _connectionString = $"Data Source={Path.Combine(dir, "knowledge.db")};Pooling=True;";

        try
        {
            Directory.CreateDirectory(dir);
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS KnowledgeCache (
                    QueryHash TEXT PRIMARY KEY,
                    QueryText TEXT NOT NULL,
                    Answer TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    SourceUrl TEXT NULL,
                    ExpiresAtUtc INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_KnowledgeCache_ExpiresAtUtc ON KnowledgeCache (ExpiresAtUtc);
                DELETE FROM KnowledgeCache WHERE ExpiresAtUtc <= $now;
                DELETE FROM KnowledgeCache
                WHERE QueryHash IN (
                    SELECT QueryHash FROM KnowledgeCache
                    ORDER BY ExpiresAtUtc DESC
                    LIMIT -1 OFFSET {MaxDiskEntries}
                );
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();

            using var columns = conn.CreateCommand();
            columns.CommandText = "PRAGMA table_info(KnowledgeCache);";
            using var reader = columns.ExecuteReader();
            bool hasSourceUrl = false;
            while (reader.Read())
                hasSourceUrl |= string.Equals(reader.GetString(1), "SourceUrl", StringComparison.OrdinalIgnoreCase);
            reader.Close();
            if (!hasSourceUrl)
            {
                using var migration = conn.CreateCommand();
                migration.CommandText = "ALTER TABLE KnowledgeCache ADD COLUMN SourceUrl TEXT NULL;";
                migration.ExecuteNonQuery();

                using var invalidateOldWikipediaEntries = conn.CreateCommand();
                invalidateOldWikipediaEntries.CommandText = "DELETE FROM KnowledgeCache WHERE Source LIKE 'Wikipedia%';";
                invalidateOldWikipediaEntries.ExecuteNonQuery();
            }

            Volatile.Write(ref _diskCacheAvailable, 1);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Persistent knowledge cache could not initialize; continuing with the in-memory cache.");
        }
    }

    public async Task<QueryResponse?> GetAsync(string query, CancellationToken ct)
    {
        string key = ComputeHash(query);
        if (_ramCache.TryGetValue(key, out QueryResponse? hit) && hit != null)
            return hit with { IsFromCache = true };
        if (Volatile.Read(ref _diskCacheAvailable) == 0)
            return null;

        await _dbLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Answer, Source, ExpiresAtUtc, SourceUrl FROM KnowledgeCache WHERE QueryHash = $h LIMIT 1;";
            cmd.Parameters.AddWithValue("$h", key);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            if (DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2)) <= DateTimeOffset.UtcNow)
                return null;

            var response = new QueryResponse(
                query,
                r.GetString(0),
                $"{r.GetString(1)} (DiskCache)",
                true,
                r.IsDBNull(3) ? null : r.GetString(3));
            SetRamCache(key, response);
            return response;
        }
        catch (SqliteException ex)
        {
            Volatile.Write(ref _diskCacheAvailable, 0);
            _logger.LogWarning(ex, "Persistent knowledge cache read failed; continuing without the disk cache.");
            return null;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task SetAsync(QueryResponse response, CancellationToken ct)
    {
        string key = ComputeHash(response.Query);
        SetRamCache(key, response);
        if (Volatile.Read(ref _diskCacheAvailable) == 0)
            return;

        await _dbLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                DELETE FROM KnowledgeCache WHERE ExpiresAtUtc <= $now;
                INSERT INTO KnowledgeCache (QueryHash, QueryText, Answer, Source, SourceUrl, ExpiresAtUtc)
                VALUES ($h, $q, $a, $s, $u, $e)
                ON CONFLICT(QueryHash) DO UPDATE SET
                    Answer = excluded.Answer,
                    Source = excluded.Source,
                    SourceUrl = excluded.SourceUrl,
                    ExpiresAtUtc = excluded.ExpiresAtUtc;
                DELETE FROM KnowledgeCache
                WHERE QueryHash IN (
                    SELECT QueryHash FROM KnowledgeCache
                    ORDER BY ExpiresAtUtc DESC
                    LIMIT -1 OFFSET {MaxDiskEntries}
                );
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$h", key);
            cmd.Parameters.AddWithValue("$q", response.Query);
            cmd.Parameters.AddWithValue("$a", response.Answer);
            cmd.Parameters.AddWithValue("$s", response.Source);
            cmd.Parameters.AddWithValue("$u", (object?)response.SourceUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", DateTimeOffset.UtcNow.Add(_ttl).ToUnixTimeSeconds());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex)
        {
            Volatile.Write(ref _diskCacheAvailable, 0);
            _logger.LogWarning(ex, "Persistent knowledge cache write failed; retaining only the in-memory result.");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private void SetRamCache(string key, QueryResponse response) =>
        _ramCache.Set(key, response, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = _ttl < TimeSpan.FromMinutes(30) ? _ttl : TimeSpan.FromMinutes(30)
        });

    private static string ComputeHash(string q) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(q.Trim().ToLowerInvariant())));

    public void Dispose() => _dbLock.Dispose();
}