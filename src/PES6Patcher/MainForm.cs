using System.Globalization;
using PES6Patcher.Core;
using PES6Patcher.Core.Config;

namespace PES6Patcher;

/// <summary>Single window: language, game folder, display, gameplay options, apply / restore, log.</summary>
internal sealed class MainForm : Form
{
    /// <summary>What the user entered, carried over when the window is rebuilt in another language.</summary>
    internal sealed record State(string Folder, string? Exe, decimal Width, decimal Height, bool Borderless, bool FreeDirection, decimal Speed, string Log);

    /// <summary>Set when the language changed: the window must be rebuilt with these values.</summary>
    public State? RebuildWith { get; private set; }

    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _exe = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Button _report = new() { Text = Loc.T("Action.CopyReport"), AutoSize = true };
    private readonly Label _exeState = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly NumericUpDown _width = new() { Minimum = 640, Maximum = 10240, Increment = 10, Width = 80 };
    private readonly NumericUpDown _height = new() { Minimum = 480, Maximum = 4320, Increment = 10, Width = 80 };
    private readonly Label _formatInfo = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly CheckBox _borderless = new() { Text = Loc.T("Display.Borderless"), AutoSize = true, Checked = true };
    private readonly CheckBox _freeDirection = new() { Text = Loc.T("Gameplay.FreeDirection"), AutoSize = true, Checked = true };
    private readonly NumericUpDown _speed = new() { Minimum = 0.5m, Maximum = 1.5m, Increment = 0.05m, DecimalPlaces = 2, Value = 1.0m, Width = 70 };
    private readonly Button _apply = new() { Text = Loc.T("Action.Apply"), AutoSize = true };
    private readonly Button _restore = new() { Text = Loc.T("Action.Restore"), AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    public MainForm(State? previous = null)
    {
        Text = "PES6 Patcher";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 590);
        MinimumSize = new Size(640, 500);

        var browse = new Button { Text = Loc.T("Folder.Browse"), AutoSize = true };
        browse.Click += (_, _) => BrowseFolder();
        var screen = new Button { Text = Loc.T("Display.ScreenResolution"), AutoSize = true };
        screen.Click += (_, _) => UseScreenResolution();
        _width.ValueChanged += (_, _) => UpdateFormatInfo();
        _height.ValueChanged += (_, _) => UpdateFormatInfo();
        _folder.Leave += (_, _) => { FillExecutables(null); RefreshState(log: true); };
        _exe.SelectionChangeCommitted += (_, _) => RefreshState(log: true);
        _report.Click += (_, _) => CopyReport();
        _apply.Click += (_, _) => Run(game => Installer.Apply(game, CurrentSettings(), Log));
        _restore.Click += (_, _) => Run(game => Installer.Restore(game, Log));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = Loc.T("Folder.Title"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 3, 3) });
        header.Controls.Add(LabelText(Loc.T("Language.Label")));
        header.Controls.Add(_language);

        var folderRow = Row(_folder, browse);
        folderRow.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        var exeRow = Flow(LabelText(Loc.T("Folder.Exe")), _exe, _report);

        var display = Group(Loc.T("Display.Group"),
            Flow(LabelText(Loc.T("Display.Resolution")), _width, LabelText("×"), _height, screen),
            _formatInfo,
            _borderless);
        var gameplay = Group(Loc.T("Gameplay.Group"),
            _freeDirection,
            Flow(LabelText(Loc.T("Gameplay.Speed")), _speed, LabelText(Loc.T("Gameplay.SpeedHint"))));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        layout.Controls.Add(header);
        layout.Controls.Add(folderRow);
        layout.Controls.Add(exeRow);
        layout.Controls.Add(_exeState);
        layout.Controls.Add(display);
        layout.Controls.Add(gameplay);
        layout.Controls.Add(Flow(_apply, _restore));
        layout.Controls.Add(_log);
        for (int i = 0; i < layout.Controls.Count - 1; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        FillLanguages();
        ActiveControl = _folder;

        if (previous == null)
        {
            _folder.Text = GameInstall.FindFolder() ?? "";
            FillExecutables(null);
            UseScreenResolution();
            RefreshState(log: true);
        }
        else
        {
            _folder.Text = previous.Folder;
            FillExecutables(previous.Exe);
            _width.Value = previous.Width;
            _height.Value = previous.Height;
            _borderless.Checked = previous.Borderless;
            _freeDirection.Checked = previous.FreeDirection;
            _log.Text = previous.Log;
            RefreshState(log: false);
            _speed.Value = previous.Speed;   // keep the user's value over the one read from speeder.cfg
            UpdateFormatInfo();
        }
    }

    private void FillLanguages()
    {
        string saved = LanguagePreference.Load();
        _language.Items.Add(new LanguageItem("", Loc.T("Language.System", Capitalize(LanguagePreference.SystemCulture.NativeName))));
        foreach (var culture in Loc.Languages)
            _language.Items.Add(new LanguageItem(culture.Name, Capitalize(culture.NativeName)));
        _language.SelectedItem = _language.Items.Cast<LanguageItem>().FirstOrDefault(i => i.Name == saved) ?? _language.Items[0];
        // Only a choice made by the user: WinForms also changes the selection by itself (e.g. on activation).
        _language.SelectionChangeCommitted += (_, _) => ChangeLanguage();
    }

    private void ChangeLanguage()
    {
        var item = (LanguageItem)_language.SelectedItem!;
        if (item.Name == LanguagePreference.Load()) return;
        LanguagePreference.Save(item.Name);
        LanguagePreference.Apply(item.Name);
        RebuildWith = new State(_folder.Text, _exe.SelectedItem as string, _width.Value, _height.Value, _borderless.Checked, _freeDirection.Checked, _speed.Value, _log.Text);
        Close();
    }

    private InstallSettings CurrentSettings() =>
        new((int)_width.Value, (int)_height.Value, _borderless.Checked, _freeDirection.Enabled && _freeDirection.Checked, (double)_speed.Value);

    private void UseScreenResolution()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _width.Value = Math.Clamp(bounds.Width, (int)_width.Minimum, (int)_width.Maximum);
        _height.Value = Math.Clamp(bounds.Height, (int)_height.Minimum, (int)_height.Maximum);
        UpdateFormatInfo();
    }

    private void UpdateFormatInfo()
    {
        var s = CurrentSettings();
        _formatInfo.Text = Loc.T("Display.FormatInfo", s.Aspect, 2 * s.Half);
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = Loc.T("Folder.BrowseDescription"), SelectedPath = _folder.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _folder.Text = dialog.SelectedPath;
        FillExecutables(null);
        RefreshState(log: true);
    }

    /// <summary>Lists the game executables of the folder, keeping <paramref name="preferred"/> selected when present.</summary>
    private void FillExecutables(string? preferred)
    {
        _exe.Items.Clear();
        foreach (var name in GameInstall.FindExecutables(_folder.Text.Trim())) _exe.Items.Add(name);
        if (_exe.Items.Count == 0) return;
        _exe.SelectedItem = _exe.Items.Cast<string>().FirstOrDefault(n => n == preferred) ?? _exe.Items[0];
    }

    /// <summary>Shows the state of the selected executable and pre-fills the gameplay options.</summary>
    private void RefreshState(bool log)
    {
        var game = SelectedGame();
        _report.Enabled = game != null && File.Exists(game.Exe);
        if (game == null)
        {
            _exeState.Text = Loc.T("State.NotFound");
            _apply.Enabled = _restore.Enabled = false;
            return;
        }

        var (state, report) = game.Inspect();
        string exe = Path.GetFileName(game.Exe), backup = Path.GetFileName(game.ExeBackup);
        _exeState.Text = state switch
        {
            ExeState.Original when report!.Validated => Loc.T("State.Validated", exe),
            ExeState.Original => Loc.T("State.Compatible", exe),
            ExeState.Patched => Loc.T("State.Patched", exe, backup),
            ExeState.PatchedWithoutBackup => Loc.T("State.PatchedWithoutBackup", exe, backup),
            ExeState.Missing => Loc.T("State.NotFound"),
            _ => Loc.T("State.Unsupported", exe),
        };
        _apply.Enabled = state is ExeState.Original or ExeState.Patched;
        _restore.Enabled = File.Exists(game.ExeBackup) || game.ConfigFiles.Any(f => File.Exists(f + ".orig"));

        bool freeDirection = report is { FreeDirection: true };
        _freeDirection.Enabled = freeDirection;
        _freeDirection.Text = Loc.T("Gameplay.FreeDirection") + (freeDirection || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));

        if (File.Exists(game.Speeder) && SpeederConfig.Read(File.ReadAllText(game.Speeder)) is double speed)
            _speed.Value = Math.Clamp((decimal)speed, _speed.Minimum, _speed.Maximum);
        if (log)
        {
            Log(Loc.T("Log.Game", game.Folder));
            if (!game.HasKitserver) Log(Loc.T("Log.NoKitserver"));
        }
    }

    private GameInstall? SelectedGame()
    {
        var folder = _folder.Text.Trim();
        return folder.Length > 0 && _exe.SelectedItem is string exe && File.Exists(Path.Combine(folder, exe)) ? new GameInstall(folder, exe) : null;
    }

    /// <summary>Puts a report on the selected executable (and its backup) in the clipboard, for support.</summary>
    private void CopyReport()
    {
        var game = SelectedGame();
        if (game == null) return;
        var text = Diagnostics.Report(game.Exe);
        if (File.Exists(game.ExeBackup)) text += Environment.NewLine + Environment.NewLine + Diagnostics.Report(game.ExeBackup);
        Clipboard.SetText(text);
        Log(text);
        Log(Loc.T("Log.ReportCopied"));
    }

    private void Run(Action<GameInstall> action)
    {
        var game = SelectedGame();
        if (game == null) return;
        UseWaitCursor = true;
        _apply.Enabled = _restore.Enabled = false;
        try
        {
            action(game);
        }
        catch (Exception ex)
        {
            Log(Loc.T("Log.Error", ex.Message));
            MessageBox.Show(this, ex.Message, "PES6 Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshState(log: false);
        }
    }

    private void Log(string message) => _log.AppendText(message + Environment.NewLine);

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    private static Label LabelText(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    private static GroupBox Group(string title, params Control[] rows)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.AddRange(rows);
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        box.Controls.Add(panel);
        return box;
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static TableLayoutPanel Row(params Control[] controls)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = controls.Length, Margin = new Padding(0) };
        foreach (var c in controls) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.AddRange(controls);
        return row;
    }

    private sealed record LanguageItem(string Name, string Display)
    {
        public override string ToString() => Display;
    }
}
