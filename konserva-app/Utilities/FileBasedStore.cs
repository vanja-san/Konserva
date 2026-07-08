using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Konserva.Utilities;

/// <summary>
/// Универсальное хранилище данных на основе файла с синхронным и асинхронным доступом.
/// Предоставляет потокобезопасное чтение/запись с JSON-сериализацией.
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
    /// Создаёт хранилище для указанного файла
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
    /// Загрузить данные из файла (синхронно)
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
    /// Загрузить данные из файла (асинхронно)
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
    /// Сохранить данные в файл (синхронно)
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
    /// Сохранить данные в файл (асинхронно)
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
    /// Доступ к кэшированным данным без блокировки
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
    /// Выполнить действие над данными с сохранением
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
            Logger.Warning($"Failed to load {typeof(T).Name} from {_filePath}: {ex.Message}", "FileBasedStore");
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

    private void SaveToFile(T data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using var fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fileStream);
            writer.Write(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save {typeof(T).Name} to {_filePath}: {ex.Message}", ex, "FileBasedStore");
        }
    }

    private async Task SaveToFileAsync(T data, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await using var fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var writer = new StreamWriter(fileStream);
            await writer.WriteAsync(json.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save {typeof(T).Name} to {_filePath}: {ex.Message}", ex, "FileBasedStore");
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
