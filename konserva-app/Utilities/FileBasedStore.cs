using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Konserva.Utilities;

/// <summary>
/// Generic file-based data store with synchronous and asynchronous access.
/// Provides thread-safe reads/writes with JSON serialization.
/// </summary>
public class FileBasedStore<T> where T : class
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private T? _cached;
    private long _lastFileSize;
    private DateTime _lastWriteTime;

    /// <summary>
    /// Creates a store for the specified file
    /// </summary>
    public FileBasedStore(string filePath, JsonSerializerOptions? jsonOptions = null)
    {
        _filePath = filePath;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true
        };
    }

    /// <summary>
    /// Load data from the file (synchronously)
    /// </summary>
    public T? Load()
    {
        _lock.Wait();
        try
        {
            if (_cached != null && !IsFileModified())
                return _cached;

            _cached = LoadFromFile();
            UpdateFileStats();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Load data from the file (asynchronously)
    /// </summary>
    public async Task<T?> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null && !IsFileModified())
                return _cached;

            var data = await LoadFromFileAsync(ct);
            _cached = data;
            UpdateFileStats();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Save data to the file (synchronously)
    /// </summary>
    public void Save(T data)
    {
        _lock.Wait();
        try
        {
            _cached = data;
            SaveToFile(data);
            UpdateFileStats();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Save data to the file (asynchronously)
    /// </summary>
    public async Task SaveAsync(T data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _cached = data;
            await SaveToFileAsync(data, ct);
            UpdateFileStats();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Access to the cached data without locking
    /// </summary>
    public T? PeekCache()
    {
        _lock.Wait();
        try
        {
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Perform an action on the data and persist the result
    /// </summary>
    public void Update(Action<T> updateAction)
    {
        _lock.Wait();
        try
        {
            if (_cached == null)
            {
                _cached = LoadFromFile();
                UpdateFileStats();
            }

            if (_cached == null)
            {
                Logger.Warning($"Cannot update {typeof(T).Name}: no data loaded", "FileBasedStore");
                return;
            }

            updateAction(_cached);
            SaveToFile(_cached);
            UpdateFileStats();
        }
        finally
        {
            _lock.Release();
        }
    }

    private T? LoadFromFile()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load {typeof(T).Name} from {_filePath}: {ex.Message}", ex, "FileBasedStore");

            // Falling back to an empty result here would make the caller believe the data is
            // simply gone. Try the last-known-good copy before giving up.
            return LoadFromBackup();
        }
    }

    /// <summary>
    /// Restores the previous good copy of the file (kept as <c>&lt;name&gt;.bak</c>).
    /// </summary>
    private T? LoadFromBackup()
    {
        var backupPath = _filePath + ".bak";
        if (!File.Exists(backupPath))
            return null;

        try
        {
            using var fileStream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var data = JsonSerializer.Deserialize<T>(reader.ReadToEnd(), _jsonOptions);
            Logger.Warning($"Recovered {typeof(T).Name} from backup: {backupPath}", "FileBasedStore");
            return data;
        }
        catch (Exception ex)
        {
            Logger.Error($"Backup {backupPath} is unreadable too: {ex.Message}", ex, "FileBasedStore");
            return null;
        }
    }

    private async Task<T?> LoadFromFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            await using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var reader = new StreamReader(fileStream);
            var json = await reader.ReadToEndAsync(ct);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load {typeof(T).Name} from {_filePath}: {ex.Message}", "FileBasedStore");
            return null;
        }
    }

    /// <summary>
    /// Writes the data atomically: a temporary file next to the target is written and then
    /// moved into place. <see cref="FileMode.Create"/> would truncate the target first, so a
    /// crash or a full disk mid-write left a truncated file that deserialized to nothing —
    /// silently emptying the whole server list.
    /// </summary>
    private void SaveToFile(T data)
    {
        var tempPath = _filePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fileStream))
            {
                writer.Write(json);
            }

            // Keep the previous good copy as a recovery point before replacing it.
            TryBackupCurrent();
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save {typeof(T).Name} to {_filePath}: {ex.Message}", ex, "FileBasedStore");
            TryDeleteTemp(tempPath);
        }
    }

    /// <inheritdoc cref="SaveToFile"/>
    private async Task SaveToFileAsync(T data, CancellationToken ct)
    {
        var tempPath = _filePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(fileStream))
            {
                await writer.WriteAsync(json.AsMemory(), ct);
            }

            // Keep the previous good copy as a recovery point before replacing it.
            TryBackupCurrent();
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save {typeof(T).Name} to {_filePath}: {ex.Message}", ex, "FileBasedStore");
            TryDeleteTemp(tempPath);
        }
    }

    /// <summary>
    /// Copies the current file aside as <c>&lt;name&gt;.bak</c> so a corrupted write
    /// can be recovered on the next load. Skipped when the current file is unreadable —
    /// a bad copy would only replace one broken source with another.
    /// </summary>
    private void TryBackupCurrent()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            File.Copy(_filePath, _filePath + ".bak", overwrite: true);
        }
        catch
        {
            // A missing backup is not a failure of the save itself.
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Leftover temp files are harmless — the next write overwrites them.
        }
    }

    private bool IsFileModified()
    {
        try
        {
            var fileInfo = new FileInfo(_filePath);
            return fileInfo.Exists &&
                   (fileInfo.LastWriteTimeUtc != _lastWriteTime ||
                    fileInfo.Length != _lastFileSize);
        }
        catch
        {
            return true;
        }
    }

    private void UpdateFileStats()
    {
        try
        {
            var fileInfo = new FileInfo(_filePath);
            if (fileInfo.Exists)
            {
                _lastWriteTime = fileInfo.LastWriteTimeUtc;
                _lastFileSize = fileInfo.Length;
            }
        }
        catch
        {
            // ignore
        }
    }
}
