using System.Reflection;
using PES6Patcher.Core.Patching;

namespace PES6Patcher.Core;

/// <summary>Text report about an executable, for users to share when their build is not supported.</summary>
public static class Diagnostics
{
    public static string Report(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var report = ExePatcher.Analyze(bytes);
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        string YesNo(bool b) => Loc.T(b ? "Common.Yes" : "Common.No");

        var lines = new List<string>
        {
            Loc.T("Report.Title", version),
            Loc.T("Report.File", Path.GetFileName(path), bytes.LongLength),
            Loc.T("Report.Hash", report.Sha256),
            Loc.T("Report.Result", YesNo(report.AlreadyPatched), YesNo(report.Display), YesNo(report.FreeDirection), YesNo(report.CloseDribble), YesNo(report.Validated)),
        };
        lines.AddRange(report.Problems.Select(p => Loc.T("Report.Missing", p)));
        return string.Join(Environment.NewLine, lines);
    }
}
