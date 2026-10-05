using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace FC27Assist;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}

public enum PlayMode { Attack, Defense }
public enum Dir { None, Forward, ForwardRight, Right, BackRight, Back, BackLeft, Left, ForwardLeft }

[Flags]
public enum XButtons : ushort
{
    DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008,
    Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

internal static class XInputNative
{
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState910(uint dwUserIndex, out XInputState pState);

    public static bool TryGetState(int slot, out XInputState state)
    {
        try { return XInputGetState14((uint)slot, out state) == 0; }
        catch (DllNotFoundException) { return XInputGetState910((uint)slot, out state) == 0; }
        catch (EntryPointNotFoundException) { return XInputGetState910((uint)slot, out state) == 0; }
    }
}

public sealed class AppConfig
{
    public string Language { get; set; } = "ar";
    public int ControllerSlot { get; set; } = 0;
    public bool DirtyMeta { get; set; } = false;
    public int RsTriggerDeadzone { get; set; } = 18500;
    public int RsReleaseDeadzone { get; set; } = 9000;
    public int SkillStepMs { get; set; } = 52;
    public int SkillCooldownMs { get; set; } = 130;
    public int BTapThresholdMs { get; set; } = 180;
    public int LowDrivenChargeMs { get; set; } = 420;
    public int BNormalShotCapMs { get; set; } = 620;
    public int LowDrivenSecondTapGapMs { get; set; } = 30;
    public int LowDrivenSecondTapMs { get; set; } = 45;
    public int LbChordWindowMs { get; set; } = 85;
    public int InputLoopHz { get; set; } = 500;
    public bool AutoPress { get; set; } = true;
    public string PressureStrength { get; set; } = "Balanced";
    public bool SprintJockeyAssist { get; set; } = true;
    public bool HardTackleAssist { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;

    public Dictionary<string, string> SkillMap { get; set; } = new()
    {
        ["RS_UP"] = "Explosive Stepover",
        ["RS_RIGHT"] = "Ball Roll Spin Right",
        ["RS_LEFT"] = "Ball Roll Spin Left",
        ["RS_DOWN"] = "Stepover Ball Right",
        ["LB_RS_UP"] = "Lateral Heel to Heel",
        ["LB_RS_RIGHT"] = "Skilled Bridge",
        ["LB_RS_LEFT"] = "Stop and Go",
        ["LB_RS_DOWN"] = "Trickster Fake Shot"
    };

    private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FC27Assist");
    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch { return new AppConfig(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record MacroStep(int Ms, Dir Rs = Dir.None, Dir Ls = Dir.None,
    XButtons Down = 0, XButtons Up = 0, byte? Lt = null, byte? Rt = null,
    bool NeutralRs = false, bool NeutralLs = false);

public sealed class SkillDef
{
    public string Name { get; init; } = "";
    public int Stars { get; init; }
    public bool Trickster { get; init; }
    public bool NewFc27 { get; init; }
    public string Category { get; init; } = "All";
    public Func<int, List<MacroStep>> Build { get; init; } = _ => new();
    public override string ToString()
    {
        var req = Stars > 0 ? new string('★', Stars) : "";
        if (Trickster) req = string.IsNullOrEmpty(req) ? "Trickster Required" : req + " • Trickster";
        return $"{Name}  ({req})";
    }
}

public static class SkillLibrary
{
    private static MacroStep S(int ms, Dir rs = Dir.None, XButtons down = 0, XButtons up = 0, byte? lt = null, byte? rt = null, bool neutralRs = false)
        => new(ms, rs, Dir.None, down, up, lt, rt, neutralRs, false);

    private static List<MacroStep> Seq(params MacroStep[] s) => s.ToList();

    public static readonly List<SkillDef> Skills = new()
    {
        new(){ Name="Explosive Stepover", Stars=3, Category="Meta", Build=ms => Seq(
            S(Math.Max(24,ms/2), Dir.Forward, XButtons.LeftShoulder),
            S(Math.Max(24,ms/2), Dir.ForwardRight, XButtons.LeftShoulder),
            S(Math.Max(24,ms/2), Dir.Right, XButtons.LeftShoulder),
            S(18, Dir.None, up:XButtons.LeftShoulder, neutralRs:true))},
        new(){ Name="Ball Roll Spin Right", Stars=4, NewFc27=true, Category="Meta", Build=ms => Seq(
            S(Math.Max(70,ms+18), Dir.Right), S(ms, Dir.Forward), S(18, neutralRs:true))},
        new(){ Name="Ball Roll Spin Left", Stars=4, NewFc27=true, Category="Meta", Build=ms => Seq(
            S(Math.Max(70,ms+18), Dir.Left), S(ms, Dir.Forward), S(18, neutralRs:true))},
        new(){ Name="Stepover Ball Right", Stars=4, NewFc27=true, Category="Meta", Build=ms => Seq(
            S(ms, Dir.Forward, XButtons.LeftShoulder), S(ms, Dir.Right, XButtons.LeftShoulder), S(18, up:XButtons.LeftShoulder, neutralRs:true))},
        new(){ Name="Stepover Ball Left", Stars=4, NewFc27=true, Category="Meta", Build=ms => Seq(
            S(ms, Dir.Forward, XButtons.LeftShoulder), S(ms, Dir.Left, XButtons.LeftShoulder), S(18, up:XButtons.LeftShoulder, neutralRs:true))},
        new(){ Name="Lateral Heel to Heel", Stars=3, NewFc27=true, Category="Fast", Build=ms => Seq(
            S(ms, Dir.Left, XButtons.LeftShoulder), S(ms, Dir.Right, XButtons.LeftShoulder), S(20, up:XButtons.LeftShoulder, neutralRs:true))},
        new(){ Name="Skilled Bridge", Stars=4, NewFc27=true, Category="Fast", Build=ms => Seq(
            S(ms, Dir.Forward, lt:255), S(ms, Dir.Back, lt:255), S(20, lt:0, neutralRs:true))},
        new(){ Name="Stop and Go", Stars=2, NewFc27=true, Category="Fast", Build=ms => Seq(
            S(ms, Dir.Back, lt:255), S(ms, Dir.Forward, lt:255), S(20, lt:0, neutralRs:true))},
        new(){ Name="Trickster Fake Shot", Stars=0, Trickster=true, Category="Meta", Build=ms => Seq(
            S(42, down:XButtons.LeftShoulder|XButtons.B), S(28, down:XButtons.LeftShoulder|XButtons.A, up:XButtons.B), S(ms, Dir.Right, XButtons.LeftShoulder, XButtons.A), S(20, up:XButtons.LeftShoulder, neutralRs:true))},

        new(){ Name="Ball Roll Right", Stars=2, Category="Fast", Build=ms => Seq(S(Math.Max(110,ms*2), Dir.Right), S(20, neutralRs:true))},
        new(){ Name="Ball Roll Left", Stars=2, Category="Fast", Build=ms => Seq(S(Math.Max(110,ms*2), Dir.Left), S(20, neutralRs:true))},
        new(){ Name="Body Feint Right", Stars=2, Category="Fast", Build=ms => Seq(S(ms, Dir.Right), S(20, neutralRs:true))},
        new(){ Name="Body Feint Left", Stars=2, Category="Fast", Build=ms => Seq(S(ms, Dir.Left), S(20, neutralRs:true))},
        new(){ Name="Stepover Right", Stars=2, Category="Fast", Build=ms => QuarterArc(ms,true,false)},
        new(){ Name="Stepover Left", Stars=2, Category="Fast", Build=ms => QuarterArc(ms,false,false)},
        new(){ Name="Reverse Stepover Right", Stars=2, Category="Fast", Build=ms => QuarterArc(ms,true,true)},
        new(){ Name="Reverse Stepover Left", Stars=2, Category="Fast", Build=ms => QuarterArc(ms,false,true)},
        new(){ Name="Heel Flick", Stars=3, Category="Fast", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Back),S(18,neutralRs:true))},
        new(){ Name="Heel to Ball Roll", Stars=4, Category="Meta", Build=ms => Seq(S(ms,Dir.Forward,XButtons.LeftShoulder),S(ms,Dir.Back,XButtons.LeftShoulder),S(18,up:XButtons.LeftShoulder,neutralRs:true))},
        new(){ Name="Lane Change Right", Stars=4, Category="Meta", Build=ms => Seq(S(Math.Max(120,ms*2),Dir.Right,XButtons.LeftShoulder),S(20,up:XButtons.LeftShoulder,neutralRs:true))},
        new(){ Name="Lane Change Left", Stars=4, Category="Meta", Build=ms => Seq(S(Math.Max(120,ms*2),Dir.Left,XButtons.LeftShoulder),S(20,up:XButtons.LeftShoulder,neutralRs:true))},
        new(){ Name="Drag Turn Right", Stars=4, NewFc27=true, Category="Fast", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Right),S(20,neutralRs:true))},
        new(){ Name="Drag Turn Left", Stars=4, NewFc27=true, Category="Fast", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Left),S(20,neutralRs:true))},
        new(){ Name="Drag Back Spin Right", Stars=4, Category="Fast", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Right),S(20,neutralRs:true))},
        new(){ Name="Drag Back Spin Left", Stars=4, Category="Fast", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Left),S(20,neutralRs:true))},
        new(){ Name="Three Touch Roulette Right", Stars=4, Category="Direction", Build=ms => Seq(S(ms,Dir.Back,lt:255),S(ms,Dir.Right,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Three Touch Roulette Left", Stars=4, Category="Direction", Build=ms => Seq(S(ms,Dir.Back,lt:255),S(ms,Dir.Left,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Four Touch Skill", Stars=4, NewFc27=true, Category="Direction", Build=ms => Seq(S(ms,Dir.Back,lt:255),S(ms,Dir.Back,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Flair Roulette Right", Stars=4, NewFc27=true, Category="Direction", Build=ms => Circle(ms,true,XButtons.LeftShoulder)},
        new(){ Name="Flair Roulette Left", Stars=4, NewFc27=true, Category="Direction", Build=ms => Circle(ms,false,XButtons.LeftShoulder)},
        new(){ Name="Simple Rainbow", Stars=4, Category="Direction", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Forward),S(ms,Dir.Forward),S(20,neutralRs:true))},
        new(){ Name="Stop and Turn Right", Stars=4, Category="Fast", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Right),S(20,neutralRs:true))},
        new(){ Name="Stop and Turn Left", Stars=4, Category="Fast", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Left),S(20,neutralRs:true))},
        new(){ Name="Ball Hop", Stars=4, Category="Utility", Build=ms => Seq(new MacroStep(Math.Max(60,ms),Dir.None,Dir.None,XButtons.LeftShoulder|XButtons.RightThumb), new MacroStep(20,Down:0,Up:XButtons.LeftShoulder|XButtons.RightThumb))},
        new(){ Name="Directional Nutmeg", Stars=1, Category="Fast", Build=ms => Seq(S(ms,Dir.Forward,XButtons.LeftShoulder|XButtons.RightShoulder),S(20,up:XButtons.LeftShoulder|XButtons.RightShoulder,neutralRs:true))},

        new(){ Name="Elastico", Stars=5, Category="5★", Build=ms => Arc(ms,true)},
        new(){ Name="Reverse Elastico", Stars=5, Category="5★", Build=ms => Arc(ms,false)},
        new(){ Name="Advanced Rainbow", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Back),S(Math.Max(90,ms+30),Dir.Forward),S(ms,Dir.Forward),S(20,neutralRs:true))},
        new(){ Name="Sombrero Flick", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Forward),S(ms,Dir.Back),S(20,neutralRs:true))},
        new(){ Name="Turn and Spin Right", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Right),S(20,neutralRs:true))},
        new(){ Name="Turn and Spin Left", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Forward),S(ms,Dir.Left),S(20,neutralRs:true))},
        new(){ Name="Heel Fake Right", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Left,lt:255),S(ms,Dir.Right,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Heel Fake Left", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Right,lt:255),S(ms,Dir.Left,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Alternate Elastico Chop Right", Stars=5, NewFc27=true, Category="5★", Build=ms => Seq(S(ms,Dir.Back,XButtons.RightShoulder,lt:255),S(ms,Dir.Right,XButtons.RightShoulder,lt:255),S(20,up:XButtons.RightShoulder,lt:0,neutralRs:true))},
        new(){ Name="Alternate Elastico Chop Left", Stars=5, NewFc27=true, Category="5★", Build=ms => Seq(S(ms,Dir.Back,XButtons.RightShoulder,lt:255),S(ms,Dir.Left,XButtons.RightShoulder,lt:255),S(20,up:XButtons.RightShoulder,lt:0,neutralRs:true))},

        new(){ Name="Drag Back", Stars=2, Category="Utility", Build=ms => Seq(new MacroStep(ms,Dir.None,Dir.Back,XButtons.LeftShoulder|XButtons.RightShoulder),new MacroStep(20,Up:XButtons.LeftShoulder|XButtons.RightShoulder,NeutralLs:true))},
        new(){ Name="Feint Forward and Turn", Stars=2, Category="Direction", Build=ms => Seq(S(ms,Dir.Back),S(ms,Dir.Back),S(20,neutralRs:true))},
        new(){ Name="Stutter Feint Right", Stars=3, Category="Fast", Build=ms => Seq(S(ms,Dir.Left,lt:255),S(ms,Dir.Right,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Stutter Feint Left", Stars=3, Category="Fast", Build=ms => Seq(S(ms,Dir.Right,lt:255),S(ms,Dir.Left,lt:255),S(20,lt:0,neutralRs:true))},
        new(){ Name="Fake Left Go Right", Stars=3, Category="Direction", Build=ms => HalfArc(ms,true)},
        new(){ Name="Fake Right Go Left", Stars=3, Category="Direction", Build=ms => HalfArc(ms,false)},
        new(){ Name="Flair Nutmeg", Stars=4, Category="Utility", Build=ms => Seq(S(ms,Dir.Forward,XButtons.LeftShoulder|XButtons.RightShoulder),S(20,up:XButtons.LeftShoulder|XButtons.RightShoulder,neutralRs:true))},
        new(){ Name="Spin Right", Stars=4, Category="Direction", Build=ms => CircleWithTrigger(ms,true,XButtons.RightShoulder,255)},
        new(){ Name="Spin Left", Stars=4, Category="Direction", Build=ms => CircleWithTrigger(ms,false,XButtons.RightShoulder,255)},
        new(){ Name="Flick Over", Stars=5, Category="5★", Build=ms => Seq(S(Math.Max(150,ms*3),Dir.Forward),S(20,neutralRs:true))},
        new(){ Name="Flair Rainbow", Stars=5, Category="5★", Build=ms => Seq(S(ms,Dir.Back,XButtons.LeftShoulder),S(ms,Dir.Forward,XButtons.LeftShoulder),S(20,up:XButtons.LeftShoulder,neutralRs:true))},
        new(){ Name="First Time Spin", Stars=5, NewFc27=true, Category="5★", Build=ms => Seq(new MacroStep(Math.Max(90,ms*2),Down:XButtons.LeftShoulder|XButtons.RightShoulder),new MacroStep(20,Up:XButtons.LeftShoulder|XButtons.RightShoulder))}
    };

    private static int ArcStep(int ms) => Math.Max(18, ms / 2);

    private static List<MacroStep> QuarterArc(int ms, bool right, bool reverse)
    {
        var q = ArcStep(ms);
        Dir[] dirs = (right, reverse) switch
        {
            (true, false) => new[]{Dir.Forward, Dir.ForwardRight, Dir.Right},
            (false, false) => new[]{Dir.Forward, Dir.ForwardLeft, Dir.Left},
            (true, true) => new[]{Dir.Right, Dir.ForwardRight, Dir.Forward},
            _ => new[]{Dir.Left, Dir.ForwardLeft, Dir.Forward}
        };
        var list = dirs.Select(d => S(q,d)).ToList();
        list.Add(S(18,neutralRs:true));
        return list;
    }

    private static List<MacroStep> Arc(int ms, bool clockwise)
    {
        var q = ArcStep(ms);
        var dirs = clockwise
            ? new[]{Dir.Right,Dir.BackRight,Dir.Back,Dir.BackLeft,Dir.Left}
            : new[]{Dir.Left,Dir.BackLeft,Dir.Back,Dir.BackRight,Dir.Right};
        var list = dirs.Select(d => S(q,d)).ToList();
        list.Add(S(20,neutralRs:true));
        return list;
    }

    private static List<MacroStep> HalfArc(int ms, bool leftToRight)
    {
        var q = ArcStep(ms);
        var dirs = leftToRight
            ? new[]{Dir.Left,Dir.BackLeft,Dir.Back,Dir.BackRight,Dir.Right}
            : new[]{Dir.Right,Dir.BackRight,Dir.Back,Dir.BackLeft,Dir.Left};
        var list = dirs.Select(d => S(q,d)).ToList();
        list.Add(S(20,neutralRs:true));
        return list;
    }

    private static List<MacroStep> HalfCircle(int ms, bool clockwise) => Arc(ms, clockwise);

    private static List<MacroStep> Circle(int ms, bool clockwise, XButtons modifier)
    {
        var q = ArcStep(ms);
        var dirs = clockwise
            ? new[]{Dir.Forward,Dir.ForwardRight,Dir.Right,Dir.BackRight,Dir.Back,Dir.BackLeft,Dir.Left,Dir.ForwardLeft}
            : new[]{Dir.Forward,Dir.ForwardLeft,Dir.Left,Dir.BackLeft,Dir.Back,Dir.BackRight,Dir.Right,Dir.ForwardRight};
        var list = dirs.Select(d => S(q,d,modifier)).ToList();
        list.Add(S(20,up:modifier,neutralRs:true));
        return list;
    }

    private static List<MacroStep> CircleWithTrigger(int ms, bool clockwise, XButtons modifier, byte lt)
    {
        var q = ArcStep(ms);
        var dirs = clockwise
            ? new[]{Dir.Back,Dir.BackRight,Dir.Right,Dir.ForwardRight,Dir.Forward,Dir.ForwardLeft,Dir.Left}
            : new[]{Dir.Back,Dir.BackLeft,Dir.Left,Dir.ForwardLeft,Dir.Forward,Dir.ForwardRight,Dir.Right};
        var list = dirs.Select(d => S(q,d,modifier,lt:lt)).ToList();
        list.Add(S(20,up:modifier,lt:0,neutralRs:true));
        return list;
    }

    public static SkillDef Get(string name) => Skills.FirstOrDefault(s => s.Name == name) ?? Skills[0];
}

public sealed class VirtualReport
{
    public ushort Buttons;
    public byte LT, RT;
    public short LX, LY, RX, RY;
}

public sealed class MacroRunner
{
    private List<MacroStep>? _steps;
    private int _index;
    private long _stepDeadline;
    private double _facingRad;
    public bool Active => _steps is { Count: > 0 } && _index < _steps.Count;
    public string CurrentName { get; private set; } = "";
    public event Action<string>? Finished;

    public void Start(string name, List<MacroStep> steps, double facingRad)
    {
        _steps = steps;
        _index = 0;
        var now = Stopwatch.GetTimestamp();
        _stepDeadline = AddMs(now, Math.Max(1, steps[0].Ms));
        _facingRad = facingRad;
        CurrentName = name;
    }

    public void Cancel()
    {
        _steps = null;
        _index = 0;
        CurrentName = "";
    }

    private static long AddMs(long ticks, int ms) => ticks + (long)(Stopwatch.Frequency * (ms / 1000.0));

    public void Apply(VirtualReport r)
    {
        if (!Active || _steps is null) return;
        var now = Stopwatch.GetTimestamp();
        if (Active && now >= _stepDeadline)
        {
            // Never skip required macro edges after an OS scheduling stall.
            // Advance at most one step per output frame so every press/release reaches ViGEm.
            _index++;
            if (!Active)
            {
                var done = CurrentName;
                Cancel();
                Finished?.Invoke(done);
                return;
            }
            _stepDeadline = AddMs(now, Math.Max(1, _steps[_index].Ms));
        }
        if (!Active || _steps is null) return;
        var s = _steps[_index];
        r.Buttons = (ushort)((r.Buttons | (ushort)s.Down) & ~(ushort)s.Up);
        if (s.Lt.HasValue) r.LT = s.Lt.Value;
        if (s.Rt.HasValue) r.RT = s.Rt.Value;
        if (s.NeutralRs) { r.RX = 0; r.RY = 0; }
        else if (s.Rs != Dir.None) (r.RX, r.RY) = RotatedVector(s.Rs, _facingRad);
        if (s.NeutralLs) { r.LX = 0; r.LY = 0; }
        else if (s.Ls != Dir.None) (r.LX, r.LY) = RotatedVector(s.Ls, _facingRad);
    }

    public static (short x, short y) RotatedVector(Dir d, double facing)
    {
        double baseAngle = d switch
        {
            Dir.Forward => 0,
            Dir.ForwardRight => -Math.PI / 4,
            Dir.Right => -Math.PI / 2,
            Dir.BackRight => -3 * Math.PI / 4,
            Dir.Back => Math.PI,
            Dir.BackLeft => 3 * Math.PI / 4,
            Dir.Left => Math.PI / 2,
            Dir.ForwardLeft => Math.PI / 4,
            _ => 0
        };
        var a = facing + baseAngle;
        const double mag = 30500;
        return ((short)Math.Clamp((int)(Math.Sin(a) * mag), short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)(Math.Cos(a) * mag), short.MinValue, short.MaxValue));
    }
}

