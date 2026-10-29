using PES6Patcher.Core;
using PES6Patcher.Core.Config;

namespace PES6Patcher;

/// <summary>Single window: game folder, display, gameplay options, apply / restore, log.</summary>
internal sealed class MainForm : Form
{
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly Label _exeState = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly NumericUpDown _width = new() { Minimum = 640, Maximum = 10240, Increment = 10, Width = 80 };
    private readonly NumericUpDown _height = new() { Minimum = 480, Maximum = 4320, Increment = 10, Width = 80 };
    private readonly Label _formatInfo = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly CheckBox _borderless = new() { Text = "Fenêtre sans bordure couvrant l'écran (nécessaire au-delà de 1920 × 1080)", AutoSize = true, Checked = true };
    private readonly CheckBox _freeDirection = new() { Text = "Directions libres au stick analogique", AutoSize = true, Checked = true };
    private readonly NumericUpDown _speed = new() { Minimum = 0.5m, Maximum = 1.5m, Increment = 0.05m, DecimalPlaces = 2, Value = 1.0m, Width = 70 };
    private readonly Button _apply = new() { Text = "Appliquer", AutoSize = true };
    private readonly Button _restore = new() { Text = "Restaurer l'original", AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "PES6 Patcher";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(640, 480);

        var browse = new Button { Text = "Parcourir…", AutoSize = true };
        browse.Click += (_, _) => BrowseFolder();
        var screen = new Button { Text = "Résolution de l'écran", AutoSize = true };
        screen.Click += (_, _) => UseScreenResolution();
        _width.ValueChanged += (_, _) => UpdateFormatInfo();
        _height.ValueChanged += (_, _) => UpdateFormatInfo();
        _folder.Leave += (_, _) => RefreshState(log: true);
        _apply.Click += (_, _) => Run(game => Installer.Apply(game, CurrentSettings(), Log));
        _restore.Click += (_, _) => Run(game => Installer.Restore(game, Log));

        var folderRow = Row(_folder, browse);
        folderRow.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);

        var display = Group("Affichage",
            Flow(new Label { Text = "Résolution :", AutoSize = true, Margin = new Padding(3, 6, 3, 3) },
                 _width, new Label { Text = "×", AutoSize = true, Margin = new Padding(3, 6, 3, 3) }, _height, screen),
            _formatInfo,
            _borderless);
        var gameplay = Group("Gameplay",
            _freeDirection,
            Flow(new Label { Text = "Vitesse du jeu :", AutoSize = true, Margin = new Padding(3, 6, 3, 3) }, _speed,
                 new Label { Text = "(1,00 = vitesse d'origine)", AutoSize = true, Margin = new Padding(3, 6, 3, 3) }));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        layout.Controls.Add(new Label { Text = "Dossier du jeu", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        layout.Controls.Add(folderRow);
        layout.Controls.Add(_exeState);
        layout.Controls.Add(display);
        layout.Controls.Add(gameplay);
        layout.Controls.Add(Flow(_apply, _restore));
        layout.Controls.Add(_log);
        for (int i = 0; i < layout.Controls.Count - 1; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        _folder.Text = GameInstall.FindFolder() ?? "";
        UseScreenResolution();
        RefreshState(log: true);
    }

    private InstallSettings CurrentSettings() =>
        new((int)_width.Value, (int)_height.Value, _borderless.Checked, _freeDirection.Checked, (double)_speed.Value);

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
        _formatInfo.Text = $"Format {s.Aspect:0.###}:1  —  repère des menus {2 * s.Half} × 448 (d'origine 512 × 448)";
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Dossier contenant PES6.exe", SelectedPath = _folder.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _folder.Text = dialog.SelectedPath;
        RefreshState(log: true);
    }

    /// <summary>Shows the state of the selected installation and pre-fills the gameplay options.</summary>
    private void RefreshState(bool log)
    {
        var game = SelectedGame();
        if (game == null)
        {
            _exeState.Text = "PES6.exe introuvable dans ce dossier.";
            _apply.Enabled = _restore.Enabled = false;
            return;
        }

        var state = game.GetExeState();
        _exeState.Text = state switch
        {
            ExeState.Original => "PES6.exe : version d'origine, prise en charge.",
            ExeState.Patched => "PES6.exe : déjà patché (l'original est sauvegardé dans PES6.exe.orig).",
            _ => "PES6.exe : version non prise en charge. Ce patch ne fonctionne qu'avec une version précise de l'exe.",
        };
        _apply.Enabled = state is ExeState.Original or ExeState.Patched;
        _restore.Enabled = File.Exists(game.ExeBackup) || game.ConfigFiles.Any(f => File.Exists(f + ".orig"));

        if (File.Exists(game.Speeder) && SpeederConfig.Read(File.ReadAllText(game.Speeder)) is double speed)
            _speed.Value = Math.Clamp((decimal)speed, _speed.Minimum, _speed.Maximum);
        if (log)
        {
            Log($"Jeu : {game.Folder}");
            if (!game.HasKitserver) Log("Attention : Kitserver est introuvable (kitserver\\kload.cfg). Il est nécessaire.");
        }
    }

    private GameInstall? SelectedGame()
    {
        var folder = _folder.Text.Trim();
        return folder.Length > 0 && File.Exists(Path.Combine(folder, "PES6.exe")) ? new GameInstall(folder) : null;
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
            Log("Erreur : " + ex.Message);
            MessageBox.Show(this, ex.Message, "PES6 Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshState(log: false);
        }
    }

    private void Log(string message) => _log.AppendText(message + Environment.NewLine);

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
}
