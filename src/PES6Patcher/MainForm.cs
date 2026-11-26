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
    internal sealed record State(string Folder, string? Exe, bool Display, decimal Width, decimal Height, bool Borderless, bool Gameplay, bool FreeDirection, bool CloseDribble, bool FreeKick, decimal FreeKickWeight, bool FreeKickDebug, bool Shot, decimal ShotPower, bool ShotPowerDebug, bool Accuracy, decimal ShotAccuracy, bool ShotAccuracyDebug, decimal LobPower, bool Spread, decimal SpreadPercent, bool SpreadDebug, bool SpeedGap, decimal SpeedGapPercent, bool SpeedGapDebug, bool SprintTurn, decimal SprintTurnDegrees, bool SprintTurnDebug, bool Tackle, decimal TacklePercent, bool TackleDebug, bool LoftedAim, bool PassLead, decimal PassLeadPercent, bool PassLeadDebug, bool StickDeadZone, decimal StickDeadZonePercent, bool CarrierTurn, decimal CarrierTurnDegrees, decimal CloseDribbleStrength, bool CloseDribbleStrengthDebug, bool AccelerationGap, bool Headers, decimal HeadersPercent, bool HeadersDebug, bool DefenceLine, decimal DefenceLinePercent, bool OffBallPace, decimal OffBallPacePercent, bool SuperShot, decimal SuperShotPercent, decimal SuperShotSpeed, decimal SuperShotWeight, decimal SuperShotStar, bool SuperShotDebug, bool SkipIntro, decimal Speed, string Log, bool PassPowerGauges, decimal ShortPassFrames, decimal ThroughPassFrames, decimal LoftedThroughFrames, bool ForwardRuns, decimal ShotGaugeSpeed, bool BallPush, bool CrossGauge, decimal CrossGaugeMs, bool PassAccuracy, bool LoftedThrough, decimal PassAccuracyNeutral, decimal LoftedSpeed, decimal LoftedHeight, decimal LoftedSpin);

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
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    // the sidebar: one button per page, the badge each shows on its right, and the pages they switch between
    private readonly List<RadioButton> _nav = [];
    private readonly List<Panel> _pages = [];
    private Panel[] GameplayPages = [];
    private string _presetBadge = "";
    // the description popups of the (i) icons
    private readonly ToolTip _tip = new() { OwnerDraw = true, AutoPopDelay = 10000, InitialDelay = 250 };
    private const TextFormatFlags TipFlags = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;
    private static readonly Font InfoFont = new("Georgia", 8.5f, FontStyle.Bold | FontStyle.Italic);
    // the log is folded away: the panel holds it, the button opens it
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 150, Visible = false, BackColor = Card };
    private readonly Button _logButton = new() { AutoSize = true };
    private bool _logUnread;
    private readonly Dictionary<GameplayPreset, RadioButton> _presets = GameplayPresets.All.ToDictionary(p => p, PresetButton);
    private readonly Label _presetInfo = new() { AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(3, 6, 3, 3) };
    private readonly LinkLabel _showDetails = new() { Text = Loc.T("Preset.ShowDetails"), AutoSize = true, Margin = new Padding(3, 0, 3, 6) };
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
    private readonly NumericUpDown _shotGaugeSpeed = new()
    {
        Minimum = PatchOptions.MinShotGaugeSpeed, Maximum = PatchOptions.MaxShotGaugeSpeed, Increment = 5,
        Value = PatchOptions.MaxShotGaugeSpeed, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
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
    private readonly NumericUpDown _closeDribbleStrength = new()
    {
        Minimum = PatchOptions.MinCloseDribbleStrength, Maximum = PatchOptions.MaxCloseDribbleStrength, Increment = 25,
        Value = PatchOptions.DefaultCloseDribbleStrength, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly CheckBox _closeDribbleStrengthDebug = new() { Text = Loc.T("Gameplay.CloseDribbleStrengthDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
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
    private readonly CheckBox _defenceLine = new() { Text = Loc.T("Gameplay.DefenceLine"), AutoSize = true };
    private readonly NumericUpDown _defenceLinePercent = new()
    {
        Minimum = PatchOptions.MinDefenceLine, Maximum = PatchOptions.MaxDefenceLine, Increment = 25,
        Value = PatchOptions.DefaultDefenceLine, Width = 60, Enabled = false,
    };
private readonly CheckBox _offBallPace = new() { Text = Loc.T("Gameplay.OffBallPace"), AutoSize = true };    private readonly NumericUpDown _offBallPacePercent = new()    {        Minimum = PatchOptions.MinOffBallPace, Maximum = PatchOptions.MaxOffBallPace, Increment = 25,        Value = PatchOptions.DefaultOffBallPace, Width = 60, Enabled = false,    };
    private readonly CheckBox _accelerationGap = new() { Text = Loc.T("Gameplay.AccelerationGap"), AutoSize = true };
    private readonly CheckBox _loftedAim = new() { Text = Loc.T("Gameplay.LoftedAim"), AutoSize = true };
    private readonly CheckBox _forwardRuns = new() { Text = Loc.T("Gameplay.ForwardRuns"), AutoSize = true };
    private readonly CheckBox _ballPush = new() { Text = Loc.T("Gameplay.BallPush"), AutoSize = true };
    private readonly CheckBox _passGauges = new() { Text = Loc.T("Gameplay.PassPowerGauges"), AutoSize = true };
    private readonly CheckBox _crossGauge = new() { Text = Loc.T("Gameplay.CrossGauge"), AutoSize = true };
    private readonly CheckBox _passAccuracy = new() { Text = Loc.T("Gameplay.PassAccuracy"), AutoSize = true };
    private readonly CheckBox _loftedThrough = new() { Text = Loc.T("Gameplay.LoftedThrough"), AutoSize = true };
    private readonly NumericUpDown _passAccuracyNeutral = Setting(PatchOptions.MinPassAccuracyNeutral, PatchOptions.MaxPassAccuracyNeutral, PatchOptions.DefaultPassAccuracyNeutral);
    private readonly NumericUpDown _loftedSpeed = Setting(PatchOptions.MinLoftedSpeed, PatchOptions.MaxLoftedSpeed, PatchOptions.DefaultLoftedSpeed);
    private readonly NumericUpDown _loftedHeight = Setting(PatchOptions.MinLoftedHeight, PatchOptions.MaxLoftedHeight, PatchOptions.DefaultLoftedHeight);
    private readonly NumericUpDown _loftedSpin = Setting(PatchOptions.MinLoftedSpin, PatchOptions.MaxLoftedSpin, PatchOptions.DefaultLoftedSpin);

    /// <summary>A numeric setting of an option, off until the option is checked.</summary>
    private static NumericUpDown Setting(int minimum, int maximum, int value) => new() { Minimum = minimum, Maximum = maximum, Value = value, Width = 60, Enabled = false };
    private readonly NumericUpDown _crossGaugeMs = new()
    {
        Minimum = PatchOptions.MinCrossGaugeMs, Maximum = PatchOptions.MaxCrossGaugeMs, Increment = 50,
        Value = PatchOptions.DefaultCrossGaugeMs, Width = 60, Enabled = false,
    };
    private readonly NumericUpDown _shortPassFrames = GaugeFrames(25);
    private readonly NumericUpDown _throughPassFrames = GaugeFrames(25);
    private readonly NumericUpDown _loftedThroughFrames = GaugeFrames(25);

    private static NumericUpDown GaugeFrames(int value) => new()
    {
        Minimum = 4, Maximum = 60, Value = value, Width = 60, Enabled = false,
    };

    private void RefreshPassGauges()
    {
        bool active = _passGauges.Enabled && _passGauges.Checked;
        _shortPassFrames.Enabled = _throughPassFrames.Enabled = _loftedThroughFrames.Enabled = active;
        foreach (var input in new[] { _shortPassFrames, _throughPassFrames, _loftedThroughFrames })
            if (input.Parent is { } row) row.Visible = _passGauges.Enabled;
    }

    private readonly CheckBox _passLead = new() { Text = Loc.T("Gameplay.PassLead"), AutoSize = true };
    private readonly NumericUpDown _passLeadPercent = new()
    {
        Minimum = PatchOptions.MinPassLead, Maximum = PatchOptions.MaxPassLead - 10, Increment = 10,
        Value = PatchOptions.DefaultPassLead, Width = 60, Enabled = false,
    };
    private readonly CheckBox _passLeadDebug = new() { Text = Loc.T("Gameplay.PassLeadDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _headers = new() { Text = Loc.T("Gameplay.Headers"), AutoSize = true };
    private readonly NumericUpDown _headersPercent = new()
    {
        Minimum = PatchOptions.MinHeaders, Maximum = PatchOptions.MaxHeaders - 10, Increment = 10,
        Value = PatchOptions.DefaultHeaders, Width = 60, Enabled = false,
    };
    private readonly CheckBox _headersDebug = new() { Text = Loc.T("Gameplay.HeadersDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
    private readonly CheckBox _superShot = new() { Text = Loc.T("Gameplay.SuperShot"), AutoSize = true };
    private readonly NumericUpDown _superShotPercent = new()
    {
        Minimum = PatchOptions.MinSuperShot, Maximum = PatchOptions.MaxSuperShot, Increment = 2,
        Value = PatchOptions.DefaultSuperShot, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly NumericUpDown _superShotSpeed = new()
    {
        Minimum = PatchOptions.MinSuperShotSpeed, Maximum = PatchOptions.MaxSuperShotSpeed, Increment = 1000,
        Value = PatchOptions.DefaultSuperShotSpeed, Width = 72, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly NumericUpDown _superShotWeight = new()
    {
        Minimum = PatchOptions.MinSuperShotWeight, Maximum = PatchOptions.MaxSuperShotWeight, Increment = 5,
        Value = PatchOptions.DefaultSuperShotWeight, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly NumericUpDown _superShotStar = new()
    {
        Minimum = PatchOptions.MinSuperShotStar, Maximum = PatchOptions.MaxSuperShotStar, Increment = 1,
        Value = PatchOptions.DefaultSuperShotStar, Width = 60, Enabled = false, Margin = new Padding(22, 3, 3, 3),
    };
    private readonly CheckBox _superShotDebug = new() { Text = Loc.T("Gameplay.SuperShotDebug"), AutoSize = true, Enabled = false, Margin = new Padding(22, 0, 3, 3) };
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
        ClientSize = new Size(1060, 720);
        MinimumSize = new Size(760, 520);
        BackColor = Bg;
        ForeColor = Txt;

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
        _closeDribble.EnabledChanged += (_, _) => _closeDribbleStrength.Enabled = _closeDribbleStrengthDebug.Enabled = _closeDribble.Enabled && _closeDribble.Checked;
        _closeDribble.CheckedChanged += (_, _) => _closeDribbleStrength.Enabled = _closeDribbleStrengthDebug.Enabled = _closeDribble.Enabled && _closeDribble.Checked;
        _carrierTurn.EnabledChanged += (_, _) => _carrierTurnDegrees.Enabled = _carrierTurn.Enabled && _carrierTurn.Checked;
        _carrierTurn.CheckedChanged += (_, _) => _carrierTurnDegrees.Enabled = _carrierTurn.Enabled && _carrierTurn.Checked;
        _defenceLine.EnabledChanged += (_, _) => _defenceLinePercent.Enabled = _defenceLine.Enabled && _defenceLine.Checked;
        _defenceLine.CheckedChanged += (_, _) => _defenceLinePercent.Enabled = _defenceLine.Enabled && _defenceLine.Checked;
_offBallPace.EnabledChanged += (_, _) => _offBallPacePercent.Enabled = _offBallPace.Enabled && _offBallPace.Checked;        _offBallPace.CheckedChanged += (_, _) => _offBallPacePercent.Enabled = _offBallPace.Enabled && _offBallPace.Checked;
        _stickDeadZone.EnabledChanged += (_, _) => _stickDeadZonePercent.Enabled = _stickDeadZone.Enabled && _stickDeadZone.Checked;
        _stickDeadZone.CheckedChanged += (_, _) => _stickDeadZonePercent.Enabled = _stickDeadZone.Enabled && _stickDeadZone.Checked;
        _crossGauge.EnabledChanged += (_, _) => _crossGaugeMs.Enabled = _crossGauge.Enabled && _crossGauge.Checked;
        _crossGauge.CheckedChanged += (_, _) => _crossGaugeMs.Enabled = _crossGauge.Enabled && _crossGauge.Checked;
        void Link(CheckBox option, params NumericUpDown[] settings)
        {
            void Update() { foreach (var setting in settings) setting.Enabled = option.Enabled && option.Checked; }
            option.EnabledChanged += (_, _) => Update();
            option.CheckedChanged += (_, _) => Update();
        }
        Link(_passAccuracy, _passAccuracyNeutral);
        Link(_loftedThrough, _loftedSpeed, _loftedHeight, _loftedSpin);
        _passGauges.CheckedChanged += (_, _) => RefreshPassGauges();
        _passGauges.EnabledChanged += (_, _) => RefreshPassGauges();
        _passLead.EnabledChanged += (_, _) => _passLeadPercent.Enabled = _passLeadDebug.Enabled = _passLead.Enabled && _passLead.Checked;
        _passLead.CheckedChanged += (_, _) => _passLeadPercent.Enabled = _passLeadDebug.Enabled = _passLead.Enabled && _passLead.Checked;
        _headers.EnabledChanged += (_, _) => _headersPercent.Enabled = _headersDebug.Enabled = _headers.Enabled && _headers.Checked;
        _headers.CheckedChanged += (_, _) => _headersPercent.Enabled = _headersDebug.Enabled = _headers.Enabled && _headers.Checked;
        _superShot.EnabledChanged += (_, _) => _superShotPercent.Enabled = _superShotSpeed.Enabled = _superShotWeight.Enabled = _superShotStar.Enabled = _superShotDebug.Enabled = _superShot.Enabled && _superShot.Checked;
        _superShot.CheckedChanged += (_, _) => _superShotPercent.Enabled = _superShotSpeed.Enabled = _superShotWeight.Enabled = _superShotStar.Enabled = _superShotDebug.Enabled = _superShot.Enabled && _superShot.Checked;
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

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = Loc.T("Folder.Title"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 3, 3) });
        header.Controls.Add(LabelText(Loc.T("Language.Label")));
        header.Controls.Add(_language);
        header.Controls.Add(_theme);

        var folderRow = Row(_folder, browse);
        folderRow.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        var exeRow = Flow(LabelText(Loc.T("Folder.Exe")), _exe, _report);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 12, 16, 10), BackColor = Card };
        top.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1); };
        top.Controls.Add(header);
        top.Controls.Add(folderRow);
        top.Controls.Add(exeRow);
        top.Controls.Add(_exeState);

        // the pages of the sidebar: general, the profile, then one page per theme of the gameplay options
        var display = Group(_display,
            Flow(LabelText(Loc.T("Display.Resolution")), _width, LabelText("×"), _height, screen),
            _formatInfo,
            Flow(_borderless, Info("Borderless")));
        var general = Page("Section.General", display, Flow(_skipIntro, Info("SkipIntro")));
        var presetCards = Flow([.. _presets.Select(p => Stack(p.Value, new Label
        {
            Text = Loc.T("Preset.Info." + p.Key), AutoSize = true, MaximumSize = new Size(200, 0), ForeColor = Mut, Margin = new Padding(3, 2, 3, 3),
        }))]);
        var profile = Page("Section.Profile",
            new Label { Text = Loc.T("Preset.Hint"), AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Mut, Margin = new Padding(3, 0, 3, 8) },
            presetCards, _presetInfo, _showDetails, _checkAll);
        var dribble = Page("Gameplay.Theme.Dribble",
            Flow(_freeDirection, Info("FreeDirection")),
            Flow(_stickDeadZone, Info("StickDeadZone"), _stickDeadZonePercent, LabelText(Loc.T("Gameplay.StickDeadZonePercent"))),
            Flow(_closeDribble, Info("CloseDribble")),
            Flow(_closeDribbleStrength, LabelText(Loc.T("Gameplay.CloseDribbleStrength"))),
            _closeDribbleStrengthDebug,
            Flow(_sprintTurn, Info("SprintTurn"), _sprintTurnDegrees, LabelText(Loc.T("Gameplay.SprintTurnDegrees"))),
            _sprintTurnDebug,
            Flow(_carrierTurn, Info("CarrierTurn"), _carrierTurnDegrees, LabelText(Loc.T("Gameplay.CarrierTurnDegrees"))));
        var shooting = Page("Gameplay.Theme.Shooting",
            Flow(_freeKick, Info("FreeKick"), _freeKickWeight, LabelText(Loc.T("Gameplay.FreeKickWeight"))),
            _freeKickDebug,
            Flow(_shot, Info("Shot"), _shotPower, LabelText(Loc.T("Gameplay.ShotPower"))),
            _shotPowerDebug,
            Flow(_shotGaugeSpeed, LabelText(Loc.T("Gameplay.ShotGaugeSpeed"))),
            Flow(_accuracy, Info("ShotAccuracy"), _shotAccuracy, LabelText(Loc.T("Gameplay.ShotAccuracyPercent"))),
            _shotAccuracyDebug,
            Flow(_lobPower, LabelText(Loc.T("Gameplay.LobPower"))),
            Flow(_headers, Info("Headers"), _headersPercent, LabelText(Loc.T("Gameplay.HeadersPercent"))),
            _headersDebug,
            Flow(_superShot, Info("SuperShot")),   // its text is long: the height goes on a line of its own, like the other three values
            Flow(_superShotPercent, LabelText(Loc.T("Gameplay.SuperShotPercent"))),
            Flow(_superShotSpeed, LabelText(Loc.T("Gameplay.SuperShotSpeed"))),
            Flow(_superShotWeight, LabelText(Loc.T("Gameplay.SuperShotWeight"))),
            Flow(_superShotStar, LabelText(Loc.T("Gameplay.SuperShotStar"))),
            _superShotDebug);
        var passing = Page("Gameplay.Theme.Passing",
            Flow(_loftedAim, Info("LoftedAim")),
            Flow(_passLead, Info("PassLead"), _passLeadPercent, LabelText(Loc.T("Gameplay.PassLeadPercent"))),
            _passLeadDebug,
            Flow(_passGauges, Info("PassGauges")),
            Flow(LabelText(Loc.T("Gameplay.ShortPassFrames")), _shortPassFrames),
            Flow(LabelText(Loc.T("Gameplay.ThroughPassFrames")), _throughPassFrames),
            Flow(LabelText(Loc.T("Gameplay.LoftedThroughFrames")), _loftedThroughFrames),
            Flow(_crossGauge, Info("CrossGauge")),
            Flow(LabelText(Loc.T("Gameplay.CrossGaugeMs")), _crossGaugeMs),
            Flow(_passAccuracy, Info("PassAccuracy")),
            Flow(LabelText(Loc.T("Gameplay.PassAccuracyNeutral")), _passAccuracyNeutral),
            Flow(_loftedThrough, Info("LoftedThrough")),
            Flow(LabelText(Loc.T("Gameplay.LoftedSpeed")), _loftedSpeed, LabelText(Loc.T("Gameplay.LoftedHeight")), _loftedHeight,
                LabelText(Loc.T("Gameplay.LoftedSpin")), _loftedSpin));
        var team = Page("Section.Team",
            Flow(_spread, Info("Spread"), _spreadPercent, LabelText(Loc.T("Gameplay.SpreadPercent"))),
            _spreadDebug,
            Flow(_tackle, Info("Tackle"), _tacklePercent, LabelText(Loc.T("Gameplay.TacklePercent"))),
            _tackleDebug,
            Flow(_defenceLine, Info("DefenceLine"), _defenceLinePercent, LabelText(Loc.T("Gameplay.DefenceLinePercent"))),
            Flow(_offBallPace, Info("OffBallPace"), _offBallPacePercent, LabelText(Loc.T("Gameplay.OffBallPacePercent"))),
            Flow(_forwardRuns, Info("ForwardRuns")));
        var physics = Page("Section.Physics",
            Flow(_ballPush, Info("BallPush")),
            Flow(_speedGap, Info("SpeedGap"), _speedGapPercent, LabelText(Loc.T("Gameplay.SpeedGapPercent"))),
            _speedGapDebug,
            Flow(_accelerationGap, Info("AccelerationGap")),
            Flow(_physicsMod, Info("PhysicsMod")),
            _physicsCredit,
            Flow(LabelText(Loc.T("Gameplay.Speed")), _speed, LabelText(Loc.T("Gameplay.SpeedHint")), Info("Speed")));
        GameplayPages = [profile, dribble, shooting, passing, team, physics];

        // the sidebar: general, then the gameplay group (its box switches every gameplay page on or off)
        var navGeneral = NavButton(Loc.T("Section.General"), general, () => "");
        var navProfile = NavButton(Loc.T("Section.Profile"), profile, () => _presetBadge);
        var navDribble = NavButton(Loc.T("Gameplay.Theme.Dribble"), dribble, () => Badge(_freeDirection, _stickDeadZone, _closeDribble, _sprintTurn, _carrierTurn));
        var sidebar = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 240, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 12, 8, 8), BackColor = Card };
        _gameplay.Font = new Font(Font, FontStyle.Bold);
        _gameplay.Margin = new Padding(3, 4, 3, 4);
        sidebar.Controls.AddRange([
            navGeneral,
            new Panel { Size = new Size(204, 1), BackColor = Line, Margin = new Padding(0, 8, 0, 6) },
            _gameplay,
            navProfile,
            navDribble,
            NavButton(Loc.T("Gameplay.Theme.Shooting"), shooting, () => Badge(_freeKick, _shot, _accuracy, _headers, _superShot)),
            NavButton(Loc.T("Gameplay.Theme.Passing"), passing, () => Badge(_loftedAim, _passLead, _passGauges, _crossGauge, _passAccuracy, _loftedThrough)),
            NavButton(Loc.T("Section.Team"), team, () => Badge(_spread, _tackle, _defenceLine, _offBallPace, _forwardRuns)),
            NavButton(Loc.T("Section.Physics"), physics, () => Badge(_ballPush, _speedGap, _accelerationGap, _physicsMod)),
        ]);
        foreach (var button in sidebar.Controls.OfType<RadioButton>()) button.Width = button == navGeneral ? 204 : 192;
        sidebar.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height); };
        var pages = new Panel { Dock = DockStyle.Fill };
        foreach (var page in _pages) pages.Controls.Add(page);
        _gameplay.CheckedChanged += (_, _) =>
        {
            foreach (var page in GameplayPages) page.Enabled = _gameplay.Checked;
            UpdateApply();
        };

        // the log is folded away until the button is pressed (or an apply writes to it)
        _logButton.Click += (_, _) => ShowLog(!_logPanel.Visible);
        var copyLog = new Button { Text = Loc.T("Action.CopyLog"), AutoSize = true };
        copyLog.Click += (_, _) => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); };
        var closeLog = new Button { Text = Loc.T("Action.Close"), AutoSize = true };
        closeLog.Click += (_, _) => ShowLog(false);
        var logHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 4, 16, 4) };
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.Controls.Add(new Label { Text = Loc.T("Action.Log"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 3, 3) });
        logHeader.Controls.Add(copyLog);
        logHeader.Controls.Add(closeLog);
        _logPanel.Controls.Add(_log);
        _logPanel.Controls.Add(logHeader);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 10, 16, 12), BackColor = Card };
        footer.Paint += (_, e) => { using var pen = new Pen(Line); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(Flow(_apply, _restore));
        footer.Controls.Add(_logButton);
        _logButton.Anchor = AnchorStyles.Right;
        // docked in this order: the later a control is added, the closer to the edge it sits
        Controls.Add(pages);
        Controls.Add(sidebar);
        Controls.Add(_logPanel);
        Controls.Add(footer);
        Controls.Add(top);

        _tip.Popup += (_, e) =>
            e.ToolTipSize = TextRenderer.MeasureText(_tip.GetToolTip(e.AssociatedControl!), Font, new Size(300, 0), TipFlags) + new Size(18, 14);
        _tip.Draw += (_, e) =>
        {
            using var back = new SolidBrush(Card);
            using var border = new Pen(Line);
            e.Graphics.FillRectangle(back, e.Bounds);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Font, new Rectangle(9, 7, e.Bounds.Width - 18, e.Bounds.Height - 14),
                Txt, TipFlags);
        };
        Deactivate += (_, _) => _tip.Hide(this);
        _theme.Items.AddRange([Loc.T("Theme.System"), Loc.T("Theme.Light"), Loc.T("Theme.Dark")]);
        _theme.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "", "light", "dark" }, ThemePreference.Load()));
        _theme.SelectionChangeCommitted += (_, _) => ChangeTheme();

        // a preset fills the options, any option changed shows which preset they match
        _showDetails.LinkClicked += (_, _) => navDribble.Checked = true;
        foreach (var link in new[] { _showDetails, _physicsCredit }) link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Accent;
        foreach (var (preset, button) in _presets)
            button.Click += (_, _) => ApplyPreset(preset);
        foreach (var control in GameplayPages.Skip(1).SelectMany(Descendants))
        {
            if (control is CheckBox box && box != _physicsMod) box.CheckedChanged += (_, _) => UpdatePreset();
            if (control is NumericUpDown number && number != _speed) number.ValueChanged += (_, _) => UpdatePreset();
        }

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
            _shotGaugeSpeed.Value = previous.ShotGaugeSpeed;
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
            _forwardRuns.Checked = previous.ForwardRuns;
            _ballPush.Checked = previous.BallPush;
            _crossGauge.Checked = previous.CrossGauge;
            _crossGaugeMs.Value = previous.CrossGaugeMs;
            _passAccuracy.Checked = previous.PassAccuracy;
            _loftedThrough.Checked = previous.LoftedThrough;
            _passAccuracyNeutral.Value = previous.PassAccuracyNeutral;
            _loftedSpeed.Value = previous.LoftedSpeed;
            _loftedHeight.Value = previous.LoftedHeight;
            _loftedSpin.Value = previous.LoftedSpin;
            _passLead.Checked = previous.PassLead;
            _passLeadPercent.Value = previous.PassLeadPercent;
            _passLeadDebug.Checked = previous.PassLeadDebug;
            _passGauges.Checked = previous.PassPowerGauges;
            _shortPassFrames.Value = previous.ShortPassFrames;
            _throughPassFrames.Value = previous.ThroughPassFrames;
            _loftedThroughFrames.Value = previous.LoftedThroughFrames;
            _headers.Checked = previous.Headers;
            _headersPercent.Value = previous.HeadersPercent;
            _headersDebug.Checked = previous.HeadersDebug;
            _superShot.Checked = previous.SuperShot;
            _superShotPercent.Value = previous.SuperShotPercent;
            _superShotSpeed.Value = previous.SuperShotSpeed;
            _superShotWeight.Value = previous.SuperShotWeight;
            _superShotStar.Value = previous.SuperShotStar;
            _superShotDebug.Checked = previous.SuperShotDebug;
            _stickDeadZone.Checked = previous.StickDeadZone;
            _stickDeadZonePercent.Value = previous.StickDeadZonePercent;
            _carrierTurn.Checked = previous.CarrierTurn;
            _carrierTurnDegrees.Value = previous.CarrierTurnDegrees;
            _defenceLine.Checked = previous.DefenceLine;
            _defenceLinePercent.Value = previous.DefenceLinePercent;
            _offBallPace.Checked = previous.OffBallPace;
            _offBallPacePercent.Value = previous.OffBallPacePercent;
            _closeDribbleStrength.Value = previous.CloseDribbleStrength;
            _closeDribbleStrengthDebug.Checked = previous.CloseDribbleStrengthDebug;
            _accelerationGap.Checked = previous.AccelerationGap;
            _skipIntro.Checked = previous.SkipIntro;
            _log.Text = previous.Log;
            RefreshState(log: false);
            _speed.Value = previous.Speed;   // keep the user's value over the one read from speeder.cfg
            UpdateFormatInfo();
        }
        // a new window (no options read from a patched exe) starts from the Pro preset
        if (previous == null && !_recordShown) ApplyPreset(GameplayPreset.Pro);
        UpdatePreset();
        navProfile.Checked = true;
        UpdateBadges();
        UpdateLogButton();
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
        RebuildWith = CaptureState();
        Close();
    }

    private void ChangeTheme()
    {
        string name = new[] { "", "light", "dark" }[_theme.SelectedIndex];
        if (name == ThemePreference.Load()) return;
        ThemePreference.Save(name);
        RebuildWith = CaptureState();
        Close();
    }

    /// <summary>What the user entered, to carry over when the window is rebuilt (another language or theme).</summary>
    private State CaptureState() =>
        new State(_folder.Text, _exe.SelectedItem as string, _display.Checked, _width.Value, _height.Value, _borderless.Checked, _gameplay.Checked, _freeDirection.Checked, _closeDribble.Checked, _freeKick.Checked, _freeKickWeight.Value, _freeKickDebug.Checked, _shot.Checked, _shotPower.Value, _shotPowerDebug.Checked, _accuracy.Checked, _shotAccuracy.Value, _shotAccuracyDebug.Checked, _lobPower.Value, _spread.Checked, _spreadPercent.Value, _spreadDebug.Checked, _speedGap.Checked, _speedGapPercent.Value, _speedGapDebug.Checked, _sprintTurn.Checked, _sprintTurnDegrees.Value, _sprintTurnDebug.Checked, _tackle.Checked, _tacklePercent.Value, _tackleDebug.Checked, _loftedAim.Checked, _passLead.Checked, _passLeadPercent.Value, _passLeadDebug.Checked, _stickDeadZone.Checked, _stickDeadZonePercent.Value, _carrierTurn.Checked, _carrierTurnDegrees.Value, _closeDribbleStrength.Value, _closeDribbleStrengthDebug.Checked, _accelerationGap.Checked, _headers.Checked, _headersPercent.Value, _headersDebug.Checked, _defenceLine.Checked, _defenceLinePercent.Value, _offBallPace.Checked, _offBallPacePercent.Value, _superShot.Checked, _superShotPercent.Value, _superShotSpeed.Value, _superShotWeight.Value, _superShotStar.Value, _superShotDebug.Checked, _skipIntro.Checked, _speed.Value, _log.Text, _passGauges.Checked, _shortPassFrames.Value, _throughPassFrames.Value, _loftedThroughFrames.Value, _forwardRuns.Checked, _shotGaugeSpeed.Value, _ballPush.Checked, _crossGauge.Checked, _crossGaugeMs.Value, _passAccuracy.Checked, _loftedThrough.Checked, _passAccuracyNeutral.Value, _loftedSpeed.Value, _loftedHeight.Value, _loftedSpin.Value);

    private InstallSettings CurrentSettings() =>
        new((int)_width.Value, (int)_height.Value, _borderless.Checked, _freeDirection.Enabled && _freeDirection.Checked, (double)_speed.Value,
            Display: _display.Enabled && _display.Checked, Gameplay: _gameplay.Checked, CloseDribble: _closeDribble.Enabled && _closeDribble.Checked,
            CloseDribbleStrength: (int)_closeDribbleStrength.Value, CloseDribbleStrengthDebug: _closeDribbleStrengthDebug.Checked,
            AccelerationGap: _accelerationGap.Enabled && _accelerationGap.Checked,
            SkipIntro: _skipIntro.Enabled && _skipIntro.Checked, PhysicsMod: _physicsMod.Enabled ? _physicsMod.Checked : null,
            FreeKick: _freeKick.Enabled && _freeKick.Checked, FreeKickWeight: (int)_freeKickWeight.Value,
            FreeKickDebug: _freeKickDebug.Checked, ShotPower: _shot.Enabled && _shot.Checked ? (int)_shotPower.Value : 100,
            ShotPowerDebug: _shotPowerDebug.Checked, ShotAccuracy: _accuracy.Enabled && _accuracy.Checked ? (int)_shotAccuracy.Value : 100,
            ShotAccuracyDebug: _shotAccuracyDebug.Checked, Spread: _spread.Enabled && _spread.Checked ? (int)_spreadPercent.Value : 0,
            SpreadDebug: _spreadDebug.Checked, SpeedGap: _speedGap.Enabled && _speedGap.Checked ? (int)_speedGapPercent.Value : 100,
            SpeedGapDebug: _speedGapDebug.Checked, SprintTurn: _sprintTurn.Enabled && _sprintTurn.Checked ? (int)_sprintTurnDegrees.Value : 0,
            LobPower: (int)_lobPower.Value, Headers: _headers.Enabled && _headers.Checked ? (int)_headersPercent.Value : 100, HeadersDebug: _headersDebug.Checked,
            SuperShot: _superShot.Enabled && _superShot.Checked ? (int)_superShotPercent.Value : 0, SuperShotDebug: _superShotDebug.Checked,
            SuperShotSpeed: (int)_superShotSpeed.Value, SuperShotWeight: (int)_superShotWeight.Value, SuperShotStar: (int)_superShotStar.Value,
            SprintTurnDebug: _sprintTurnDebug.Checked, Tackle: _tackle.Enabled && _tackle.Checked ? (int)_tacklePercent.Value : 100,
            TackleDebug: _tackleDebug.Checked, LoftedAim: _loftedAim.Enabled && _loftedAim.Checked,
            PassLead: _passLead.Enabled && _passLead.Checked ? (int)_passLeadPercent.Value : 100, PassLeadDebug: _passLeadDebug.Checked,
            StickDeadZone: _stickDeadZone.Enabled && _stickDeadZone.Checked ? (int)_stickDeadZonePercent.Value : PatchOptions.GameStickDeadZone,
            CarrierTurn: _carrierTurn.Enabled && _carrierTurn.Checked ? (int)_carrierTurnDegrees.Value : 0,
            DefenceLine: _defenceLine.Enabled && _defenceLine.Checked ? (int)_defenceLinePercent.Value : 0,
            OffBallPace: _offBallPace.Enabled && _offBallPace.Checked ? (int)_offBallPacePercent.Value : 0,
            PassPowerGauges: _passGauges.Enabled && _passGauges.Checked,
            ShortPassFrames: (int)_shortPassFrames.Value, ThroughPassFrames: (int)_throughPassFrames.Value,
            LoftedThroughFrames: (int)_loftedThroughFrames.Value, ForwardRuns: _forwardRuns.Enabled && _forwardRuns.Checked,
            ShotGaugeSpeed: _shotGaugeSpeed.Enabled ? (int)_shotGaugeSpeed.Value : 100,
            BallPush: _ballPush.Enabled && _ballPush.Checked,
            CrossGauge: _crossGauge.Enabled && _crossGauge.Checked,
            CrossGaugeMs: (int)_crossGaugeMs.Value,
            PassAccuracy: _passAccuracy.Enabled && _passAccuracy.Checked,
            LoftedThrough: _loftedThrough.Enabled && _loftedThrough.Checked,
            PassAccuracyNeutral: (int)_passAccuracyNeutral.Value,
            LoftedSpeed: (int)_loftedSpeed.Value, LoftedHeight: (int)_loftedHeight.Value, LoftedSpin: (int)_loftedSpin.Value);

    /// <summary>The gameplay options that "check all" sets (not the test keys).</summary>
    private CheckBox[] GameplayOptions =>
        [_freeDirection, _stickDeadZone, _closeDribble, _freeKick, _shot, _accuracy, _spread, _speedGap, _sprintTurn, _carrierTurn, _tackle, _defenceLine, _offBallPace, _forwardRuns, _ballPush, _accelerationGap, _loftedAim, _passLead, _passGauges, _crossGauge, _passAccuracy, _loftedThrough, _headers, _superShot, _physicsMod];

    private bool _settingAll, _recordShown, _applyingPreset;

    /// <summary>Sets the gameplay options to <paramref name="preset"/>'s (the options the exe cannot have stay off).</summary>
    private void ApplyPreset(GameplayPreset preset)
    {
        _applyingPreset = true;
        try { ShowSettings(GameplayPresets.Apply(CurrentSettings(), preset)); }
        finally { _applyingPreset = false; }
        UpdatePreset(preset);
    }

    /// <summary>Selects the preset the options match (<paramref name="applied"/>: the one just applied), or none for custom options.</summary>
    private void UpdatePreset(GameplayPreset? applied = null)
    {
        if (_applyingPreset) return;
        var preset = applied ?? GameplayPresets.Detect(CurrentSettings());
        foreach (var (p, button) in _presets) button.Checked = p == preset;
        _presetInfo.Text = preset is GameplayPreset p2 ? Loc.T("Preset.Summary." + p2) : Loc.T("Preset.Custom");
        _presetBadge = preset is GameplayPreset p3 ? Loc.T("Preset." + p3) : Loc.T("Preset.CustomShort");
        UpdateBadges();
    }

    private static RadioButton PresetButton(GameplayPreset preset)
    {
        var button = new RadioButton
        {
            Text = Loc.T("Preset." + preset), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Appearance = Appearance.Button, AutoSize = false, Size = new Size(200, 38), TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat, AutoCheck = false,
        };
        button.FlatAppearance.CheckedBackColor = Selected;
        button.FlatAppearance.BorderColor = Line;
        button.BackColor = Card;
        return button;
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    /// <summary>Rows stacked top-down (a part of a group shown or hidden as one).</summary>
    private static FlowLayoutPanel Stack(params Control[] rows)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.AddRange(rows);
        return panel;
    }

    private void UpdateCheckAll()
    {
        if (_settingAll) return;
        var available = GameplayOptions.Where(o => o.Enabled).ToList();
        _checkAll.Checked = available.Count > 0 && available.All(o => o.Checked);
        _checkAll.Enabled = available.Count > 0;
        UpdateBadges();
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
        _shotGaugeSpeed.Enabled = report is { ShotGauge: true };
        bool forwardRuns = report is { ForwardRuns: true };
        _forwardRuns.Enabled = forwardRuns;
        _forwardRuns.Text = Loc.T("Gameplay.ForwardRuns") + (forwardRuns || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool ballPush = report is { BallPush: true };
        _ballPush.Enabled = ballPush;
        _ballPush.Text = Loc.T("Gameplay.BallPush") + (ballPush || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool crossGauge = report is { CrossGauge: true };
        _crossGauge.Enabled = crossGauge;
        _crossGauge.Text = Loc.T("Gameplay.CrossGauge") + (crossGauge || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool passStats = report is { PassStats: true };
        _passAccuracy.Enabled = passStats;
        _passAccuracy.Text = Loc.T("Gameplay.PassAccuracy") + (passStats || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        _loftedThrough.Enabled = passStats;
        _loftedThrough.Text = Loc.T("Gameplay.LoftedThrough") + (passStats || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool passLead = report is { PassLead: true };
        _passLead.Enabled = passLead;
        _passGauges.Enabled = report is { PassPowerGauges: true };
        RefreshPassGauges();
        _passLead.Text = Loc.T("Gameplay.PassLead") + (passLead || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool headers = report is { ShotPower: true };
        _headers.Enabled = headers;
        _headers.Text = Loc.T("Gameplay.Headers") + (headers || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        _superShot.Enabled = headers;
        _superShot.Text = Loc.T("Gameplay.SuperShot") + (headers || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool carrierTurn = report is { CarrierTurn: true };
        _carrierTurn.Enabled = carrierTurn;
        _carrierTurn.Text = Loc.T("Gameplay.CarrierTurn") + (carrierTurn || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool defenceLine = report is { DefenceLine: true };
        _defenceLine.Enabled = defenceLine;
        _defenceLine.Text = Loc.T("Gameplay.DefenceLine") + (defenceLine || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool offBallPace = report is { OffBallPace: true };
        _offBallPace.Enabled = offBallPace;
        _offBallPace.Text = Loc.T("Gameplay.OffBallPace") + (offBallPace || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
        bool accelerationGap = report is { AccelerationGap: true };
        _accelerationGap.Enabled = accelerationGap;
        _accelerationGap.Text = Loc.T("Gameplay.AccelerationGap") + (accelerationGap || report == null ? "" : " " + Loc.T("Gameplay.FreeDirectionUnavailable"));
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
        _recordShown |= patchedWith != null;
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
        Set(_closeDribbleStrength, s.CloseDribbleStrength);
        _closeDribbleStrengthDebug.Checked = s.CloseDribbleStrengthDebug;
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
        _defenceLine.Checked = s.DefenceLine > 0;
        if (_defenceLine.Checked) Set(_defenceLinePercent, s.DefenceLine);
        _offBallPace.Checked = s.OffBallPace > 0;
        if (_offBallPace.Checked) Set(_offBallPacePercent, s.OffBallPace);
        _tackle.Checked = s.Tackle != 100;
        if (_tackle.Checked) Set(_tacklePercent, s.Tackle);
        _tackleDebug.Checked = s.TackleDebug;
        _accelerationGap.Checked = s.AccelerationGap;
        _loftedAim.Checked = s.LoftedAim;
        _forwardRuns.Checked = s.ForwardRuns;
        _ballPush.Checked = s.BallPush;
        _crossGauge.Checked = s.CrossGauge;
        Set(_crossGaugeMs, s.CrossGaugeMs);
        _passAccuracy.Checked = s.PassAccuracy;
        _loftedThrough.Checked = s.LoftedThrough;
        Set(_passAccuracyNeutral, s.PassAccuracyNeutral);
        Set(_loftedSpeed, s.LoftedSpeed);
        Set(_loftedHeight, s.LoftedHeight);
        Set(_loftedSpin, s.LoftedSpin);
        Set(_shotGaugeSpeed, s.ShotGaugeSpeed);
        _passLead.Checked = s.PassLead < 100;
        if (_passLead.Checked) Set(_passLeadPercent, s.PassLead);
        _passLeadDebug.Checked = s.PassLeadDebug;
        _passGauges.Checked = s.PassPowerGauges;
        Set(_shortPassFrames, s.ShortPassFrames);
        Set(_throughPassFrames, s.ThroughPassFrames);
        Set(_loftedThroughFrames, s.LoftedThroughFrames);
        _headers.Checked = s.Headers < 100;
        if (_headers.Checked) Set(_headersPercent, s.Headers);
        _headersDebug.Checked = s.HeadersDebug;
        _superShot.Checked = s.SuperShot > 0;
        if (_superShot.Checked) Set(_superShotPercent, s.SuperShot);
        Set(_superShotSpeed, s.SuperShotSpeed);
        Set(_superShotWeight, s.SuperShotWeight);
        Set(_superShotStar, s.SuperShotStar);
        _superShotDebug.Checked = s.SuperShotDebug;
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
        ShowLog(true);
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

    private void Log(string message)
    {
        _log.AppendText(message + Environment.NewLine);
        if (!_logPanel.Visible) _logUnread = true;
        UpdateLogButton();
    }

    private void ShowLog(bool show)
    {
        _logPanel.Visible = show;
        if (show)
        {
            _logUnread = false;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        UpdateLogButton();
    }

    private void UpdateLogButton() =>
        _logButton.Text = (_logPanel.Visible ? "▾ " : "▴ ") + Loc.T("Action.Log")
            + (_logUnread && !_logPanel.Visible ? " ●" : $" ({_log.Lines.Count(l => l.Length > 0)})");

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    private static Label LabelText(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    // the colours of the mockup: the window on Bg, the header, sidebar and footer on Card, hairlines in Line
    private static bool Dark => Application.IsDarkModeEnabled;
    private static Color Bg => Dark ? Color.FromArgb(0x1e, 0x1f, 0x22) : Color.FromArgb(0xf3, 0xf3, 0xf3);
    private static Color Card => Dark ? Color.FromArgb(0x2a, 0x2b, 0x2f) : Color.White;
    private static Color Line => Dark ? Color.FromArgb(0x3d, 0x3f, 0x44) : Color.FromArgb(0xd9, 0xd9, 0xd9);
    private static Color Txt => Dark ? Color.FromArgb(0xe8, 0xe8, 0xe8) : Color.FromArgb(0x1a, 0x1a, 0x1a);
    private static Color Mut => Dark ? Color.FromArgb(0x9a, 0x9a, 0x9a) : Color.FromArgb(0x66, 0x66, 0x66);
    private static Color Accent => Dark ? Color.FromArgb(0x5a, 0xa2, 0xf0) : Color.FromArgb(0x0b, 0x63, 0xc5);
    private static Color Selected => Dark ? Color.FromArgb(0x22, 0x36, 0x4d) : Color.FromArgb(0xe5, 0xf0, 0xfb);

    /// <summary>A page of the sidebar: its title, then its rows. Hidden until its button is selected.</summary>
    private Panel Page(string titleKey, params Control[] rows)
    {
        var title = new Label { Text = Loc.T(titleKey), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(3, 0, 3, 8) };
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMargin = new Size(22, 16), Visible = false };
        // each option starts after a hairline, its test key and values sit close under it
        var items = new List<Control> { title };
        foreach (var row in rows)
        {
            bool option = row is GroupBox || row is FlowLayoutPanel { Controls: [CheckBox box, ..] } && GameplayOptions.Contains(box);
            if (option && items.Count > 1)
                items.Add(new Panel { Size = new Size(700, 1), BackColor = Line, Margin = new Padding(0, 6, 0, 6) });
            row.Margin = new Padding(row.Margin.Left, 2, row.Margin.Right, 2);
            items.Add(row);
        }
        var stack = Stack([.. items]);
        stack.Location = new Point(22, 16);
        page.Controls.Add(stack);
        return page;
    }

    /// <summary>A button of the sidebar showing <paramref name="page"/> when selected, with a badge on its right.</summary>
    private RadioButton NavButton(string text, Panel page, Func<string> badge)
    {
        var button = new RadioButton
        {
            Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, UseMnemonic = false, Text = text, TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(192, 32), Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 1, 0, 1),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.CheckedBackColor = Selected;
        button.CheckedChanged += (_, _) =>
        {
            if (!button.Checked) return;
            _tip.Hide(this);
            foreach (var p in _pages) p.Visible = p == page;
        };
        button.Paint += (_, e) => TextRenderer.DrawText(e.Graphics, badge(), Font,
            new Rectangle(0, 0, button.Width - 8, button.Height), Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        _nav.Add(button);
        _pages.Add(page);
        return button;
    }

    /// <summary>"checked/total" of a page's options.</summary>
    private static string Badge(params CheckBox[] options) => $"{options.Count(o => o.Checked)}/{options.Length}";

    private void UpdateBadges()
    {
        foreach (var button in _nav) button.Invalidate();
    }

    /// <summary>The (i) icon of an option: its description in a popup, on hover or click.</summary>
    private Label Info(string key)
    {
        var text = Loc.T("Info." + key);
        var icon = new Label { Size = new Size(18, 18), Margin = new Padding(0, 4, 8, 0), Cursor = Cursors.Hand, AccessibleName = text };
        icon.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Accent);
            e.Graphics.DrawEllipse(pen, 1, 1, 15, 15);
            TextRenderer.DrawText(e.Graphics, "i", InfoFont, new Rectangle(0, 0, 18, 18), Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        _tip.SetToolTip(icon, text);
        icon.Click += (_, _) => _tip.Show(text, icon, 0, icon.Height + 2, 8000);
        // a popup shown by a click is not hidden by the tooltip itself when the mouse leaves
        icon.MouseLeave += (_, _) => _tip.Hide(icon);
        return icon;
    }

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