public static class ConflictRules
{
    public static bool SuppressLb(bool modeTransitionConsumed, bool chordConsumed, double lbAgeMs, int chordWindowMs)
        => modeTransitionConsumed || chordConsumed || lbAgeMs < chordWindowMs;

    public static bool ManualFaceOverride(ushort buttons)
        => (buttons & (ushort)(XButtons.A | XButtons.B | XButtons.X | XButtons.Y)) != 0;

    public static bool BlockAutoPress(ushort buttons, bool rsSwitchIntent)
        => rsSwitchIntent || (buttons & (ushort)(XButtons.A | XButtons.B | XButtons.X | XButtons.Y)) != 0;

    public static bool ShotOwnsModifiers(bool bActive, bool lowDrivenTail)
        => bActive || lowDrivenTail;
}

public sealed class ControllerEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly MacroRunner _macro = new();
    private AppConfig _cfg;
    private Thread? _thread;
    private volatile bool _running;
    private ViGEmClient? _client;
    private IXbox360Controller? _virtual;
    private Action? _submit;
    private bool _controllerConnected;
    private bool _vigemReady;
    private ushort _prevButtons;
    private bool _rsLatched;
    private long _lastSkillEnd;
    private double _facingRad;
    private bool _bActive;
    private long _bStart;
    private bool _bCapped;
    private bool _bNormalMode;
    private bool _lowDrivenTail;
    private int _lowDrivenTailPhase;
    private long _lowDrivenPhaseStart;
    private long _pressPhaseStart;
    private bool _pressOn;
    private long _dirtyBoostUntil;
    private double _lastLsAngle;
    private long _lastLsAngleAt;
    private long _fidgetSnapUntil;
    private short _snapLX, _snapLY;
    private long _lbDownAt;
    private bool _lbChordConsumed;
    private bool _lbModeTransitionConsumed;
    private bool _lbRawPassed;
    private bool _prevLtModePressed;
    private double _lastPhysicalRsMagnitude;
    private ushort _blockedUntilReleaseMask;
    private long _defensePressBlockUntil;
    private double _prevRsMagnitude;

    public PlayMode Mode { get; private set; } = PlayMode.Attack;
    public bool Connected => _controllerConnected;
    public bool ViGEmReady => _vigemReady;
    public string LastAction { get; private set; } = "Ready";
    public XInputGamepad LastPhysical { get; private set; }
    public double LoopHz { get; private set; }
    public string Error { get; private set; } = "";

    public event Action? StatusChanged;

    public ControllerEngine(AppConfig cfg)
    {
        _cfg = cfg;
        _macro.Finished += name =>
        {
            _lastSkillEnd = Stopwatch.GetTimestamp();
            if (_cfg.DirtyMeta && (name.Contains("Stepover") || name.Contains("Spin") || name.Contains("Bridge")))
                _dirtyBoostUntil = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 0.11);
        };
    }

    public void UpdateConfig(AppConfig cfg) { lock (_gate) _cfg = cfg; }

    public void Start()
    {
        if (_running) return;
        try
        {
            _client = new ViGEmClient();
            _virtual = _client.CreateXbox360Controller();
            _virtual.Connect();
            var t = _virtual.GetType();
            t.GetProperty("AutoSubmitReport")?.SetValue(_virtual, false);
            var m = t.GetMethod("SubmitReport");
            if (m != null) _submit = (Action)Delegate.CreateDelegate(typeof(Action), _virtual, m);
            _vigemReady = true;
        }
        catch (Exception ex)
        {
            Error = "ViGEm: " + ex.Message;
            _vigemReady = false;
        }
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "FC27Assist.Input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private static double MsSince(long t) => (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
    private static bool Btn(ushort b, XButtons f) => (b & (ushort)f) != 0;
    private static bool Rising(ushort now, ushort prev, XButtons f) => Btn(now,f) && !Btn(prev,f);

    private void SetMode(PlayMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        _macro.Cancel();
        _rsLatched = false;
        _bActive = false;
        _lowDrivenTail = false;
        _bNormalMode = false;
        _dirtyBoostUntil = 0;
        _fidgetSnapUntil = 0;
        _pressOn = false;
        _pressPhaseStart = 0;
        _prevRsMagnitude = 0;
        _defensePressBlockUntil = 0;
        LastAction = mode == PlayMode.Attack ? "ATTACK MODE" : "DEFENSE MODE";
        StatusChanged?.Invoke();
    }

    private void Loop()
    {
        long hzStart = Stopwatch.GetTimestamp();
        long nextTick = Stopwatch.GetTimestamp();
        int ticks = 0;
        while (_running)
        {
            AppConfig cfg; lock (_gate) cfg = _cfg;
            if (!XInputNative.TryGetState(cfg.ControllerSlot, out var state))
            {
                if (_controllerConnected) { _controllerConnected = false; StatusChanged?.Invoke(); }
                _prevButtons = 0;
                _prevLtModePressed = false;
                _rsLatched = false;
                _prevRsMagnitude = 0;
                _lbChordConsumed = false;
                _lbModeTransitionConsumed = false;
                _lbRawPassed = false;
                _lastPhysicalRsMagnitude = 0;
                _blockedUntilReleaseMask = 0;
                _bActive = false;
                _lowDrivenTail = false;
                _pressOn = false;
                _pressPhaseStart = 0;
                _macro.Cancel();
                Thread.Sleep(8);
                continue;
            }
            if (!_controllerConnected) { _controllerConnected = true; StatusChanged?.Invoke(); }
            var p = state.Gamepad;
            var r = ProcessFrameForTest(p, cfg);
            Send(r);
            ticks++;
            if (MsSince(hzStart) >= 1000)
            {
                LoopHz = ticks * 1000.0 / Math.Max(1, MsSince(hzStart));
                hzStart = Stopwatch.GetTimestamp(); ticks = 0;
            }
            var targetHz = Math.Clamp(cfg.InputLoopHz, 250, 1000);
            var tickTicks = Math.Max(1L, Stopwatch.Frequency / targetHz);
            nextTick += tickTicks;
            var nowTicks = Stopwatch.GetTimestamp();

            // If Windows pre-empted us for too long, resync instead of accumulating timing debt.
            if (nowTicks - nextTick > tickTicks * 4)
                nextTick = nowTicks + tickTicks;

            while (_running)
            {
                var remain = nextTick - Stopwatch.GetTimestamp();
                if (remain <= 0) break;

                // Coarse sleep first, then spin for the final sub-millisecond slice.
                if (remain > Stopwatch.Frequency / 700)
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(32);
            }
        }
    }

    public VirtualReport ProcessFrameForTest(XInputGamepad p, AppConfig? cfgOverride = null)
    {
        AppConfig cfg = cfgOverride ?? _cfg;
        LastPhysical = p;

        bool ltModePressed = p.LeftTrigger >= 28;

        // Mode changes are edge-triggered: one transition only.
        if (ltModePressed && !_prevLtModePressed && Mode != PlayMode.Defense)
        {
            _blockedUntilReleaseMask |= (ushort)(p.Buttons & (ushort)(XButtons.A | XButtons.B | XButtons.X | XButtons.Y));
            SetMode(PlayMode.Defense);
        }

        if (Rising(p.Buttons, _prevButtons, XButtons.LeftShoulder))
        {
            _lbDownAt = Stopwatch.GetTimestamp();
            _lbChordConsumed = false;
            _lbRawPassed = false;
            _lbModeTransitionConsumed = Mode != PlayMode.Attack;
            if (_lbModeTransitionConsumed)
            {
                _blockedUntilReleaseMask |= (ushort)(p.Buttons & (ushort)(XButtons.A | XButtons.B | XButtons.X | XButtons.Y));
                bool rsWasAlreadyHeld = _lastPhysicalRsMagnitude >= cfg.RsReleaseDeadzone;
                SetMode(PlayMode.Attack);
                _rsLatched = rsWasAlreadyHeld;
            }
        }

        if (!Btn(p.Buttons, XButtons.LeftShoulder) && Btn(_prevButtons, XButtons.LeftShoulder))
        {
            var currentRsMag = Math.Sqrt((double)p.ThumbRX*p.ThumbRX + (double)p.ThumbRY*p.ThumbRY);
            if ((_lbChordConsumed || _lbModeTransitionConsumed || _lbRawPassed) &&
                currentRsMag >= cfg.RsReleaseDeadzone)
                _rsLatched = true;

            _lbChordConsumed = false;
            _lbModeTransitionConsumed = false;
            _lbRawPassed = false;
        }

        _prevLtModePressed = ltModePressed;

        // Any face button held while changing mode stays inert until physically released.
        _blockedUntilReleaseMask = (ushort)(_blockedUntilReleaseMask & p.Buttons);
        var effectiveP = p;
        effectiveP.Buttons = (ushort)(p.Buttons & ~_blockedUntilReleaseMask);

        UpdateFacing(effectiveP);
        var r = new VirtualReport
        {
            Buttons=effectiveP.Buttons, LT=effectiveP.LeftTrigger, RT=effectiveP.RightTrigger,
            LX=effectiveP.ThumbLX, LY=effectiveP.ThumbLY, RX=effectiveP.ThumbRX, RY=effectiveP.ThumbRY
        };

        if (Mode == PlayMode.Attack) ApplyAttack(effectiveP, r, cfg);
        else ApplyDefense(effectiveP, r, cfg);

        if (_macro.Active)
        {
            r.Buttons = (ushort)(r.Buttons & ~((ushort)XButtons.LeftShoulder | (ushort)XButtons.RightShoulder));
            r.LT = 0;
            r.RT = 0;
            _macro.Apply(r);
        }

        if (cfg.DirtyMeta && Mode == PlayMode.Attack)
            ApplyDirtyMeta(effectiveP, r, cfg);

        _prevButtons = p.Buttons;
        _lastPhysicalRsMagnitude = Math.Sqrt((double)p.ThumbRX*p.ThumbRX + (double)p.ThumbRY*p.ThumbRY);
        return r;
    }

    public void ResetInputStateForTest()
    {
        _prevButtons = 0;
        _prevLtModePressed = false;
        _rsLatched = false;
        _prevRsMagnitude = 0;
        _lbChordConsumed = false;
        _lbModeTransitionConsumed = false;
        _lbRawPassed = false;
        _lastPhysicalRsMagnitude = 0;
        _blockedUntilReleaseMask = 0;
        _bActive = false;
        _bNormalMode = false;
        _bCapped = false;
        _lowDrivenTail = false;
        _pressOn = false;
        _pressPhaseStart = 0;
        _dirtyBoostUntil = 0;
        _fidgetSnapUntil = 0;
        _macro.Cancel();
    }

    private void UpdateFacing(XInputGamepad p)
    {
        double mag = Math.Sqrt((double)p.ThumbLX*p.ThumbLX + (double)p.ThumbLY*p.ThumbLY);
        if (mag > 6500)
        {
            _facingRad = Math.Atan2(p.ThumbLX, p.ThumbLY);
            var now = Stopwatch.GetTimestamp();
            if (_lastLsAngleAt != 0)
            {
                double delta = NormalizeAngle(_facingRad - _lastLsAngle);
                if (Math.Abs(delta) > 0.95 && MsSince(_lastLsAngleAt) < 120)
                {
                    var m = Math.Min(32767.0, Math.Max(24000.0, mag));
                    _snapLX = (short)(Math.Sin(_facingRad)*m);
                    _snapLY = (short)(Math.Cos(_facingRad)*m);
                    _fidgetSnapUntil = now + (long)(Stopwatch.Frequency*0.040);
                }
            }
            _lastLsAngle = _facingRad; _lastLsAngleAt = now;
        }
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= Math.PI*2;
        while (a < -Math.PI) a += Math.PI*2;
        return a;
    }

    private void ApplyAttack(XInputGamepad p, VirtualReport r, AppConfig cfg)
    {
        double rsMag = Math.Sqrt((double)p.ThumbRX*p.ThumbRX + (double)p.ThumbRY*p.ThumbRY);
        bool lbHeld = Btn(p.Buttons, XButtons.LeftShoulder);
        var lbAge = _lbDownAt == 0 ? double.MaxValue : MsSince(_lbDownAt);

        if (rsMag < cfg.RsReleaseDeadzone) _rsLatched = false;

        // LB is a chord candidate only during the short intent window.
        // Once native LB has been released to the game, RS is passed through until LB is released.
        if (lbHeld && !_lbModeTransitionConsumed && !_lbChordConsumed && lbAge >= cfg.LbChordWindowMs)
            _lbRawPassed = true;

        if (_lbRawPassed)
        {
            r.RX = p.ThumbRX;
            r.RY = p.ThumbRY;
        }
        else
        {
            r.RX = 0;
            r.RY = 0;

            bool lbSkillModifier = lbHeld &&
                (!_lbModeTransitionConsumed || lbAge <= cfg.LbChordWindowMs);

            if (!_rsLatched && !_macro.Active && rsMag >= cfg.RsTriggerDeadzone && SkillReady(cfg))
            {
                var dir = Cardinal(p.ThumbRX, p.ThumbRY);
                string key = (lbSkillModifier ? "LB_RS_" : "RS_") + dir;
                if (cfg.SkillMap.TryGetValue(key, out var skillName))
                {
                    if (lbSkillModifier) _lbChordConsumed = true;
                    var skill = SkillLibrary.Get(skillName);
                    _macro.Start(skill.Name, skill.Build(cfg.SkillStepMs), _facingRad);
                    LastAction = skill.Name;
                    _rsLatched = true;
                }
            }
        }

        if (lbHeld)
        {
            if (ConflictRules.SuppressLb(_lbModeTransitionConsumed, _lbChordConsumed, lbAge, cfg.LbChordWindowMs))
                r.Buttons = (ushort)(r.Buttons & ~(ushort)XButtons.LeftShoulder);
        }

        // Manual face-button input always wins over a running skill macro.
        if (_macro.Active && ConflictRules.ManualFaceOverride(p.Buttons))
        {
            _macro.Cancel();
            _lastSkillEnd = Stopwatch.GetTimestamp();
            LastAction = "Manual override";
        }

        if (_macro.Active) return;

        // A new manual pass/cross command cancels any automated shot tail.
        if ((_bActive || _lowDrivenTail) &&
            (Btn(p.Buttons, XButtons.A) || Btn(p.Buttons, XButtons.X) || Btn(p.Buttons, XButtons.Y)))
        {
            _bActive = false;
            _lowDrivenTail = false;
            _bNormalMode = false;
            _bCapped = false;
            LastAction = "Shot cancelled by manual input";
        }

        if (Btn(p.Buttons, XButtons.A))
        {
            r.Buttons = (ushort)((r.Buttons | (ushort)(XButtons.A|XButtons.RightShoulder)) & ~(ushort)XButtons.LeftShoulder);
        }

        if (Btn(p.Buttons, XButtons.Y))
        {
            r.Buttons = (ushort)((r.Buttons | (ushort)(XButtons.Y|XButtons.LeftShoulder)) & ~(ushort)XButtons.RightShoulder);
        }

        HandleShotB(p, r, cfg);

        // The calibrated B state machine owns shot modifiers while active.
        if (ConflictRules.ShotOwnsModifiers(_bActive, _lowDrivenTail))
            r.Buttons = (ushort)(r.Buttons & ~((ushort)XButtons.LeftShoulder | (ushort)XButtons.RightShoulder));
    }

    private bool SkillReady(AppConfig cfg)
    {
        if (_lastSkillEnd == 0) return true;
        var cd = cfg.DirtyMeta ? Math.Min(90, cfg.SkillCooldownMs) : cfg.SkillCooldownMs;
        return MsSince(_lastSkillEnd) >= cd;
    }

    private static string Cardinal(short x, short y)
    {
        if (Math.Abs(y) >= Math.Abs(x)) return y >= 0 ? "UP" : "DOWN";
        return x >= 0 ? "RIGHT" : "LEFT";
    }

    private void HandleShotB(XInputGamepad p, VirtualReport r, AppConfig cfg)
    {
        bool b = Btn(p.Buttons, XButtons.B);
        bool bRise = Rising(p.Buttons, _prevButtons, XButtons.B);

        if (bRise)
        {
            _bActive = true;
            _bCapped = false;
            _bNormalMode = false;
            _bStart = Stopwatch.GetTimestamp();
            _lowDrivenTail = false;
        }

        // Attack-mode B is fully owned by the shot state machine.
        r.Buttons = (ushort)(r.Buttons & ~(ushort)XButtons.B);

        if (_bActive)
        {
            var held = MsSince(_bStart);

            // If B is still physically held past the tap threshold, this is a normal shot.
            if (!_bNormalMode && held > cfg.BTapThresholdMs)
            {
                _bNormalMode = true;
                LastAction = "Normal Strong Shot";
            }

            if (!_bNormalMode)
            {
                if (b)
                {
                    // Keep the initial press continuous while intent is still undecided.
                    r.Buttons |= (ushort)XButtons.B;
                }
                else
                {
                    // Quick release: finish a calibrated first-shot charge, then add the second tap.
                    _bActive = false;
                    _lowDrivenTail = true;
                    _lowDrivenTailPhase = 0;
                    _lowDrivenPhaseStart = Stopwatch.GetTimestamp();
                    LastAction = "Low Driven Shot";
                }
            }
            else
            {
                // Normal shot: user chooses hold intent; app chooses the maximum power.
                if (held < cfg.BNormalShotCapMs && !_bCapped)
                {
                    // Once Hold intent is confirmed, physical release no longer changes shot power.
                    r.Buttons |= (ushort)XButtons.B;
                }
                else
                {
                    _bCapped = true;
                    _bActive = false;
                }
            }
        }

        if (_lowDrivenTail)
        {
            var totalCharge = MsSince(_bStart);

            if (_lowDrivenTailPhase == 0)
            {
                if (totalCharge < cfg.LowDrivenChargeMs)
                {
                    // Continue the same first B press even though the user already released.
                    r.Buttons |= (ushort)XButtons.B;
                }
                else
                {
                    _lowDrivenTailPhase = 1;
                    _lowDrivenPhaseStart = Stopwatch.GetTimestamp();
                }
            }
            else if (_lowDrivenTailPhase == 1)
            {
                // Required release gap between first charge and second B tap.
                if (MsSince(_lowDrivenPhaseStart) >= cfg.LowDrivenSecondTapGapMs)
                {
                    _lowDrivenTailPhase = 2;
                    _lowDrivenPhaseStart = Stopwatch.GetTimestamp();
                }
            }
            else
            {
                r.Buttons |= (ushort)XButtons.B;
                if (MsSince(_lowDrivenPhaseStart) >= cfg.LowDrivenSecondTapMs)
                {
                    _lowDrivenTail = false;
                    _lowDrivenTailPhase = 0;
                }
            }
        }
    }

    private void ApplyDefense(XInputGamepad p, VirtualReport r, AppConfig cfg)
    {
        _macro.Cancel();
        r.RX = p.ThumbRX; r.RY = p.ThumbRY;

        var now = Stopwatch.GetTimestamp();
        var rsMag = Math.Sqrt((double)p.ThumbRX*p.ThumbRX + (double)p.ThumbRY*p.ThumbRY);
        bool rsSwitchIntent = rsMag > 14500 && _prevRsMagnitude <= 14500;
        _prevRsMagnitude = rsMag;

        bool tackleOrKeeper = Btn(p.Buttons, XButtons.A) || Btn(p.Buttons, XButtons.B) || Btn(p.Buttons, XButtons.X) || Btn(p.Buttons, XButtons.Y);
        bool conflictBlock = ConflictRules.BlockAutoPress(p.Buttons, rsSwitchIntent);
        if (conflictBlock)
        {
            // Never combine automatic RB pressure with a manual tackle/slide/keeper rush/player switch.
            _defensePressBlockUntil = now + (long)(Stopwatch.Frequency * (tackleOrKeeper ? 0.28 : 0.16));
            _pressOn = false;
            _pressPhaseStart = now;
        }

        bool pressGuardActive = now < _defensePressBlockUntil;

        if (cfg.AutoPress && !pressGuardActive && !Btn(p.Buttons, XButtons.RightShoulder))
        {
            int onMs = cfg.PressureStrength == "Aggressive" ? 430 : cfg.PressureStrength == "Low" ? 220 : 330;
            int offMs = cfg.PressureStrength == "Aggressive" ? 80 : cfg.PressureStrength == "Low" ? 220 : 130;
            if (_pressPhaseStart == 0) _pressPhaseStart = Stopwatch.GetTimestamp();
            var e = MsSince(_pressPhaseStart);
            if (_pressOn && e >= onMs) { _pressOn=false; _pressPhaseStart=Stopwatch.GetTimestamp(); }
            else if (!_pressOn && e >= offMs) { _pressOn=true; _pressPhaseStart=Stopwatch.GetTimestamp(); }
            if (_pressOn) r.Buttons |= (ushort)XButtons.RightShoulder;
        }
        else { _pressPhaseStart=Stopwatch.GetTimestamp(); _pressOn=false; }

        if (cfg.SprintJockeyAssist && p.LeftTrigger > 40)
        {
            double ls = Math.Sqrt((double)p.ThumbLX*p.ThumbLX + (double)p.ThumbLY*p.ThumbLY) / 32767.0;
            if (ls > 0.72) r.RT = 255;
        }

        if (cfg.HardTackleAssist)
        {
            if ((Btn(p.Buttons,XButtons.B) || Btn(p.Buttons,XButtons.X)) && !Btn(_prevButtons,XButtons.B) && !Btn(_prevButtons,XButtons.X))
                _bStart = Stopwatch.GetTimestamp();
            if ((Btn(p.Buttons,XButtons.B) || Btn(p.Buttons,XButtons.X)) && MsSince(_bStart) > 170)
                r.Buttons |= (ushort)XButtons.RightShoulder;
        }
    }

    private void ApplyDirtyMeta(XInputGamepad p, VirtualReport r, AppConfig cfg)
    {
        // Never alter aim/movement while the user is committing a face-button action or shot.
        if (_bActive || _lowDrivenTail || ConflictRules.ManualFaceOverride(p.Buttons))
            return;

        long now = Stopwatch.GetTimestamp();
        if (now < _dirtyBoostUntil)
        {
            double ls = Math.Sqrt((double)p.ThumbLX*p.ThumbLX + (double)p.ThumbLY*p.ThumbLY);
            if (ls > 9000) r.RT = 255;
        }
        if (now < _fidgetSnapUntil && !_macro.Active)
        {
            r.LX = _snapLX; r.LY = _snapLY;
        }
    }

    private void Send(VirtualReport r)
    {
        if (!_vigemReady || _virtual is null) return;
        try
        {
            _virtual.ButtonState = r.Buttons;
            _virtual.LeftTrigger = r.LT;
            _virtual.RightTrigger = r.RT;
            _virtual.LeftThumbX = r.LX;
            _virtual.LeftThumbY = r.LY;
            _virtual.RightThumbX = r.RX;
            _virtual.RightThumbY = r.RY;
            _submit?.Invoke();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _vigemReady = false;
            StatusChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(300); } catch { }
        try { _virtual?.Disconnect(); } catch { }
        try { (_virtual as IDisposable)?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
    }
}

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly ControllerEngine _engine;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly Panel _content = new();
    private readonly Label _modeBadge = new();
    private readonly Label _controllerBadge = new();
    private readonly Label _vigemBadge = new();
    private readonly Label _latencyBadge = new();
    private readonly Button _langBtn = new();
    private readonly Dictionary<string, Button> _nav = new();
    private string _page = "dashboard";
    private readonly Color _bg = Color.FromArgb(10,14,23);
    private readonly Color _panel = Color.FromArgb(18,24,38);
    private readonly Color _panel2 = Color.FromArgb(25,33,50);
    private readonly Color _accent = Color.FromArgb(73,221,164);
    private readonly Color _cyan = Color.FromArgb(72,180,255);
    private readonly Color _text = Color.FromArgb(237,242,250);
    private readonly Color _muted = Color.FromArgb(150,164,186);

    private bool Ar => _cfg.Language == "ar";
    private string T(string ar, string en) => Ar ? ar : en;

    public MainForm()
    {
        _cfg = AppConfig.Load();
        Text = "FC27 Assist";
        MinimumSize = new Size(1060, 700);
        Size = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _bg;
        ForeColor = _text;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        BuildShell();
        _engine = new ControllerEngine(_cfg);
        _engine.StatusChanged += () => { if (!IsDisposed) BeginInvoke(UpdateStatus); };
        _engine.Start();
        ShowPage("dashboard");

        _uiTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _uiTimer.Tick += (_,_) => UpdateStatus();
        _uiTimer.Start();
        FormClosing += (_,_) => { _cfg.Save(); _engine.Dispose(); };
    }

    private void BuildShell()
    {
        var side = new Panel { Dock=DockStyle.Left, Width=225, BackColor=Color.FromArgb(13,18,29), Padding=new Padding(16) };
        Controls.Add(side);
        var logo = new Label { Text="FC27\nASSIST", AutoSize=false, Height=82, Dock=DockStyle.Top, ForeColor=_text, Font=new Font("Segoe UI Semibold",18,FontStyle.Bold), TextAlign=ContentAlignment.MiddleLeft };
        side.Controls.Add(logo);

        AddNav(side,"dashboard","◈  " + T("الرئيسية","Dashboard"));
        AddNav(side,"attack","⚡  " + T("الهجوم","Attack"));
        AddNav(side,"defense","◆  " + T("الدفاع","Defense"));
        AddNav(side,"skills","✦  " + T("مكتبة المهارات","Skill Library"));
        AddNav(side,"controller","◎  " + T("اختبار اليد","Controller Test"));
        AddNav(side,"settings","⚙  " + T("الإعدادات","Settings"));

        var foot = new Label { Dock=DockStyle.Bottom, Height=60, Text=T("LB = هجوم   •   LT = دفاع","LB = Attack   •   LT = Defense"), ForeColor=_muted, TextAlign=ContentAlignment.MiddleLeft };
        side.Controls.Add(foot);

        var top = new Panel { Dock=DockStyle.Top, Height=74, BackColor=_bg, Padding=new Padding(24,14,24,10) };
        Controls.Add(top);
        _langBtn.Text = Ar ? "EN" : "عربي";
        _langBtn.Dock=DockStyle.Right; _langBtn.Width=70; StyleButton(_langBtn,true); _langBtn.Click += (_,_) => ToggleLanguage();
        top.Controls.Add(_langBtn);
        _latencyBadge.Dock=DockStyle.Right; _latencyBadge.Width=112; StyleBadge(_latencyBadge); top.Controls.Add(_latencyBadge);
        _vigemBadge.Dock=DockStyle.Right; _vigemBadge.Width=115; StyleBadge(_vigemBadge); top.Controls.Add(_vigemBadge);
        _controllerBadge.Dock=DockStyle.Right; _controllerBadge.Width=150; StyleBadge(_controllerBadge); top.Controls.Add(_controllerBadge);
        _modeBadge.Dock=DockStyle.Left; _modeBadge.Width=190; StyleBadge(_modeBadge); _modeBadge.Font=new Font("Segoe UI Semibold",11,FontStyle.Bold); top.Controls.Add(_modeBadge);

        _content.Dock=DockStyle.Fill; _content.BackColor=_bg; _content.Padding=new Padding(24,8,24,24); _content.AutoScroll=true;
        Controls.Add(_content);
        _content.BringToFront(); top.BringToFront(); side.BringToFront();
    }

    private void AddNav(Panel side, string key, string text)
    {
        var b = new Button { Text=text, Dock=DockStyle.Top, Height=48, FlatStyle=FlatStyle.Flat, TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(12,0,0,0), ForeColor=_muted, BackColor=Color.Transparent, Cursor=Cursors.Hand };
        b.FlatAppearance.BorderSize=0; b.FlatAppearance.MouseOverBackColor=_panel2;
        b.Click += (_,_) => ShowPage(key);
        side.Controls.Add(b); b.BringToFront(); _nav[key]=b;
    }

    private void ShowPage(string page)
    {
        _page=page; _content.SuspendLayout(); _content.Controls.Clear();
        foreach(var kv in _nav){ kv.Value.ForeColor = kv.Key==page ? _accent : _muted; kv.Value.BackColor = kv.Key==page ? Color.FromArgb(24,34,46) : Color.Transparent; }
        Control p = page switch
        {
            "attack" => BuildAttack(), "defense" => BuildDefense(), "skills" => BuildSkills(),
            "controller" => BuildController(), "settings" => BuildSettings(), _ => BuildDashboard()
        };
        p.Dock=DockStyle.Top; _content.Controls.Add(p); _content.ResumeLayout();
    }

    private Control BuildDashboard()
    {
        var root=Stack(); root.Controls.Add(Title(T("FC27 Assist — لوحة التحكم","FC27 Assist — Control Center"),T("اختصارات سريعة، وضع هجوم/دفاع، وDirty Meta بدون تحليل شاشة.","Low-latency shortcuts, Attack/Defense modes, and Dirty Meta without screen analysis.")));
        var row=Row(3,190);
        row.Controls.Add(Card(T("الوضع الحالي","CURRENT MODE"), _engine.Mode==PlayMode.Attack?T("هجوم ⚡","ATTACK ⚡"):T("دفاع ◆","DEFENSE ◆"), T("LB للهجوم • LT للدفاع","LB Attack • LT Defense")));
        row.Controls.Add(Card(T("Dirty Meta","DIRTY META"), _cfg.DirtyMeta?T("مفعّل","ENABLED"):T("متوقف","OFF"), T("مفتاح واحد لكل إضافات الميتا","One switch for the full meta layer")));
        row.Controls.Add(Card(T("محرك اليد","CONTROLLER ENGINE"), _engine.Connected?T("متصل","CONNECTED"):T("غير متصل","DISCONNECTED"), T("XInput → Virtual Xbox","XInput → Virtual Xbox")));
        root.Controls.Add(row);

        var dirty=PanelCard(210); var lbl=BigLabel(T("DIRTY META — الوضع القذر","DIRTY META — FULL LAYER")); dirty.Controls.Add(lbl);
        var ddesc=new Label{Text=T("Fidget Snap + Explosive Exit + Faster Skill Chaining. الاختصارات الأساسية تظل شغالة حتى لو كان Dirty Meta متوقف.","Fidget Snap + Explosive Exit + faster skill chaining. Core shortcuts remain active even when Dirty Meta is off."),AutoSize=false,Height=55,Dock=DockStyle.Top,ForeColor=_muted}; dirty.Controls.Add(ddesc); ddesc.BringToFront(); lbl.BringToFront();
        var toggle=new Button{Text=_cfg.DirtyMeta?T("إيقاف Dirty Meta","TURN DIRTY META OFF"):T("تشغيل Dirty Meta كامل","ENABLE FULL DIRTY META"),Height=46,Width=250,Dock=DockStyle.Bottom}; StyleButton(toggle,_cfg.DirtyMeta); toggle.Click+=(_,_)=>{_cfg.DirtyMeta=!_cfg.DirtyMeta;SaveAndRefresh("dashboard");}; dirty.Controls.Add(toggle);
        root.Controls.Add(dirty);

        var info=PanelCard(170); info.Controls.Add(BigLabel(T("منع التعارض","CONFLICT CONTROL")));
        info.Controls.Add(new Label{Text=T("LT يحول فورًا لوضع الدفاع ويرجع RS لتبديل اللاعب. LB يحول للهجوم ويرجع اختصارات المهارات. RT وLS يظلون طبيعيين دائمًا.","LT immediately activates Defense and restores RS player switching. LB activates Attack and restores skill shortcuts. RT and LS remain native at all times."),AutoSize=false,Height=72,Dock=DockStyle.Fill,ForeColor=_muted,Padding=new Padding(0,12,0,0)});
        root.Controls.Add(info);
        return root;
    }

    private Control BuildAttack()
    {
        var root=Stack(); root.Controls.Add(Title(T("الهجوم","Attack Mode"),T("اضغط LB للدخول. القير اليمين يصبح لوحة مهارات سريعة.","Press LB to enter. The right stick becomes a fast skill pad.")));
        var grid=Row(2,360); grid.Controls.Add(BuildMappingCard(false)); grid.Controls.Add(BuildMappingCard(true)); root.Controls.Add(grid);
        var shot=PanelCard(235); shot.Controls.Add(BigLabel(T("التسديد والتمرير","SHOOTING & PASSING")));
        var txt=new Label{Dock=DockStyle.Fill,ForeColor=_muted,Text=T(
            $"B نقرة سريعة → Low Driven تلقائي\nB ضغط مستمر → شوت عادي قوي (حد القوة { _cfg.BNormalShotCapMs }ms)\nA → Driven Ground Pass (RB+A)\nY → Lobbed Through Pass (LB+Y)",
            $"Quick B tap → automatic Low Driven\nHold B → strong normal shot (power cap {_cfg.BNormalShotCapMs}ms)\nA → Driven Ground Pass (RB+A)\nY → Lobbed Through Pass (LB+Y)"),Padding=new Padding(0,12,0,0)}; shot.Controls.Add(txt);
        root.Controls.Add(shot); return root;
    }

    private Control BuildMappingCard(bool lb)
    {
        var p=PanelCard(340); p.Controls.Add(BigLabel(lb?T("LB + RS — الطبقة الثانية","LB + RS — SECOND LAYER"):T("RS — المهارات الأساسية","RS — CORE SKILLS")));
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(0,12,0,0)}; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,31)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,69));
        string[] dirs={"UP","RIGHT","LEFT","DOWN"}; string[] arrows={"↑","→","←","↓"};
        for(int i=0;i<4;i++)
        {
            string key=(lb?"LB_RS_":"RS_")+dirs[i];
            table.Controls.Add(new Label{Text=(lb?"LB + ":"")+"RS "+arrows[i],Dock=DockStyle.Fill,ForeColor=_muted,TextAlign=ContentAlignment.MiddleLeft},0,i);
            var cb=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=_panel2,ForeColor=_text,FlatStyle=FlatStyle.Flat};
            cb.Items.AddRange(SkillLibrary.Skills.Cast<object>().ToArray());
            var current=SkillLibrary.Get(_cfg.SkillMap.TryGetValue(key,out var n)?n:SkillLibrary.Skills[0].Name); cb.SelectedItem=SkillLibrary.Skills.First(s=>s.Name==current.Name);
            cb.SelectedIndexChanged+=(_,_)=>{if(cb.SelectedItem is SkillDef sd){_cfg.SkillMap[key]=sd.Name;_cfg.Save();_engine.UpdateConfig(_cfg);}};
            table.Controls.Add(cb,1,i);
        }
        p.Controls.Add(table); return p;
    }

    private Control BuildDefense()
    {
        var root=Stack(); root.Controls.Add(Title(T("الدفاع","Defense Mode"),T("اضغط LT: مساعدات دفاعية بدون Auto Tackle أو تحريك اللاعب بدالك.","Press LT: defensive assistance without auto-tackling or moving your defender for you.")));
        var p=PanelCard(350); p.Controls.Add(BigLabel(T("مساعد الدفاع","DEFENSE ASSIST")));
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=5,Padding=new Padding(0,12,0,0)}; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        AddToggleRow(table,0,T("ضغط اللاعب الثاني التلقائي","Smart Second-Man Press"),_cfg.AutoPress,v=>{_cfg.AutoPress=v;SaveCfg();});
        var pc=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=_panel2,ForeColor=_text,FlatStyle=FlatStyle.Flat}; pc.Items.AddRange(new object[]{"Low","Balanced","Aggressive"});pc.SelectedItem=_cfg.PressureStrength;pc.SelectedIndexChanged+=(_,_)=>{_cfg.PressureStrength=pc.SelectedItem?.ToString()??"Balanced";SaveCfg();}; AddRow(table,1,T("قوة الضغط","Pressure strength"),pc);
        AddToggleRow(table,2,T("Sprint Jockey Assist","Sprint Jockey Assist"),_cfg.SprintJockeyAssist,v=>{_cfg.SprintJockeyAssist=v;SaveCfg();});
        AddToggleRow(table,3,T("Hard Tackle عند الضغط المطول","Hard Tackle on hold"),_cfg.HardTackleAssist,v=>{_cfg.HardTackleAssist=v;SaveCfg();});
        AddRow(table,4,T("RS في الدفاع","RS in Defense"),new Label{Text=T("تبديل لاعب طبيعي 100%","100% native player switching"),Dock=DockStyle.Fill,ForeColor=_accent,TextAlign=ContentAlignment.MiddleLeft});
        p.Controls.Add(table); root.Controls.Add(p);
        var note=PanelCard(150);note.Controls.Add(BigLabel(T("الأولوية لك","YOU KEEP CONTROL")));note.Controls.Add(new Label{Dock=DockStyle.Fill,Text=T("البرنامج لا يسوي Tackle من نفسه. LS وRT والحركة والتدخل النهائي كلها بيدك. الضغط فقط يساعد لاعبًا ثانيًا.","The app never tackles on its own. LS, RT, movement and the final tackle remain yours; press assistance only uses the second defender."),ForeColor=_muted,Padding=new Padding(0,12,0,0)});root.Controls.Add(note);
        return root;
    }

    private Control BuildSkills()
    {
        var root=Stack(); root.Controls.Add(Title(T("مكتبة المهارات","Skill Library"),T("كل المهارات الموجودة في هذه النسخة قابلة للربط بأي اتجاه من صفحة الهجوم.","Every skill in this build can be assigned to any shortcut from Attack.")));
        var box=PanelCard(Math.Max(520, SkillLibrary.Skills.Count*28+90)); box.Controls.Add(BigLabel($"{T("المهارات المتاحة","AVAILABLE SKILLS")} — {SkillLibrary.Skills.Count}"));
        var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,BackColor=_panel2,ForeColor=_text,BorderStyle=BorderStyle.None,HeaderStyle=ColumnHeaderStyle.Nonclickable};
        list.Columns.Add(T("المهارة","Skill"),280);list.Columns.Add(T("النجوم","Stars"),80);list.Columns.Add(T("الفئة","Category"),120);list.Columns.Add("FC27",70);list.Columns.Add("Trickster",90);
        foreach(var s in SkillLibrary.Skills){var it=new ListViewItem(s.Name);it.SubItems.Add(new string('★',s.Stars));it.SubItems.Add(s.Category);it.SubItems.Add(s.NewFc27?"NEW":"");it.SubItems.Add(s.Trickster?"YES":"");list.Items.Add(it);} box.Controls.Add(list); root.Controls.Add(box);return root;
    }

    private Control BuildController()
    {
        var root=Stack();root.Controls.Add(Title(T("اختبار يد Xbox","Xbox Controller Test"),T("راقب الإدخال الخام والوضع الحالي قبل فتح المباراة.","Watch raw input and current mode before opening a match.")));
        var p=PanelCard(390);p.Controls.Add(BigLabel(T("Live Input","LIVE INPUT")));
        var live=new Label{Name="liveInput",Dock=DockStyle.Fill,Font=new Font("Consolas",12),ForeColor=_cyan,Padding=new Padding(0,15,0,0)};p.Controls.Add(live);root.Controls.Add(p);
        var h=PanelCard(155);h.Controls.Add(BigLabel(T("مهم لمنع الإدخال المكرر","IMPORTANT: PREVENT DOUBLE INPUT")));var l=new Label{Dock=DockStyle.Fill,ForeColor=_muted,Text=T("استخدم HidHide لإخفاء اليد الحقيقية عن FC27 والسماح لـ FC27Assist برؤيتها. إذا اللعبة شافت اليد الحقيقية والافتراضية معًا قد تحصل ضغطات مزدوجة.","Use HidHide to hide the physical controller from FC27 while whitelisting FC27Assist. If the game sees both physical and virtual pads, duplicate input can occur."),Padding=new Padding(0,10,0,0)};h.Controls.Add(l);root.Controls.Add(h);return root;
    }

    private Control BuildSettings()
    {
        var root=Stack();root.Controls.Add(Title(T("الإعدادات الدقيقة","Precision Settings"),T("لا تغيّر التوقيت إلا بعد الاختبار في Practice Arena.","Only tune timing after testing in Practice Arena.")));
        var p=PanelCard(585);p.Controls.Add(BigLabel(T("التوقيت والإدخال","TIMING & INPUT")));
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=11,Padding=new Padding(0,12,0,0)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));
        AddNumeric(table,0,T("XInput Slot (0-3)","XInput Slot (0-3)"),_cfg.ControllerSlot,0,3,v=>_cfg.ControllerSlot=v);
        AddNumeric(table,1,T("RS Trigger Deadzone","RS Trigger Deadzone"),_cfg.RsTriggerDeadzone,10000,30000,v=>_cfg.RsTriggerDeadzone=v);
        AddNumeric(table,2,T("Skill Step (ms)","Skill Step (ms)"),_cfg.SkillStepMs,25,100,v=>_cfg.SkillStepMs=v);
        AddNumeric(table,3,T("Skill Cooldown (ms)","Skill Cooldown (ms)"),_cfg.SkillCooldownMs,60,300,v=>_cfg.SkillCooldownMs=v);
        AddNumeric(table,4,T("B Tap Threshold (ms)","B Tap Threshold (ms)"),_cfg.BTapThresholdMs,90,300,v=>_cfg.BTapThresholdMs=v);
        AddNumeric(table,5,T("Low Driven Charge (ms)","Low Driven Charge (ms)"),_cfg.LowDrivenChargeMs,220,700,v=>_cfg.LowDrivenChargeMs=v);
        AddNumeric(table,6,T("Normal Shot Power Cap (ms)","Normal Shot Power Cap (ms)"),_cfg.BNormalShotCapMs,300,1000,v=>_cfg.BNormalShotCapMs=v);
        AddNumeric(table,7,T("Low Driven Gap (ms)","Low Driven Gap (ms)"),_cfg.LowDrivenSecondTapGapMs,10,100,v=>_cfg.LowDrivenSecondTapGapMs=v);
        AddNumeric(table,8,T("Low Driven 2nd Tap (ms)","Low Driven 2nd Tap (ms)"),_cfg.LowDrivenSecondTapMs,20,100,v=>_cfg.LowDrivenSecondTapMs=v);
        AddNumeric(table,9,T("LB Skill Chord Window (ms)","LB Skill Chord Window (ms)"),_cfg.LbChordWindowMs,30,160,v=>_cfg.LbChordWindowMs=v);
        AddNumeric(table,10,T("Input Loop Hz","Input Loop Hz"),_cfg.InputLoopHz,250,1000,v=>_cfg.InputLoopHz=v);
        p.Controls.Add(table);root.Controls.Add(p);
        var buttons=PanelCard(135);var reset=new Button{Text=T("استعادة الإعدادات الافتراضية","RESET DEFAULTS"),Dock=DockStyle.Left,Width=220};StyleButton(reset,false);reset.Click+=(_,_)=>{var fresh=new AppConfig{Language=_cfg.Language};CopyConfig(fresh,_cfg);SaveAndRefresh("settings");};buttons.Controls.Add(reset);var save=new Button{Text=T("حفظ","SAVE"),Dock=DockStyle.Right,Width=180};StyleButton(save,true);save.Click+=(_,_)=>SaveCfg();buttons.Controls.Add(save);root.Controls.Add(buttons);return root;
    }

    private void CopyConfig(AppConfig src, AppConfig dst)
    {
        var lang=dst.Language; var fresh=src;
        dst.ControllerSlot=fresh.ControllerSlot;dst.DirtyMeta=fresh.DirtyMeta;dst.RsTriggerDeadzone=fresh.RsTriggerDeadzone;dst.RsReleaseDeadzone=fresh.RsReleaseDeadzone;dst.SkillStepMs=fresh.SkillStepMs;dst.SkillCooldownMs=fresh.SkillCooldownMs;dst.BTapThresholdMs=fresh.BTapThresholdMs;dst.LowDrivenChargeMs=fresh.LowDrivenChargeMs;dst.BNormalShotCapMs=fresh.BNormalShotCapMs;dst.LowDrivenSecondTapGapMs=fresh.LowDrivenSecondTapGapMs;dst.LowDrivenSecondTapMs=fresh.LowDrivenSecondTapMs;dst.LbChordWindowMs=fresh.LbChordWindowMs;dst.InputLoopHz=fresh.InputLoopHz;dst.AutoPress=fresh.AutoPress;dst.PressureStrength=fresh.PressureStrength;dst.SprintJockeyAssist=fresh.SprintJockeyAssist;dst.HardTackleAssist=fresh.HardTackleAssist;dst.SkillMap=new Dictionary<string,string>(fresh.SkillMap);dst.Language=lang;
    }

    private void AddNumeric(TableLayoutPanel t,int row,string name,int val,int min,int max,Action<int> set)
    {
        var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Clamp(val,min,max),Dock=DockStyle.Fill,BackColor=_panel2,ForeColor=_text,BorderStyle=BorderStyle.FixedSingle};n.ValueChanged+=(_,_)=>{set((int)n.Value);SaveCfg();};AddRow(t,row,name,n);
    }
    private void AddToggleRow(TableLayoutPanel t,int row,string name,bool value,Action<bool> change)
    {
        var c=new CheckBox{Checked=value,Text=value?T("مفعّل","ON"):T("متوقف","OFF"),Dock=DockStyle.Fill,ForeColor=value?_accent:_muted,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter};c.FlatAppearance.BorderSize=0;c.CheckedChanged+=(_,_)=>{c.Text=c.Checked?T("مفعّل","ON"):T("متوقف","OFF");c.ForeColor=c.Checked?_accent:_muted;change(c.Checked);};AddRow(t,row,name,c);
    }
    private void AddRow(TableLayoutPanel t,int row,string name,Control c){t.RowStyles.Add(new RowStyle(SizeType.Absolute,52));t.Controls.Add(new Label{Text=name,Dock=DockStyle.Fill,ForeColor=_text,TextAlign=ContentAlignment.MiddleLeft},0,row);t.Controls.Add(c,1,row);}

    private FlowLayoutPanel Stack()=>new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Width=Math.Max(760,_content.ClientSize.Width-50),Padding=new Padding(0)};
    private TableLayoutPanel Row(int cols,int height){var t=new TableLayoutPanel{Width=Math.Max(760,_content.ClientSize.Width-50),Height=height,ColumnCount=cols,Margin=new Padding(0,0,0,16)};for(int i=0;i<cols;i++)t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/cols));return t;}
    private Panel PanelCard(int h)=>new(){Height=h,Width=Math.Max(760,_content.ClientSize.Width-50),BackColor=_panel,Padding=new Padding(20),Margin=new Padding(0,0,0,16)};
    private Label BigLabel(string s)=>new(){Text=s,Dock=DockStyle.Top,Height=38,Font=new Font("Segoe UI Semibold",13,FontStyle.Bold),ForeColor=_text};
    private Control Card(string top,string big,string small){var p=new Panel{Dock=DockStyle.Fill,BackColor=_panel,Padding=new Padding(18),Margin=new Padding(0,0,12,0)};p.Controls.Add(new Label{Text=small,Dock=DockStyle.Bottom,Height=35,ForeColor=_muted});p.Controls.Add(new Label{Text=big,Dock=DockStyle.Fill,Font=new Font("Segoe UI Semibold",17,FontStyle.Bold),ForeColor=_accent,TextAlign=ContentAlignment.MiddleLeft});p.Controls.Add(new Label{Text=top,Dock=DockStyle.Top,Height=32,ForeColor=_muted});return p;}
    private Control Title(string title,string sub){var p=new Panel{Width=Math.Max(760,_content.ClientSize.Width-50),Height=105,Margin=new Padding(0,0,0,12)};p.Controls.Add(new Label{Text=sub,Dock=DockStyle.Bottom,Height=38,ForeColor=_muted,Font=new Font("Segoe UI",10.5f)});p.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,Height=54,ForeColor=_text,Font=new Font("Segoe UI Semibold",22,FontStyle.Bold)});return p;}
    private void StyleButton(Button b,bool active){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=active?_accent:_panel2;b.ForeColor=active?Color.FromArgb(8,24,18):_text;b.Cursor=Cursors.Hand;b.Font=new Font("Segoe UI Semibold",10,FontStyle.Bold);}
    private void StyleBadge(Label l){l.Margin=new Padding(6,0,0,0);l.Padding=new Padding(8);l.ForeColor=_muted;l.TextAlign=ContentAlignment.MiddleCenter;l.BackColor=_panel;}

    private void UpdateStatus()
    {
        if (IsDisposed) return;
        _modeBadge.Text=_engine.Mode==PlayMode.Attack?T("⚡ وضع الهجوم","⚡ ATTACK MODE"):T("◆ وضع الدفاع","◆ DEFENSE MODE");_modeBadge.ForeColor=_engine.Mode==PlayMode.Attack?_accent:_cyan;
        _controllerBadge.Text=_engine.Connected?T("● اليد متصلة","● Controller OK"):T("○ اليد غير متصلة","○ Controller Lost");_controllerBadge.ForeColor=_engine.Connected?_accent:Color.OrangeRed;
        _vigemBadge.Text=_engine.ViGEmReady?"● ViGEm OK":"○ ViGEm";_vigemBadge.ForeColor=_engine.ViGEmReady?_accent:Color.OrangeRed;
        _latencyBadge.Text=$"Loop {_engine.LoopHz:0} Hz";_latencyBadge.ForeColor=_engine.LoopHz>300?_accent:_muted;
        if(_page=="controller")
        {
            var live=FindByName(_content,"liveInput") as Label;if(live!=null){var p=_engine.LastPhysical;live.Text=$"Mode      : {_engine.Mode}\nButtons   : 0x{p.Buttons:X4}\nLT / RT   : {p.LeftTrigger,3} / {p.RightTrigger,3}\nLS        : {p.ThumbLX,6} , {p.ThumbLY,6}\nRS        : {p.ThumbRX,6} , {p.ThumbRY,6}\nLoop      : {_engine.LoopHz:0} Hz\nLast      : {_engine.LastAction}\nViGEm     : {(_engine.ViGEmReady?"READY":"NOT READY")}\nError     : {_engine.Error}";}
        }
    }

    private static Control? FindByName(Control root,string name){if(root.Name==name)return root;foreach(Control c in root.Controls){var f=FindByName(c,name);if(f!=null)return f;}return null;}
    private void SaveCfg(){_cfg.Save();_engine.UpdateConfig(_cfg);}
    private void SaveAndRefresh(string page){SaveCfg();ShowPage(page);UpdateStatus();}
    private void ToggleLanguage(){_cfg.Language=Ar?"en":"ar";_cfg.Save();BuildLanguageRefresh();}
    private void BuildLanguageRefresh(){_langBtn.Text=Ar?"EN":"عربي";foreach(var kv in _nav){kv.Value.Text=kv.Key switch{"dashboard"=>"◈  "+T("الرئيسية","Dashboard"),"attack"=>"⚡  "+T("الهجوم","Attack"),"defense"=>"◆  "+T("الدفاع","Defense"),"skills"=>"✦  "+T("مكتبة المهارات","Skill Library"),"controller"=>"◎  "+T("اختبار اليد","Controller Test"),_=>"⚙  "+T("الإعدادات","Settings")};}ShowPage(_page);}
}
