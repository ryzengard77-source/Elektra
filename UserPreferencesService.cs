/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace Elektra.Configuration;

public sealed class UserPreferencesService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _prefsFilePath;
    private readonly ILogger<UserPreferencesService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly System.Threading.Timer _saveTimer;

    private UserPreferences _current;
    private Task _pendingSave = Task.CompletedTask;
    private bool _disposed;

    public UserPreferences Current
    {
        get
        {
            lock (_stateLock)
                return _current;
        }
    }

    public UserPreferencesService(ILogger<UserPreferencesService> logger)
    {
        _logger = logger;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Elektra");
        Directory.CreateDirectory(dir);
        _prefsFilePath = Path.Combine(dir, "user-preferences.json");
        _current = LoadFromDisk();
        _saveTimer = new System.Threading.Timer(OnSaveTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    private UserPreferences LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_prefsFilePath))
                return new UserPreferences();

            string json = File.ReadAllText(_prefsFilePath);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to read user preferences from {Path}; using defaults.", _prefsFilePath);
            return new UserPreferences();
        }
    }

    public void Update(Func<UserPreferences, UserPreferences> updater, int debounceMs = 500)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _current = updater(_current);
            _saveTimer.Change(Math.Max(0, debounceMs), Timeout.Infinite);
        }
    }

    private void OnSaveTimer(object? state)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _pendingSave = SaveDebouncedAsync(CancellationToken.None);
        }
    }

    private void SaveSnapshotSync(UserPreferences snapshot, string reason)
    {
        _ioLock.Wait();
        string? tempFile = null;
        try
        {
            tempFile = $"{_prefsFilePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempFile, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(tempFile, _prefsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to save user preferences during {Reason}.", reason);
        }
        finally
        {
            TryDeleteTempFile(tempFile);
            _ioLock.Release();
        }
    }

    private async Task SaveDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await _ioLock.WaitAsync(ct);
            try
            {
                UserPreferences snapshot;
                lock (_stateLock)
                {
                    if (_disposed) return;
                    snapshot = _current;
                }

                string? tempFile = null;
                try
                {
                    tempFile = $"{_prefsFilePath}.{Guid.NewGuid():N}.tmp";
                    await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(snapshot, JsonOptions), ct);
                    File.Move(tempFile, _prefsFilePath, overwrite: true);
                }
                finally
                {
                    TryDeleteTempFile(tempFile);
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist user preferences.");
        }
    }

    private void TryDeleteTempFile(string? tempFile)
    {
        if (tempFile == null) return;

        try
        {
            File.Delete(tempFile);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to remove temporary preferences file {Path}.", tempFile);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to remove temporary preferences file {Path}.", tempFile);
        }
    }

    public void Dispose()
    {
        UserPreferences snapshot;
        Task pendingSave;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            snapshot = _current;
            pendingSave = _pendingSave;
        }

        _saveTimer.Dispose();
        pendingSave.GetAwaiter().GetResult();
        SaveSnapshotSync(snapshot, "shutdown");
        _ioLock.Dispose();
    }
}