using System.Diagnostics;
using System.Globalization;
using PES6Patcher.Core;
using PES6Patcher.Core.Config;
using PES6Patcher.Core.Patching;

namespace PES6Patcher;

/// <summary>Single window: language, game folder, display, gameplay options, apply / restore, log.</summary>
internal sealed class MainForm : Form
{
    /// <summary>What the user entered, carried over when the window is rebuilt in another language.</summary>
    internal sealed record State(string Folder, string? Exe, bool Display, decimal Width, decimal Height, bool Borderless, bool Gameplay, bool FreeDirection, bool CloseDribble, bool FreeKick, decimal FreeKickWeight, bool FreeKickDebug, bool Shot, decimal ShotPower, bool ShotPowerDebug, bool Accuracy, decimal ShotAccuracy, bool ShotAccuracyDebug, decimal LobPower, bool Spread, decimal SpreadPercent, bool SpreadDebug, bool SpeedGap, decimal SpeedGapPercent, bool SpeedGapDebug, bool SprintTurn, decimal SprintTurnDegrees, bool SprintTurnDebug, bool Tackle, decimal TacklePercent, bool TackleDebug, bool LoftedAim, bool PassLead, decimal PassLeadPercent, bool PassLeadDebug, bool StickDeadZone, decimal StickDeadZonePercent, bool CarrierTurn, decimal CarrierTurnDegrees, bool SkipIntro, decimal Speed, string Log);

