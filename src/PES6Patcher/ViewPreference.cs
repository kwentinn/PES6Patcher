namespace PES6Patcher;

/// <summary>Whether the gameplay options show in detail (otherwise the simple mode, with the presets), kept in %LOCALAPPDATA%\PES6Patcher.</summary>
internal static class ViewPreference
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PES6Patcher", "view.txt");

    public static bool LoadDetailed()
    {
        try { return File.Exists(File_) && File.ReadAllText(File_).Trim() == "detailed"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void SaveDetailed(bool detailed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, detailed ? "detailed" : "simple");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
