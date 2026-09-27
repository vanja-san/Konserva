using Konserva.Utilities;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Konserva.Localization;

public static class LocalizationManager
{
    /// <summary>
    /// Версия формата файла локализации.
    /// Повышайте при любом ИЗМЕНЕНИИ структуры файла (конверт/схема JSON).
    /// Изменения самих строк отслеживаются автоматически по хешу источника — версию поднимать не нужно.
    /// </summary>
    public const int LocalizationSchemaVersion = 1;

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new();
    private static readonly ConcurrentDictionary<string, string> _sourceStamps = new();
    private static string _i18nPath = Path.Combine(AppContext.BaseDirectory, "i18n");
    private static CultureInfo _currentCulture = new(CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
    private static readonly Lock _lock = new();
    private static readonly string _systemCultureName = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;

    /// <summary>
    /// Путь к каталогу с файлами локализации. Внутренний сеттер используется только тестами.
    /// </summary>
    internal static string I18nPath
    {
        get => _i18nPath;
        set => _i18nPath = value;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static event Action<string>? LanguageChanged;

    public static CultureInfo CurrentCulture
    {
        get
        {
            lock (_lock) return _currentCulture;
        }
        set
        {
            lock (_lock)
            {
                _currentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                Thread.CurrentThread.CurrentCulture = value;
            }
        }
    }

    public static string[] SupportedCultures => ["ru", "en"];

    public static void Initialize()
    {
        if (!Directory.Exists(_i18nPath))
        {
            Directory.CreateDirectory(_i18nPath);
        }

        foreach (var culture in SupportedCultures)
        {
            var filePath = Path.Combine(_i18nPath, $"{culture}.json");
            if (!File.Exists(filePath))
            {
                CreateDefaultLocalizationFile(filePath, culture);
            }

            LoadCulture(culture);
        }

        Logger.Info("Localization initialized", "LocalizationManager");
    }

    public static void LoadCulture(string culture)
    {
        if (!Directory.Exists(_i18nPath))
            Directory.CreateDirectory(_i18nPath);

        var filePath = Path.Combine(_i18nPath, $"{culture}.json");
        if (RequiresRegeneration(filePath, culture))
        {
            CreateDefaultLocalizationFile(filePath, culture);
        }

        var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        var file = JsonSerializer.Deserialize<LocalizationFile>(json, _jsonOptions);
        if (file?.Translations != null)
        {
            _translations[culture] = file.Translations;
        }
    }

    public static bool TryGetTranslation(string key, out string value)
    {
        value = key;

        // Используем TwoLetterISOLanguageName ("ru", "en") вместо Name ("ru-RU", "en-US"),
        // чтобы корректно работать с региональными вариантами
        var cultureKey = CurrentCulture.TwoLetterISOLanguageName;

        if (_translations.TryGetValue(cultureKey, out var translations) && translations.TryGetValue(key, out var translatedValue))
        {
            value = translatedValue;
            return true;
        }

        if (_translations.TryGetValue("en", out var enTranslations) && enTranslations.TryGetValue(key, out var enValue))
        {
            value = enValue;
            return true;
        }

        return false;
    }

    public static string Get(string key)
    {
        if (TryGetTranslation(key, out var value))
        {
            return value;
        }

        var cultureKey = CurrentCulture.TwoLetterISOLanguageName;
        var defaultTranslations = GetDefaultTranslationsForCulture(cultureKey);
        if (defaultTranslations != null && defaultTranslations.TryGetValue(key, out var defaultValue))
        {
            return defaultValue;
        }

        var enTranslations = GetDefaultTranslationsForCulture("en");
        if (enTranslations != null && enTranslations.TryGetValue(key, out var enValue))
        {
            return enValue;
        }

        return key;
    }

    public static string Get(string key, params object[] args)
    {
        var format = Get(key);
        return string.Format(format, args);
    }

    public static void SetLanguage(string culture)
    {
        string actualCulture = culture;

        if (culture == "System")
        {
            // Используем сохранённую при старте системную культуру, а не CurrentUICulture
            // (которая уже могла быть изменена предыдущим вызовом SetLanguage)
            actualCulture = _systemCultureName == "ru" ? "ru" : "en";
        }
        else if (!SupportedCultures.Contains(culture))
        {
            Logger.Warning($"Unsupported language: {culture}, falling back to 'ru'", "LocalizationManager");
            actualCulture = "ru";
        }

        var cultureInfo = new CultureInfo(actualCulture);
        CurrentCulture = cultureInfo;
        LoadCulture(actualCulture);

        // Оповещаем Binding-источник — все XAML-привязки обновятся автоматически
        LocalizationResource.Instance.NotifyLanguageChanged();

        LanguageChanged?.Invoke(actualCulture);

        Logger.Info($"Language changed to: {actualCulture}", "LocalizationManager");
    }

    public static bool HasKey(string key)
    {
        if (_translations.TryGetValue(CurrentCulture.TwoLetterISOLanguageName, out var translations))
        {
            return translations.ContainsKey(key);
        }
        return false;
    }

    public static IEnumerable<string> GetAllKeys()
    {
        if (_translations.TryGetValue(CurrentCulture.TwoLetterISOLanguageName, out var translations))
        {
            return translations.Keys;
        }
        return Enumerable.Empty<string>();
    }

    private static void CreateDefaultLocalizationFile(string filePath, string culture)
    {
        var file = new LocalizationFile
        {
            SchemaVersion = LocalizationSchemaVersion,
            SourceStamp = GetOrComputeSourceStamp(culture),
            Translations = GetDefaultTranslations(culture)
        };
        var json = JsonSerializer.Serialize(file, _jsonOptions);
        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);

        Logger.Info($"Created default localization file for: {culture}", "LocalizationManager");
    }

    /// <summary>
    /// Определяет, нужно ли перегенерировать файл локализации.
    /// Файл перезаписывается, если: отсутствует, имеет старый формат/схему
    /// или устарел относительно текущих строк в исходниках (сравнение по хешу).
    /// </summary>
    private static bool RequiresRegeneration(string filePath, string culture)
    {
        if (!File.Exists(filePath))
            return true;

        try
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var file = JsonSerializer.Deserialize<LocalizationFile>(json, _jsonOptions);

            if (file?.Translations == null)
                return true;

            if (file.SchemaVersion < LocalizationSchemaVersion)
                return true;

            var currentStamp = GetOrComputeSourceStamp(culture);
            return file.SourceStamp != currentStamp;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Хеш текущего содержания словаря переводов в коде.
    /// Меняется при любом изменении значений в RussianStrings/EnglishStrings,
    /// поэтому файлы локализации автоматически обновляются при каждом запуске.
    /// </summary>
    private static string GetOrComputeSourceStamp(string culture)
    {
        return _sourceStamps.GetOrAdd(culture, ComputeSourceStamp);
    }

    private static string ComputeSourceStamp(string culture)
    {
        var translations = GetDefaultTranslations(culture);
        var sb = new StringBuilder();
        foreach (var kv in translations.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append('\u0001').Append(kv.Value ?? string.Empty).Append('\u0002');
        }
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed class LocalizationFile
    {
        public int SchemaVersion { get; set; }
        public string? SourceStamp { get; set; }
        public Dictionary<string, string>? Translations { get; set; }
    }

    public static Dictionary<string, string>? GetDefaultTranslationsForCulture(string culture)
    {
        return GetDefaultTranslations(culture);
    }

    private static Dictionary<string, string> GetDefaultTranslations(string culture)
    {
        return culture switch
        {
            "ru" => RussianStrings.Translations,
            "en" => EnglishStrings.Translations,
            _ => new Dictionary<string, string>()
        };
    }
}

/// <summary>
/// Синглтон-источник для динамических привязок локализации.
/// При смене языка вызывает PropertyChanged, что обновляет все привязанные элементы UI.
/// </summary>
public class LocalizationResource : INotifyPropertyChanged
{
    public static LocalizationResource Instance { get; } = new();
    private LocalizationResource() { }

    /// <summary>
    /// Свойство-триггер — меняется при каждом переключении языка,
    /// заставляя все Binding'и пересчитаться.
    /// </summary>
    public string Culture => LocalizationManager.CurrentCulture.TwoLetterISOLanguageName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
    }
}

/// <summary>
/// Конвертер для Binding-локализации: на входе значение Culture, на выходе — перевод по ключу.
/// </summary>
public class LocValueConverter : IValueConverter
{
    private readonly string _key;

    public LocValueConverter(string key)
    {
        _key = key;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return LocalizationManager.Get(_key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return Key;

        // Создаём Binding, который обновляется при смене языка.
        // WPF сам применит его к целевому свойству в любом контексте:
        // как напрямую, так и внутри DataTemplate/Style.
        var binding = new Binding
        {
            Source = LocalizationResource.Instance,
            Path = new PropertyPath(nameof(LocalizationResource.Culture)),
            Converter = new LocValueConverter(Key),
            Mode = BindingMode.OneWay
        };

        // Binding реализует MarkupExtension — делегируем ему создание выражения.
        // Это стандартный способ вернуть Binding из кастомного MarkupExtension.
        return binding.ProvideValue(serviceProvider);
    }
}
