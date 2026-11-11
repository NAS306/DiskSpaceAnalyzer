using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.Win32;

namespace DiskSpaceAnalyzer.Core;

public sealed class Localization : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("DiskSpaceAnalyzer.Resources.Strings", typeof(Localization).Assembly);
    public static Localization Instance { get; } = new();
    public static string Language { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;
    public string this[string key] => T(key);
    public static string T(string key) => Resources.GetString(key, CultureInfo.GetCultureInfo(Language)) ?? throw new MissingManifestResourceException(key);
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    public static void Initialize(string? requested = null)
    {
        string? saved = null;
        if (OperatingSystem.IsWindows())
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\DiskSpaceAnalyzer"); saved = key?.GetValue("Language") as string; }
            catch (System.Security.SecurityException) { }
        }
        SetLanguage(requested is "ko" or "en" ? requested : saved is "ko" or "en" ? saved : Language, false);
    }
    public static void SetLanguage(string language, bool persist = true)
    {
        if (language is not ("ko" or "en")) throw new ArgumentException("Supported languages: ko, en", nameof(language));
        Language = language;
        var culture = CultureInfo.GetCultureInfo(language == "ko" ? "ko-KR" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        if (persist && OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\DiskSpaceAnalyzer");
            key.SetValue("Language", language);
        }
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }
}
