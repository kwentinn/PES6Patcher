using System.Globalization;
using System.Resources;

namespace PES6Patcher.Core;

/// <summary>
/// User-facing texts, from Resources/Strings.resx (English) and Strings.&lt;culture&gt;.resx
/// (translations). A new translation only needs a new .resx file: it is listed automatically.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("PES6Patcher.Core.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Text for <paramref name="key"/> in the current UI culture (English if not translated).</summary>
    public static string T(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, T(key), args);

    /// <summary>English plus every culture that has a translation file.</summary>
    public static IReadOnlyList<CultureInfo> Languages { get; } = FindLanguages();

    private static List<CultureInfo> FindLanguages()
    {
        var languages = new List<CultureInfo> { CultureInfo.GetCultureInfo("en") };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.Name.Length == 0 || culture.Name == "en") continue;
            try
            {
                if (Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false) != null) languages.Add(culture);
            }
            catch (CultureNotFoundException) { }
        }
        return languages;
    }
}
