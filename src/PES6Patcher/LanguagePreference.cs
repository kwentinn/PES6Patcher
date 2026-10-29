using System.Globalization;

namespace PES6Patcher;

/// <summary>UI language chosen by the user (empty = follow Windows), kept in %LOCALAPPDATA%\PES6Patcher.</summary>
internal static class LanguagePreference
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PES6Patcher", "language.txt");

    public static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    /// <summary>Saved culture name, or "" for the Windows language.</summary>
    public static string Load()
    {
        try { return File.Exists(File_) ? File.ReadAllText(File_).Trim() : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public static void Save(string cultureName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, cultureName);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Apply(string cultureName)
    {
        var culture = cultureName.Length == 0 ? SystemCulture : CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
