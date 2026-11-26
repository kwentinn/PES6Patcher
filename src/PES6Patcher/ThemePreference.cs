namespace PES6Patcher;

/// <summary>Colour theme chosen by the user ("" = follow Windows, "light" or "dark"), kept in %LOCALAPPDATA%\PES6Patcher.</summary>
internal static class ThemePreference
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PES6Patcher", "theme.txt");

    public static string Load()
    {
        try { return File.Exists(File_) ? File.ReadAllText(File_).Trim() : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public static void Save(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, theme);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Sets the colours of the windows created from now on.</summary>
    public static void Apply(string theme) =>
        Application.SetColorMode(theme switch { "dark" => SystemColorMode.Dark, "light" => SystemColorMode.Classic, _ => SystemColorMode.System });
}
