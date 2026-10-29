using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PES6Patcher.Core;

namespace PES6Patcher.Tests;

/// <summary>Checks every translation file against the English one (keys and {n} placeholders).</summary>
public class LocalizationTests
{
    private static readonly string ResourcesDir = FindResources();

    private static string FindResources()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PES6Patcher.Core", "Resources");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/PES6Patcher.Core/Resources");
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Order());

    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ResourcesDir, "Strings.*.resx")) data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_has_the_same_keys_and_placeholders_as_English(string file)
    {
        var english = Read(Path.Combine(ResourcesDir, "Strings.resx"));
        var translated = Read(Path.Combine(ResourcesDir, file));

        Assert.Empty(english.Keys.Except(translated.Keys));    // missing
        Assert.Empty(translated.Keys.Except(english.Keys));    // unknown
        foreach (var (key, text) in english)
            Assert.True(Placeholders(text) == Placeholders(translated[key]), $"{file} : paramètres différents pour '{key}'");
    }

    [Fact]
    public void Every_translation_file_is_offered_as_a_language()
    {
        var offered = Loc.Languages.Select(c => c.Name).ToHashSet();
        Assert.Contains("en", offered);
        foreach (var file in Directory.GetFiles(ResourcesDir, "Strings.*.resx"))
            Assert.Contains(Path.GetFileName(file).Split('.')[1], offered);
    }

    [Fact]
    public void Texts_follow_the_UI_culture()
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Apply", Loc.T("Action.Apply"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Appliquer", Loc.T("Action.Apply"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");   // no translation: English
            Assert.Equal("Apply", Loc.T("Action.Apply"));
        }
        finally { CultureInfo.CurrentUICulture = saved; }
    }
}