    /// <summary>Set when the language changed: the window must be rebuilt with these values.</summary>
    public State? RebuildWith { get; private set; }

    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _exe = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Button _report = new() { Text = Loc.T("Action.CopyReport"), AutoSize = true };
    private readonly Label _exeState = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly CheckBox _display = new() { Text = Loc.T("Display.Group"), AutoSize = true, Checked = true };
    private readonly CheckBox _gameplay = new() { Text = Loc.T("Gameplay.Group"), AutoSize = true, Checked = true };
    private readonly NumericUpDown _width = new() { Minimum = 640, Maximum = 10240, Increment = 10, Width = 80 };
    private readonly NumericUpDown _height = new() { Minimum = 480, Maximum = 4320, Increment = 10, Width = 80 };
    private readonly Label _formatInfo = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    private readonly CheckBox _borderless = new() { Text = Loc.T("Display.Borderless"), AutoSize = true, Checked = true };
    private readonly CheckBox _checkAll = new() { Text = Loc.T("Gameplay.CheckAll"), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Italic) };
    private readonly CheckBox _freeDirection = new() { Text = Loc.T("Gameplay.FreeDirection"), AutoSize = true, Checked = true };
    private readonly CheckBox _stickDeadZone = new() { Text = Loc.T("Gameplay.StickDeadZone"), AutoSize = true, Checked = true };
    private readonly NumericUpDown _stickDeadZonePercent = new()
    {
        Minimum = PatchOptions.MinStickDeadZone, Maximum = PatchOptions.MaxStickDeadZone, Increment = 2,
        Value = PatchOptions.DefaultStickDeadZone, Width = 60,
    };
    private readonly CheckBox _closeDribble = new() { Text = Loc.T("Gameplay.CloseDribble"), AutoSize = true };
    private readonly CheckBox _freeKick = new() { Text = Loc.T("Gameplay.FreeKick"), AutoSize = true };
    private readonly NumericUpDown _freeKickWeight = new()
    {
        Minimum = PatchOptions.MinFreeKickWeight, Maximum = PatchOptions.MaxFreeKickWeight, Increment = 10,
        Value = PatchOptions.DefaultFreeKickWeight, Width = 60, Enabled = false,
    };
    private readonly CheckBox _freeKickDebug = new() { Text = Loc.T("Gameplay.FreeKickDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _shot = new() { Text = Loc.T("Gameplay.Shot"), AutoSize = true };
    private readonly NumericUpDown _shotPower = new()
    {
        Minimum = PatchOptions.MinShotPower, Maximum = PatchOptions.MaxShotPower - 10, Increment = 10,
        Value = PatchOptions.DefaultShotPower, Width = 60, Enabled = false,
    };
    private readonly CheckBox _shotPowerDebug = new() { Text = Loc.T("Gameplay.ShotPowerDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _physicsMod =new() { Text = Loc.T("Gameplay.PhysicsMod"), AutoSize = true };
    private readonly CheckBox _accuracy = new() { Text = Loc.T("Gameplay.ShotAccuracy"), AutoSize = true };
    private readonly NumericUpDown _shotAccuracy = new()
    {
        Minimum = PatchOptions.MinShotAccuracy, Maximum = PatchOptions.MaxShotAccuracy - 10, Increment = 5,
        Value = PatchOptions.DefaultShotAccuracy, Width = 60, Enabled = false,
    };
    private readonly CheckBox _shotAccuracyDebug = new() { Text = Loc.T("Gameplay.ShotAccuracyDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly NumericUpDown _lobPower = new()
    {
        Minimum = PatchOptions.MinLobPower, Maximum = PatchOptions.MaxLobPower, Increment = 10,
        Value = PatchOptions.DefaultLobPower, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly CheckBox _spread = new() { Text = Loc.T("Gameplay.Spread"), AutoSize = true };
    private readonly NumericUpDown _spreadPercent = new()
    {
        Minimum = PatchOptions.MinSpread, Maximum = PatchOptions.MaxSpread, Increment = 10,
        Value = PatchOptions.DefaultSpread, Width = 60, Enabled = false,
    };
    private readonly CheckBox _spreadDebug = new() { Text = Loc.T("Gameplay.SpreadDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _speedGap = new() { Text = Loc.T("Gameplay.SpeedGap"), AutoSize = true };
    private readonly NumericUpDown _speedGapPercent = new()
    {
        Minimum = PatchOptions.MinSpeedGap, Maximum = PatchOptions.MaxSpeedGap, Increment = 25,
        Value = PatchOptions.DefaultSpeedGap, Width = 60, Enabled = false,
    };
    private readonly CheckBox _speedGapDebug = new() { Text = Loc.T("Gameplay.SpeedGapDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _sprintTurn = new() { Text = Loc.T("Gameplay.SprintTurn"), AutoSize = true };
    private readonly NumericUpDown _sprintTurnDegrees = new()
    {
        Minimum = PatchOptions.MinSprintTurn, Maximum = PatchOptions.MaxSprintTurn, Increment = 15,
        Value = PatchOptions.DefaultSprintTurn, Width = 60, Enabled = false,
    };
    private readonly CheckBox _sprintTurnDebug = new() { Text = Loc.T("Gameplay.SprintTurnDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _carrierTurn = new() { Text = Loc.T("Gameplay.CarrierTurn"), AutoSize = true };
    private readonly NumericUpDown _carrierTurnDegrees = new()
    {
        Minimum = PatchOptions.MinCarrierTurn, Maximum = PatchOptions.MaxCarrierTurn, Increment = 1,
        Value = PatchOptions.DefaultCarrierTurn, Width = 60, Enabled = false,
    };
    private readonly CheckBox _tackle = new() { Text = Loc.T("Gameplay.Tackle"), AutoSize = true };
    private readonly NumericUpDown _tacklePercent = new()
    {
        Minimum = PatchOptions.MinTackle + 20, Maximum = PatchOptions.MaxTackle, Increment = 20,
        Value = PatchOptions.DefaultTackle, Width = 60, Enabled = false,
    };
    private readonly CheckBox _tackleDebug = new() { Text = Loc.T("Gameplay.TackleDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _loftedAim = new() { Text = Loc.T("Gameplay.LoftedAim"), AutoSize = true };
    private readonly CheckBox _passLead = new() { Text = Loc.T("Gameplay.PassLead"), AutoSize = true };
    private readonly NumericUpDown _passLeadPercent = new()
    {
        Minimum = PatchOptions.MinPassLead, Maximum = PatchOptions.MaxPassLead - 10, Increment = 10,
        Value = PatchOptions.DefaultPassLead, Width = 60, Enabled = false,
    };
    private readonly CheckBox _passLeadDebug = new() { Text = Loc.T("Gameplay.PassLeadDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    // shown visibly, as the physics mod's licence asks
    private readonly LinkLabel _physicsCredit = new() { Text = PhysicsMod.Credit, AutoSize = true, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _skipIntro = new() { Text = Loc.T("Startup.SkipIntro"), AutoSize = true, Checked = true, Margin = new Padding(12, 3, 3, 3) };
    private readonly NumericUpDown _speed = new() { Minimum = 0.5m, Maximum = 1.5m, Increment = 0.05m, DecimalPlaces = 2, Value = 1.0m, Width = 70 };
    private readonly Button _apply = new() { Text = Loc.T("Action.Apply"), AutoSize = true };
    private readonly Button _restore = new() { Text = Loc.T("Action.Restore"), AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private bool _exeReady;

    public MainForm(State? previous = null)
    {
        Text = "PES6 Patcher";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 900);
        MinimumSize = new Size(640, 560);

        var browse = new Button { Text = Loc.T("Folder.Browse"), AutoSize = true };
        browse.Click += (_, _) => BrowseFolder();
        var screen = new Button { Text = Loc.T("Display.ScreenResolution"), AutoSize = true };
        screen.Click += (_, _) => UseScreenResolution();
        _width.ValueChanged += (_, _) => UpdateFormatInfo();
        _height.ValueChanged += (_, _) => UpdateFormatInfo();
        _folder.Leave += (_, _) => { FillExecutables(null); RefreshState(log: true); };
        _exe.SelectionChangeCommitted += (_, _) => RefreshState(log: true);
        _report.Click += (_, _) => CopyReport();
        _skipIntro.CheckedChanged += (_, _) => UpdateApply();
        _freeKick.EnabledChanged += (_, _) => _freeKickWeight.Enabled = _freeKickDebug.Enabled = _freeKick.Enabled && _freeKick.Checked;
        _freeKick.CheckedChanged += (_, _) => _freeKickWeight.Enabled = _freeKickDebug.Enabled = _freeKick.Enabled && _freeKick.Checked;
        _shot.EnabledChanged += (_, _) => _shotPower.Enabled = _shotPowerDebug.Enabled = _shot.Enabled && _shot.Checked;
        _shot.CheckedChanged += (_, _) => _shotPower.Enabled = _shotPowerDebug.Enabled = _shot.Enabled && _shot.Checked;
        _accuracy.EnabledChanged += (_, _) => _shotAccuracy.Enabled = _shotAccuracyDebug.Enabled = _lobPower.Enabled = _accuracy.Enabled && _accuracy.Checked;
        _accuracy.CheckedChanged += (_, _) => _shotAccuracy.Enabled = _shotAccuracyDebug.Enabled = _lobPower.Enabled = _accuracy.Enabled && _accuracy.Checked;
        _spread.EnabledChanged += (_, _) => _spreadPercent.Enabled = _spreadDebug.Enabled = _spread.Enabled && _spread.Checked;
        _spread.CheckedChanged += (_, _) => _spreadPercent.Enabled = _spreadDebug.Enabled = _spread.Enabled && _spread.Checked;
        _speedGap.EnabledChanged += (_, _) => _speedGapPercent.Enabled = _speedGapDebug.Enabled = _speedGap.Enabled && _speedGap.Checked;
        _speedGap.CheckedChanged += (_, _) => _speedGapPercent.Enabled = _speedGapDebug.Enabled = _speedGap.Enabled && _speedGap.Checked;
        _sprintTurn.EnabledChanged += (_, _) => _sprintTurnDegrees.Enabled = _sprintTurnDebug.Enabled = _sprintTurn.Enabled && _sprintTurn.Checked;
        _sprintTurn.CheckedChanged += (_, _) => _sprintTurnDegrees.Enabled = _sprintTurnDebug.Enabled = _sprintTurn.Enabled && _sprintTurn.Checked;
        _tackle.EnabledChanged += (_, _) => _tacklePercent.Enabled = _tackleDebug.Enabled = _tackle.Enabled && _tackle.Checked;
        _tackle.CheckedChanged += (_, _) => _tacklePercent.Enabled = _tackleDebug.Enabled = _tackle.Enabled && _tackle.Checked;
        _carrierTurn.EnabledChanged += (_, _) => _carrierTurnDegrees.Enabled = _carrierTurn.Enabled && _carrierTurn.Checked;
        _carrierTurn.CheckedChanged += (_, _) => _carrierTurnDegrees.Enabled = _carrierTurn.Enabled && _carrierTurn.Checked;
        _stickDeadZone.EnabledChanged += (_, _) => _stickDeadZonePercent.Enabled = _stickDeadZone.Enabled && _stickDeadZone.Checked;
        _stickDeadZone.CheckedChanged += (_, _) => _stickDeadZonePercent.Enabled = _stickDeadZone.Enabled && _stickDeadZone.Checked;
        _passLead.EnabledChanged += (_, _) => _passLeadPercent.Enabled = _passLeadDebug.Enabled = _passLead.Enabled && _passLead.Checked;
        _passLead.CheckedChanged += (_, _) => _passLeadPercent.Enabled = _passLeadDebug.Enabled = _passLead.Enabled && _passLead.Checked;
        // "check all": a click sets every available option; it shows as checked when they all are
        _checkAll.Click += (_, _) =>
        {
            // each option's change updates this box: keep the value clicked until they are all set
            bool value = _checkAll.Checked;
            _settingAll = true;
            foreach (var option in GameplayOptions.Where(o => o.Enabled)) option.Checked = value;
            _settingAll = false;
            UpdateCheckAll();
        };
        foreach (var option in GameplayOptions)
        {
            option.CheckedChanged += (_, _) => UpdateCheckAll();
            option.EnabledChanged += (_, _) => UpdateCheckAll();
        }
        _physicsCredit.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(PhysicsMod.Url) { UseShellExecute = true });
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

        var display = Group(_display,
            Flow(LabelText(Loc.T("Display.Resolution")), _width, LabelText("×"), _height, screen),
            _formatInfo,
            _borderless);
        var gameplay = Group(_gameplay,
            _checkAll,
            Theme("Gameplay.Theme.Dribble"),
            _freeDirection,
            Flow(_stickDeadZone, _stickDeadZonePercent, LabelText(Loc.T("Gameplay.StickDeadZonePercent"))),
            _closeDribble,
            Flow(_sprintTurn, _sprintTurnDegrees, LabelText(Loc.T("Gameplay.SprintTurnDegrees"))),
            _sprintTurnDebug,
            Flow(_carrierTurn, _carrierTurnDegrees, LabelText(Loc.T("Gameplay.CarrierTurnDegrees"))),
            Theme("Gameplay.Theme.Shooting"),
            Flow(_freeKick, _freeKickWeight, LabelText(Loc.T("Gameplay.FreeKickWeight"))),
            _freeKickDebug,
            Flow(_shot, _shotPower, LabelText(Loc.T("Gameplay.ShotPower"))),
            _shotPowerDebug,
            Flow(_accuracy, _shotAccuracy, LabelText(Loc.T("Gameplay.ShotAccuracyPercent"))),
            _shotAccuracyDebug,
            Flow(_lobPower, LabelText(Loc.T("Gameplay.LobPower"))),
            Theme("Gameplay.Theme.Passing"),
            _loftedAim,
            Flow(_passLead, _passLeadPercent, LabelText(Loc.T("Gameplay.PassLeadPercent"))),
            _passLeadDebug,
            Theme("Gameplay.Theme.Other"),
            Flow(_spread, _spreadPercent, LabelText(Loc.T("Gameplay.SpreadPercent"))),
            _spreadDebug,
            Flow(_tackle, _tacklePercent, LabelText(Loc.T("Gameplay.TacklePercent"))),
            _tackleDebug,
            Flow(_speedGap, _speedGapPercent, LabelText(Loc.T("Gameplay.SpeedGapPercent"))),
            _speedGapDebug,
            _physicsMod,
            _physicsCredit,
            Flow(LabelText(Loc.T("Gameplay.Speed")), _speed, LabelText(Loc.T("Gameplay.SpeedHint"))));

        // scrolls when the window is shorter than the options (the log keeps a fixed height)
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10), AutoScroll = true };
        layout.Controls.Add(header);
        layout.Controls.Add(folderRow);
        layout.Controls.Add(exeRow);
        layout.Controls.Add(_exeState);
        layout.Controls.Add(display);
        layout.Controls.Add(gameplay);
        layout.Controls.Add(_skipIntro);
        layout.Controls.Add(Flow(_apply, _restore));
        layout.Controls.Add(_log);
        for (int i = 0; i < layout.Controls.Count - 1; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
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
            _display.Checked = previous.Display;
            _gameplay.Checked = previous.Gameplay;
            _width.Value = previous.Width;
            _height.Value = previous.Height;
            _borderless.Checked = previous.Borderless;
            _freeDirection.Checked = previous.FreeDirection;
            _closeDribble.Checked = previous.CloseDribble;
            _freeKick.Checked = previous.FreeKick;
            _freeKickWeight.Value = previous.FreeKickWeight;
            _freeKickDebug.Checked = previous.FreeKickDebug;
            _shot.Checked = previous.Shot;
            _shotPower.Value = previous.ShotPower;
            _shotPowerDebug.Checked = previous.ShotPowerDebug;
            _accuracy.Checked = previous.Accuracy;
            _shotAccuracy.Value = previous.ShotAccuracy;
            _shotAccuracyDebug.Checked = previous.ShotAccuracyDebug;
            _lobPower.Value = previous.LobPower;
            _spread.Checked = previous.Spread;
            _spreadPercent.Value = previous.SpreadPercent;
            _spreadDebug.Checked = previous.SpreadDebug;
            _speedGap.Checked = previous.SpeedGap;
            _speedGapPercent.Value = previous.SpeedGapPercent;
            _speedGapDebug.Checked = previous.SpeedGapDebug;
            _sprintTurn.Checked = previous.SprintTurn;
            _sprintTurnDegrees.Value = previous.SprintTurnDegrees;
            _sprintTurnDebug.Checked = previous.SprintTurnDebug;
            _tackle.Checked = previous.Tackle;
            _tacklePercent.Value = previous.TacklePercent;
            _tackleDebug.Checked = previous.TackleDebug;
            _loftedAim.Checked = previous.LoftedAim;
            _passLead.Checked = previous.PassLead;
            _passLeadPercent.Value = previous.PassLeadPercent;
            _passLeadDebug.Checked = previous.PassLeadDebug;
            _stickDeadZone.Checked = previous.StickDeadZone;
            _stickDeadZonePercent.Value = previous.StickDeadZonePercent;
            _carrierTurn.Checked = previous.CarrierTurn;
            _carrierTurnDegrees.Value = previous.CarrierTurnDegrees;
            _skipIntro.Checked = previous.SkipIntro;
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
        RebuildWith = new State(_folder.Text, _exe.SelectedItem as string, _display.Checked, _width.Value, _height.Value, _borderless.Checked, _gameplay.Checked, _freeDirection.Checked, _closeDribble.Checked, _freeKick.Checked, _freeKickWeight.Value, _freeKickDebug.Checked, _shot.Checked, _shotPower.Value, _shotPowerDebug.Checked, _accuracy.Checked, _shotAccuracy.Value, _shotAccuracyDebug.Checked, _lobPower.Value, _spread.Checked, _spreadPercent.Value, _spreadDebug.Checked, _speedGap.Checked, _speedGapPercent.Value, _speedGapDebug.Checked, _sprintTurn.Checked, _sprintTurnDegrees.Value, _sprintTurnDebug.Checked, _tackle.Checked, _tacklePercent.Value, _tackleDebug.Checked, _loftedAim.Checked, _passLead.Checked, _passLeadPercent.Value, _passLeadDebug.Checked, _stickDeadZone.Checked, _stickDeadZonePercent.Value, _carrierTurn.Checked, _carrierTurnDegrees.Value, _skipIntro.Checked, _speed.Value, _log.Text);
        Close();
    }

    private InstallSettings CurrentSettings() =>
        new((int)_width.Value, (int)_height.Value, _borderless.Checked, _freeDirection.Enabled && _freeDirection.Checked, (double)_speed.Value,
            Display: _display.Enabled && _display.Checked, Gameplay: _gameplay.Checked, CloseDribble: _closeDribble.Enabled && _closeDribble.Checked,
            SkipIntro: _skipIntro.Enabled && _skipIntro.Checked, PhysicsMod: _physicsMod.Enabled ? _physicsMod.Checked : null,
            FreeKick: _freeKick.Enabled && _freeKick.Checked, FreeKickWeight: (int)_freeKickWeight.Value,
            FreeKickDebug: _freeKickDebug.Checked, ShotPower: _shot.Enabled && _shot.Checked ? (int)_shotPower.Value : 100,
            ShotPowerDebug: _shotPowerDebug.Checked, ShotAccuracy: _accuracy.Enabled && _accuracy.Checked ? (int)_shotAccuracy.Value : 100,
            ShotAccuracyDebug: _shotAccuracyDebug.Checked, Spread: _spread.Enabled && _spread.Checked ? (int)_spreadPercent.Value : 0,
            SpreadDebug: _spreadDebug.Checked, SpeedGap: _speedGap.Enabled && _speedGap.Checked ? (int)_speedGapPercent.Value : 100,
            SpeedGapDebug: _speedGapDebug.Checked, SprintTurn: _sprintTurn.Enabled && _sprintTurn.Checked ? (int)_sprintTurnDegrees.Value : 0,
            LobPower: (int)_lobPower.Value,
            SprintTurnDebug: _sprintTurnDebug.Checked, Tackle: _tackle.Enabled && _tackle.Checked ? (int)_tacklePercent.Value : 100,
            TackleDebug: _tackleDebug.Checked, LoftedAim: _loftedAim.Enabled && _loftedAim.Checked,
            PassLead: _passLead.Enabled && _passLead.Checked ? (int)_passLeadPercent.Value : 100, PassLeadDebug: _passLeadDebug.Checked,
            StickDeadZone: _stickDeadZone.Enabled && _stickDeadZone.Checked ? (int)_stickDeadZonePercent.Value : PatchOptions.GameStickDeadZone,
            CarrierTurn: _carrierTurn.Enabled && _carrierTurn.Checked ? (int)_carrierTurnDegrees.Value : 0);

    /// <summary>The gameplay options that "check all" sets (not the test keys).</summary>
    private CheckBox[] GameplayOptions =>
        [_freeDirection, _stickDeadZone, _closeDribble, _freeKick, _shot, _accuracy, _spread, _speedGap, _sprintTurn, _carrierTurn, _tackle, _loftedAim, _passLead, _physicsMod];

    private bool _settingAll;

    private void UpdateCheckAll()
    {
        if (_settingAll) return;
        var available = GameplayOptions.Where(o => o.Enabled).ToList();
        _checkAll.Checked = available.Count > 0 && available.All(o => o.Checked);
        _checkAll.Enabled = available.Count > 0;
    }

    /// <summary>Apply needs a patchable exe and at least one group (or the intro skip) selected.</summary>
    private void UpdateApply()
    {
        var s = CurrentSettings();
        _apply.Enabled = _exeReady && (s.Display || s.Gameplay || s.SkipIntro);
    }

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
            _exeReady = _apply.Enabled = _restore.Enabled = false;
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
        _exeReady = state is ExeState.Original or ExeState.Patched;
        _restore.Enabled = File.Exists(game.ExeBackup) || game.ConfigFiles.Any(f => File.Exists(f + ".orig"));

        bool display = report is { Display: true };
        _display.Enabled = display;
        _display.Text = Loc.T("Display.Group") + (display || report == null ? "" : " " + Loc.T("Display.Unavailable"));
        bool freeDirection = report is { FreeDirection: true };
        _freeDirection.Enabled = freeDirection;
        _freeDirection.Text = Loc.T("Gameplay.FreeDirection") + (freeDirection || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool closeDribble = report is { CloseDribble: true };
        _closeDribble.Enabled = closeDribble;
        _closeDribble.Text = Loc.T("Gameplay.CloseDribble") + (closeDribble || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool freeKick = report is { FreeKick: true };
        _freeKick.Enabled = freeKick;
        _freeKick.Text = Loc.T("Gameplay.FreeKick") + (freeKick || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool shot = report is { ShotPower: true };
        _shot.Enabled = shot;
        _shot.Text = Loc.T("Gameplay.Shot") + (shot || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        _accuracy.Enabled = shot;
        _accuracy.Text = Loc.T("Gameplay.ShotAccuracy") + (shot || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool spread = report is { Spread: true };
        _spread.Enabled = spread;
        _spread.Text = Loc.T("Gameplay.Spread") + (spread || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool speedGap = report is { SpeedGap: true };
        _speedGap.Enabled = speedGap;
        _speedGap.Text = Loc.T("Gameplay.SpeedGap") + (speedGap || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool sprintTurn = report is { SprintTurn: true };
        _sprintTurn.Enabled = sprintTurn;
        _sprintTurn.Text = Loc.T("Gameplay.SprintTurn") + (sprintTurn || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool tackle = report is { Tackle: true };
        _tackle.Enabled = tackle;
        _tackle.Text = Loc.T("Gameplay.Tackle") + (tackle || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool loftedAim = report is { LoftedAim: true };
        _loftedAim.Enabled = loftedAim;
        _loftedAim.Text = Loc.T("Gameplay.LoftedAim") + (loftedAim || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool passLead = report is { PassLead: true };
        _passLead.Enabled = passLead;
        _passLead.Text = Loc.T("Gameplay.PassLead") + (passLead || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool carrierTurn = report is { CarrierTurn: true };
        _carrierTurn.Enabled = carrierTurn;
        _carrierTurn.Text = Loc.T("Gameplay.CarrierTurn") + (carrierTurn || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool stickDeadZone = report is { StickDeadZone: true };
        _stickDeadZone.Enabled = stickDeadZone;
        _stickDeadZone.Text = Loc.T("Gameplay.StickDeadZone") + (stickDeadZone || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool skipIntro = report is { SkipIntro: true };
        _skipIntro.Enabled = skipIntro;
        _skipIntro.Text = Loc.T("Startup.SkipIntro") + (skipIntro || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        _physicsMod.Enabled = game.HasPhysicsMod;
        _physicsMod.Checked = game.PhysicsModLoaded;
        _physicsMod.Text = Loc.T("Gameplay.PhysicsMod") + (game.HasPhysicsMod ? "" : " " + Loc.T("Gameplay.PhysicsModMissing"));

        if (File.Exists(game.Speeder) && SpeederConfig.Read(File.ReadAllText(game.Speeder)) is double speed)
            _speed.Value = Math.Clamp((decimal)speed, _speed.Minimum, _speed.Maximum);
        // a patched exe: show the options it was patched with (only when the user picks a game, not after an apply)
        var patchedWith = log && state is ExeState.Patched or ExeState.PatchedWithoutBackup ? PatchRecord.Load(game) : null;
        if (patchedWith != null) ShowSettings(patchedWith);
        UpdateApply();
        if (log)
        {
            Log(Loc.T("Log.Game", game.Folder));
            if (!game.HasKitserver) Log(Loc.T("Log.NoKitserver"));
            if (state is ExeState.Patched or ExeState.PatchedWithoutBackup)
                Log(Loc.T(patchedWith != null ? "Log.SettingsDetected" : "Log.SettingsUnknown", exe));
        }
    }

    /// <summary>Fills the window with the settings of an apply (the physics mod and the speed stay as read from their files).</summary>
    private void ShowSettings(InstallSettings s)
    {
        static void Set(NumericUpDown box, decimal value) => box.Value = Math.Clamp(value, box.Minimum, box.Maximum);
        _display.Checked = s.Display;
        if (s.Display)
        {
            Set(_width, s.Width);
            Set(_height, s.Height);
            _borderless.Checked = s.BorderlessWindow;
        }
        _gameplay.Checked = s.Gameplay;
        _freeDirection.Checked = s.FreeDirection;
        _stickDeadZone.Checked = s.StickDeadZone != PatchOptions.GameStickDeadZone;
        if (_stickDeadZone.Checked) Set(_stickDeadZonePercent, s.StickDeadZone);
        _closeDribble.Checked = s.CloseDribble;
        _freeKick.Checked = s.FreeKick;
        Set(_freeKickWeight, s.FreeKickWeight);
        _freeKickDebug.Checked = s.FreeKickDebug;
        _shot.Checked = s.ShotPower < 100;
        if (_shot.Checked) Set(_shotPower, s.ShotPower);
        _shotPowerDebug.Checked = s.ShotPowerDebug;
        _accuracy.Checked = s.ShotAccuracy < 100;
        if (_accuracy.Checked) Set(_shotAccuracy, s.ShotAccuracy);
        _shotAccuracyDebug.Checked = s.ShotAccuracyDebug;
        Set(_lobPower, s.LobPower);
        _spread.Checked = s.Spread > 0;
        if (_spread.Checked) Set(_spreadPercent, s.Spread);
        _spreadDebug.Checked = s.SpreadDebug;
        _speedGap.Checked = s.SpeedGap != 100;
        if (_speedGap.Checked) Set(_speedGapPercent, s.SpeedGap);
        _speedGapDebug.Checked = s.SpeedGapDebug;
        _sprintTurn.Checked = s.SprintTurn > 0;
        if (_sprintTurn.Checked) Set(_sprintTurnDegrees, s.SprintTurn);
        _sprintTurnDebug.Checked = s.SprintTurnDebug;
        _carrierTurn.Checked = s.CarrierTurn > 0;
        if (_carrierTurn.Checked) Set(_carrierTurnDegrees, s.CarrierTurn);
        _tackle.Checked = s.Tackle != 100;
        if (_tackle.Checked) Set(_tacklePercent, s.Tackle);
        _tackleDebug.Checked = s.TackleDebug;
        _loftedAim.Checked = s.LoftedAim;
        _passLead.Checked = s.PassLead < 100;
        if (_passLead.Checked) Set(_passLeadPercent, s.PassLead);
        _passLeadDebug.Checked = s.PassLeadDebug;
        _skipIntro.Checked = s.SkipIntro;
        UpdateFormatInfo();
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

    /// <summary>Heading of a theme in the gameplay group (dribbling, shooting...).</summary>
    private Label Theme(string key) => new()
    {
        Text = Loc.T(key), AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = SystemColors.HotTrack,
        Margin = new Padding(3, 10, 3, 2),
    };

    /// <summary>Group whose title is <paramref name="header"/>: unchecking it greys out the rows.</summary>
    private GroupBox Group(CheckBox header, params Control[] rows)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.AddRange(rows);
        var box = new GroupBox { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 12, 8, 8) };
        box.Controls.Add(panel);
        // drawn over the frame line, where the title would be
        header.Location = new Point(8, 0);
        header.Padding = new Padding(2, 0, 0, 0);
        header.BackColor = BackColor;
        box.Controls.Add(header);
        header.BringToFront();
        header.CheckedChanged += (_, _) => { panel.Enabled = header.Checked; UpdateApply(); };
        header.EnabledChanged += (_, _) => panel.Enabled = header.Enabled && header.Checked;
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
