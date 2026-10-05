using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace FC27Assist;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => CrashLogger.Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) CrashLogger.Log(ex);
            };
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex);
            try
            {
                MessageBox.Show(
                    "FC27 Assist could not start. A crash log was saved to:\n" + CrashLogger.Path,
                    "FC27 Assist",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}

public static class CrashLogger
{
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FC27Assist",
        "crash.log");

    public static void Log(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n----------------------------------------\r\n");
        }
        catch { }
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

    public static bool TryFindFirst(out int slot, out XInputState state)
    {
        for (int i = 0; i < 4; i++)
        {
            if (TryGetState(i, out state))
            {
                slot = i;
                return true;
            }
        }
        slot = -1;
        state = default;
        return false;
    }
}

public sealed class AppConfig
{
    public string Language { get; set; } = "ar";
    public int ControllerSlot { get; set; } = 0;
    public bool DirtyMeta { get; set; } = true;
    public int RsTriggerDeadzone { get; set; } = 18500;
    public int RsReleaseDeadzone { get; set; } = 9000;
    public int RsRearmMs { get; set; } = 90;
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

    public Dictionary<string, string> SkillMap { get; set; } = DefaultAttackSkillMap();

    public static Dictionary<string, string> DefaultAttackSkillMap() => new()
    {
        ["RS_UP"] = "Heel Flick",
        ["RS_RIGHT"] = "Ball Roll Spin Right",
        ["RS_LEFT"] = "Ball Roll Spin Left",
        ["RS_DOWN"] = "Simple Rainbow"
    };

    public void NormalizeAttackOnly()
    {
        DirtyMeta = true;
        AutoPress = false;
        SprintJockeyAssist = false;
        HardTackleAssist = false;

        var defaults = DefaultAttackSkillMap();
        var keys = defaults.Keys.ToArray();
        SkillMap ??= new Dictionary<string, string>();

        foreach (var old in SkillMap.Keys.Where(k => k.StartsWith("LB_RS_", StringComparison.OrdinalIgnoreCase)).ToArray())
            SkillMap.Remove(old);

        foreach (var key in keys)
        {
            if (!SkillMap.TryGetValue(key, out var name) || !SkillLibrary.IsRsOnlySafe(name))
                SkillMap[key] = defaults[key];
        }

        if (keys.Select(k => SkillMap[k]).Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Length)
            SkillMap = defaults;
    }

    private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FC27Assist");
    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            var cfg = !File.Exists(ConfigPath)
                ? new AppConfig()
                : JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
            cfg.NormalizeAttackOnly();
            return cfg;
        }
        catch
        {
            var cfg = new AppConfig();
            cfg.NormalizeAttackOnly();
            return cfg;
        }
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

    public static bool IsRsOnlySafe(string name)
    {
        var skill = Skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (skill is null) return false;
        var steps = skill.Build(52);
        return steps.All(step =>
            step.Down == 0 &&
            step.Up == 0 &&
            !step.Lt.HasValue &&
            !step.Rt.HasValue &&
            step.Ls == Dir.None &&
            !step.NeutralLs);
    }

    public static IReadOnlyList<SkillDef> RsOnlySafeSkills =>
        Skills.Where(s => IsRsOnlySafe(s.Name)).ToList();

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
            Dir.ForwardRight => Math.PI / 4,
            Dir.Right => Math.PI / 2,
            Dir.BackRight => 3 * Math.PI / 4,
            Dir.Back => Math.PI,
            Dir.BackLeft => -3 * Math.PI / 4,
            Dir.Left => -Math.PI / 2,
            Dir.ForwardLeft => -Math.PI / 4,
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


public sealed record UpdateManifest(string Version, string Url, string Sha256, string Commit);

public static class AutoUpdater
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/JaguarD7/JaguarD7/fc27-assist-dist/version.json";
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static bool IsNewer(string latest, string current)
        => Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;

    public static async Task<bool> CheckAndApplyAsync(IWin32Window owner, bool interactive)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FC27Assist-Updater/1.0");

            var manifestJson = await http.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.Url) || string.IsNullOrWhiteSpace(manifest.Sha256))
                throw new InvalidDataException("Update manifest is invalid.");

            if (!IsNewer(manifest.Version, CurrentVersion))
            {
                if (interactive)
                    MessageBox.Show($"أنت على آخر إصدار ({CurrentVersion}).", "FC27 Assist Update",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FC27AssistUpdate_" + Guid.NewGuid().ToString("N"));
            var zipPath = System.IO.Path.Combine(root, "update.zip");
            var extract = System.IO.Path.Combine(root, "package");
            Directory.CreateDirectory(root);

            using (var response = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zipPath);
                await src.CopyToAsync(dst);
            }

            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update SHA-256 verification failed.");

            ZipFile.ExtractToDirectory(zipPath, extract, true);
            var newExe = System.IO.Path.Combine(extract, "FC27Assist.exe");
            if (!File.Exists(newExe))
                throw new FileNotFoundException("Updated FC27Assist.exe was not found in the package.");

            string target = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            string script = System.IO.Path.Combine(root, "apply-update.ps1");
            string targetExe = System.IO.Path.Combine(target, "FC27Assist.exe");
            int pid = Environment.ProcessId;

            static string Ps(string value) => "'" + value.Replace("'", "''") + "'";
            var scriptText =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$pidToWait = " + pid + "\r\n" +
                "$source = " + Ps(extract) + "\r\n" +
                "$target = " + Ps(target) + "\r\n" +
                "$targetExe = " + Ps(targetExe) + "\r\n" +
                "$log = Join-Path $env:APPDATA 'FC27Assist\\update.log'\r\n" +
                "try {\r\n" +
                "    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }\r\n" +
                "    Start-Sleep -Milliseconds 300\r\n" +
                "    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force\r\n" +
                "    Start-Process -FilePath $targetExe\r\n" +
                "} catch {\r\n" +
                "    New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null\r\n" +
                "    Add-Content -Path $log -Value ('[' + (Get-Date) + '] ' + $_.Exception.ToString())\r\n" +
                "}\r\n";
            File.WriteAllText(script, scriptText);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            MessageBox.Show(
                $"تم العثور على تحديث {manifest.Version}. سيتم تثبيته الآن وإعادة فتح البرنامج تلقائيًا.",
                "FC27 Assist Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex);
            if (interactive)
                MessageBox.Show("تعذر فحص/تثبيت التحديث:\n" + ex.Message,
                    "FC27 Assist Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}

public sealed record SingleControllerState(
    bool HidHideInstalled,
    bool AppWhitelisted,
    bool CloakOn,
    bool DeviceHidden,
    bool RequiresReconnect,
    bool Ready,
    string Message,
    int GamingGroups,
    int HiddenDevices);

public static class HidHideManager
{
    private static readonly string[] CliCandidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions e.U.", "HidHideCLI", "HidHideCLI.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions e.U", "HidHide", "x64", "HidHideCLI.exe")
    };

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool Success => ExitCode == 0;
        public string Combined => (StdOut + Environment.NewLine + StdErr).Trim();
    }

    private sealed record GamingGroup(string FriendlyName, List<string> Paths);

    public static string? FindCli()
    {
        var exact = CliCandidates.FirstOrDefault(File.Exists);
        if (exact is not null) return exact;

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)))
        {
            try
            {
                foreach (var vendorDir in Directory.EnumerateDirectories(root, "Nefarius*"))
                {
                    var found = Directory.EnumerateFiles(vendorDir, "HidHideCLI.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found is not null) return found;
                }
            }
            catch { }
        }
        return null;
    }

    private static CliResult Run(string cli, params string[] args)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = cli,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) p.StartInfo.ArgumentList.Add(arg);
            p.Start();
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(true); } catch { }
                return new CliResult(-1, stdout, "HidHideCLI timed out.");
            }
            return new CliResult(p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new CliResult(-1, "", ex.Message);
        }
    }

    private static List<GamingGroup> Gaming(string cli)
    {
        var result = Run(cli, "--dev-gaming");
        if (!result.Success || string.IsNullOrWhiteSpace(result.StdOut))
            return new();

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var groups = new List<GamingGroup>();
            foreach (var group in doc.RootElement.EnumerateArray())
            {
                string name = group.TryGetProperty("friendlyName", out var fn) ? fn.GetString() ?? "Controller" : "Controller";
                var paths = new List<string>();
                if (group.TryGetProperty("devices", out var devices))
                {
                    foreach (var device in devices.EnumerateArray())
                    {
                        bool present = device.TryGetProperty("present", out var pr) && pr.GetBoolean();
                        bool gaming = device.TryGetProperty("gamingDevice", out var gd) && gd.GetBoolean();
                        if (!present || !gaming) continue;
                        if (device.TryGetProperty("deviceInstancePath", out var dp))
                        {
                            var value = dp.GetString();
                            if (!string.IsNullOrWhiteSpace(value)) paths.Add(value);
                        }
                    }
                }
                if (paths.Count > 0)
                    groups.Add(new GamingGroup(name, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
            }
            return groups;
        }
        catch
        {
            return new();
        }
    }

    private static HashSet<string> Hidden(string cli)
    {
        var result = Run(cli, "--dev-list");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!result.Success) return set;

        foreach (var raw in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            const string prefix = "--dev-hide \"";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !line.EndsWith("\"")) continue;
            set.Add(line[prefix.Length..^1]);
        }
        return set;
    }

    public static SingleControllerState Prepare()
    {
        string? cli = FindCli();
        if (cli is null)
            return new(false, false, false, false, false, false,
                "HidHide is not installed. Single Controller Mode is required so FC27 sees only one controller.", 0, 0);

        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var appList = Run(cli, "--app-list");
        bool appRegistered = appList.Success &&
            appList.StdOut.Contains(exe, StringComparison.OrdinalIgnoreCase);

        if (!appRegistered)
        {
            var reg = Run(cli, "--app-reg", exe);
            if (!reg.Success)
                return new(true, false, false, false, false, false,
                    "HidHide could not whitelist FC27Assist: " + reg.Combined, 0, 0);
            appRegistered = true;
        }

        // Normal cloak mode: hidden devices are invisible to every process except whitelisted apps.
        var inverseOff = Run(cli, "--inv-off");
        var inverseState = Run(cli, "--inv-state");
        if (!inverseOff.Success || !inverseState.Success ||
            !inverseState.StdOut.Contains("--inv-off", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, appRegistered, false, false, false, false,
                "HidHide inverse cloak could not be disabled safely: " + inverseOff.Combined, 0, 0);
        }

        var groups = Gaming(cli);
        var hidden = Hidden(cli);

        GamingGroup? target = null;
        if (groups.Count == 1)
        {
            target = groups[0];
        }
        else if (groups.Count > 1)
        {
            var matches = groups.Where(g => g.Paths.Any(hidden.Contains)).ToList();
            if (matches.Count == 1) target = matches[0];
        }

        bool changed = false;
        if (target is not null)
        {
            foreach (var devicePath in target.Paths)
            {
                if (hidden.Contains(devicePath)) continue;
                var hide = Run(cli, "--dev-hide", devicePath);
                if (!hide.Success)
                    return new(true, appRegistered, false, false, false, false,
                        "HidHide could not hide the physical controller: " + hide.Combined, groups.Count, hidden.Count);
                hidden.Add(devicePath);
                changed = true;
            }
        }
        else if (groups.Count > 1 && hidden.Count == 0)
        {
            return new(true, appRegistered, false, false, false, false,
                "More than one physical gaming controller is connected. Disconnect the extras once, then rescan.",
                groups.Count, hidden.Count);
        }

        var cloak = Run(cli, "--cloak-on");
        if (!cloak.Success)
            return new(true, appRegistered, false, hidden.Count > 0, changed, false,
                "HidHide could not enable device cloaking: " + cloak.Combined, groups.Count, hidden.Count);

        var cloakState = Run(cli, "--cloak-state");
        bool cloakOn = cloakState.Success && cloakState.StdOut.Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);
        bool deviceHidden = target is not null ? target.Paths.All(hidden.Contains) : hidden.Count > 0;

        int xinputSlot = -1;
        for (int i = 0; i < 4; i++)
        {
            if (XInputNative.TryGetState(i, out _)) { xinputSlot = i; break; }
        }

        bool ready = appRegistered && cloakOn && deviceHidden && !changed && xinputSlot >= 0;

        string message = changed
            ? "Single Controller Mode configured. Unplug/replug the wired controller once; FC27Assist will rescan automatically."
            : ready
                ? $"Single Controller Mode ready. Physical controller detected on XInput slot {xinputSlot}."
                : xinputSlot < 0 && deviceHidden
                    ? "Controller is hidden correctly, but FC27Assist cannot read it yet. Reconnect the USB cable; auto-rescan is active."
                    : groups.Count == 0 && hidden.Count == 0
                        ? "No physical Xbox-compatible controller detected."
                        : "Single Controller Mode is not fully ready.";

        return new(true, appRegistered, cloakOn, deviceHidden, changed, ready, message, groups.Count, hidden.Count);
    }
}

public sealed class ControllerEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly object _outputGate = new();
    private readonly MacroRunner _macro = new();
    private AppConfig _cfg;
    private Thread? _thread;
    private volatile bool _running;
    private ViGEmClient? _client;
    private IXbox360Controller? _virtual;
    private Action? _submit;
    private int _physicalSlot = -1;
    private bool _controllerConnected;
    private bool _vigemReady;
    private ushort _prevButtons;
    private bool _rsLatched;
    private bool _rsNeedsCenter;
    private long _rsCenterSince;
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
    private bool _blockLtUntilRelease;
    private bool _blockDefenseRsUntilCenter;
    private long _defensePressBlockUntil;
    private double _prevRsMagnitude;
    private long _physicalLostSince;
    private long _lastVirtualRetry;
    private long _lastLoopHeartbeat;
    private System.Threading.Timer? _watchdog;

    public PlayMode Mode { get; private set; } = PlayMode.Attack;
    public bool Connected => _controllerConnected;
    public int PhysicalSlot => _physicalSlot;
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

        // Resolve the physical controller BEFORE ViGEm exists. Once the virtual pad is
        // connected, a blind XInput scan could accidentally lock onto our own output.
        _physicalSlot = ResolvePhysicalSlot(_cfg.ControllerSlot);
        if (_physicalSlot < 0)
        {
            Error = "No physical Xbox controller detected. Connect the wired controller, then rescan.";
            _vigemReady = false;
            _controllerConnected = false;
            StatusChanged?.Invoke();
            return;
        }

        _controllerConnected = true;
        CreateVirtualController();

        _running = true;
        _lastLoopHeartbeat = Stopwatch.GetTimestamp();
        _watchdog = new System.Threading.Timer(_ =>
        {
            if (!_running) return;
            try
            {
                if (_lastLoopHeartbeat != 0 && MsSince(_lastLoopHeartbeat) > 250)
                    SendNeutral();
            }
            catch { }
        }, null, 250, 100);

        _thread = new Thread(Loop) { IsBackground = true, Name = "FC27Assist.Input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private bool CreateVirtualController()
    {
        lock (_outputGate)
        {
            try
            {
                if (_client is null)
                    _client = new ViGEmClient();

                _virtual = _client.CreateXbox360Controller();
                _virtual.Connect();

                var t = _virtual.GetType();
                var m = t.GetMethod("SubmitReport");
                if (m is null)
                    throw new MissingMethodException("ViGEm Xbox360Controller.SubmitReport was not found.");

                t.GetProperty("AutoSubmitReport")?.SetValue(_virtual, false);
                _submit = (Action)Delegate.CreateDelegate(typeof(Action), _virtual, m);
                _vigemReady = true;
                Error = "";
                StatusChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Error = "ViGEm: " + ex.Message;
                _vigemReady = false;
                _submit = null;
                StatusChanged?.Invoke();
                return false;
            }
        }
    }

    private void DestroyVirtualController()
    {
        lock (_outputGate)
        {
            try { WriteReportUnsafe(new VirtualReport()); } catch { }
            try { _virtual?.Disconnect(); } catch { }
            try { (_virtual as IDisposable)?.Dispose(); } catch { }
            _virtual = null;
            _submit = null;
            _vigemReady = false;
        }
        StatusChanged?.Invoke();
    }

    private static int ResolvePhysicalSlot(int preferred)
    {
        if (preferred >= 0 && preferred <= 3 && XInputNative.TryGetState(preferred, out _))
            return preferred;

        for (int i = 0; i < 4; i++)
            if (XInputNative.TryGetState(i, out _))
                return i;

        return -1;
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
        _rsNeedsCenter = false;
        _rsCenterSince = 0;
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
            _lastLoopHeartbeat = Stopwatch.GetTimestamp();

            try
            {
                AppConfig cfg; lock (_gate) cfg = _cfg;

                // Never scan all XInput slots while the ViGEm pad exists. Doing so can
                // select our own virtual controller and create a feedback/stuck-input loop.
                if (_physicalSlot < 0 || !XInputNative.TryGetState(_physicalSlot, out var state))
                {
                    HandlePhysicalLoss(cfg);
                    nextTick = Stopwatch.GetTimestamp();
                    continue;
                }

                _physicalLostSince = 0;
                if (!_controllerConnected)
                {
                    _controllerConnected = true;
                    LastAction = "Physical controller recovered";
                    StatusChanged?.Invoke();
                }

                // If ViGEm failed independently, retry it while keeping physical input safe.
                if (!_vigemReady && (_lastVirtualRetry == 0 || MsSince(_lastVirtualRetry) >= 1000))
                {
                    _lastVirtualRetry = Stopwatch.GetTimestamp();
                    DestroyVirtualController();
                    CreateVirtualController();
                }

                var p = state.Gamepad;
                var r = ProcessFrameForTest(p, cfg);
                Send(r);

                ticks++;
                if (MsSince(hzStart) >= 1000)
                {
                    LoopHz = ticks * 1000.0 / Math.Max(1, MsSince(hzStart));
                    hzStart = Stopwatch.GetTimestamp();
                    ticks = 0;
                }

                var targetHz = Math.Clamp(cfg.InputLoopHz, 250, 1000);
                var tickTicks = Math.Max(1L, Stopwatch.Frequency / targetHz);
                nextTick += tickTicks;
                var nowTicks = Stopwatch.GetTimestamp();

                if (nowTicks - nextTick > tickTicks * 4)
                    nextTick = nowTicks + tickTicks;

                while (_running)
                {
                    var remain = nextTick - Stopwatch.GetTimestamp();
                    if (remain <= 0) break;
                    if (remain > Stopwatch.Frequency / 700)
                        Thread.Sleep(1);
                    else
                        Thread.SpinWait(32);
                }
            }
            catch (Exception ex)
            {
                // A runtime exception must never leave the last LS/RS/button state held.
                CrashLogger.Log(ex);
                Error = "Input loop recovered: " + ex.Message;
                SendNeutral();
                ResetInputStateForTest();
                StatusChanged?.Invoke();
                Thread.Sleep(20);
                nextTick = Stopwatch.GetTimestamp();
            }
        }

        SendNeutral();
    }

    private void HandlePhysicalLoss(AppConfig cfg)
    {
        if (_physicalLostSince == 0)
        {
            _physicalLostSince = Stopwatch.GetTimestamp();
            SendNeutral();
            ResetInputStateForTest();

            if (_controllerConnected)
            {
                _controllerConnected = false;
                LastAction = "Physical controller signal lost — neutralized";
                StatusChanged?.Invoke();
            }
        }

        // Short USB/XInput hiccup: only wait for the SAME physical slot.
        // Do not scan other slots while ViGEm exists.
        if (MsSince(_physicalLostSince) < 300)
        {
            Thread.Sleep(5);
            return;
        }

        // Safe recovery: remove the virtual pad first. Only then may we scan all
        // XInput slots, because any remaining XInput pad must be the real device.
        if (_virtual is not null || _vigemReady)
        {
            DestroyVirtualController();
            Thread.Sleep(80);
        }

        if (XInputNative.TryFindFirst(out var foundSlot, out _))
        {
            _physicalSlot = foundSlot;
            _physicalLostSince = 0;
            _controllerConnected = true;
            LastAction = "Physical controller re-acquired on slot " + foundSlot;
            Error = "";
            ResetInputStateForTest();
            CreateVirtualController();
            StatusChanged?.Invoke();
            return;
        }

        _physicalSlot = -1;
        Thread.Sleep(40);
    }

    public VirtualReport ProcessFrameForTest(XInputGamepad p, AppConfig? cfgOverride = null)
    {
        AppConfig cfg = cfgOverride ?? _cfg;
        LastPhysical = p;

        // ATTACK-ONLY MODE:
        // LB/LT/RT/LS remain native at all times. The app never consumes LB
        // and never uses it as a mode switch or skill modifier.
        UpdateFacing(p);

        var r = new VirtualReport
        {
            Buttons = p.Buttons,
            LT = p.LeftTrigger,
            RT = p.RightTrigger,
            LX = p.ThumbLX,
            LY = p.ThumbLY,
            RX = p.ThumbRX,
            RY = p.ThumbRY
        };

        ApplyAttack(p, r, cfg);

        if (_macro.Active)
        {
            _macro.Apply(r);

            // Hard guarantee: skill macros may only own RS.
            // Movement, aim modifiers and player-switch controls remain physical/native.
            r.LX = p.ThumbLX;
            r.LY = p.ThumbLY;
            r.LT = p.LeftTrigger;
            r.RT = p.RightTrigger;
            r.Buttons = (ushort)((r.Buttons & ~(ushort)XButtons.LeftShoulder) |
                                 (p.Buttons & (ushort)XButtons.LeftShoulder));
        }

        ApplyDirtyMeta(p, r, cfg);

        _prevButtons = p.Buttons;
        _lastPhysicalRsMagnitude = Math.Sqrt((double)p.ThumbRX*p.ThumbRX + (double)p.ThumbRY*p.ThumbRY);
        return r;
    }

    public void ResetInputStateForTest()
    {
        _prevButtons = 0;
        _prevLtModePressed = false;
        _rsLatched = false;
        _rsNeedsCenter = false;
        _rsCenterSince = 0;
        _prevRsMagnitude = 0;
        _lbChordConsumed = false;
        _lbModeTransitionConsumed = false;
        _lbRawPassed = false;
        _lastPhysicalRsMagnitude = 0;
        _blockedUntilReleaseMask = 0;
        _blockLtUntilRelease = false;
        _blockDefenseRsUntilCenter = false;
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

        // One Flick = One Command.
        // A direction is classified once when the stick crosses the trigger threshold.
        // It cannot fire again until RS is intentionally centered for the re-arm window.
        if (_rsNeedsCenter)
        {
            if (rsMag < cfg.RsReleaseDeadzone)
            {
                if (_rsCenterSince == 0)
                    _rsCenterSince = Stopwatch.GetTimestamp();

                if (MsSince(_rsCenterSince) >= Math.Clamp(cfg.RsRearmMs, 40, 250))
                {
                    _rsNeedsCenter = false;
                    _rsLatched = false;
                    _rsCenterSince = 0;
                }
            }
            else
            {
                _rsCenterSince = 0;
            }
        }
        else if (rsMag < cfg.RsReleaseDeadzone)
        {
            _rsLatched = false;
        }

        // Physical RS is replaced only while deciding/executing a skill.
        r.RX = 0;
        r.RY = 0;

        if (!_rsLatched && !_rsNeedsCenter && !_macro.Active &&
            rsMag >= cfg.RsTriggerDeadzone && SkillReady(cfg))
        {
            var dir = Cardinal(p.ThumbRX, p.ThumbRY);
            string key = "RS_" + dir;

            if (cfg.SkillMap.TryGetValue(key, out var skillName) && SkillLibrary.IsRsOnlySafe(skillName))
            {
                var skill = SkillLibrary.Get(skillName);
                _macro.Start(skill.Name, skill.Build(cfg.SkillStepMs), _facingRad);
                LastAction = dir + " → " + skill.Name;
                _rsLatched = true;
                _rsNeedsCenter = true;
                _rsCenterSince = 0;
            }
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
            // Driven ground pass while preserving every physical modifier, including LB.
            r.Buttons = (ushort)(r.Buttons | (ushort)(XButtons.A | XButtons.RightShoulder));
        }

        HandleShotB(p, r, cfg);

        // B timing is automated, but shot direction (LS) and shoulder modifiers remain fully physical/native.
    }

    private bool SkillReady(AppConfig cfg)
    {
        if (_lastSkillEnd == 0) return true;
        var cd = Math.Min(90, cfg.SkillCooldownMs);
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
        // Never rewrite LS. User movement and shot direction stay 100% physical.
    }

    private void WriteReportUnsafe(VirtualReport r)
    {
        if (_virtual is null || _submit is null) return;
        _virtual.ButtonState = r.Buttons;
        _virtual.LeftTrigger = r.LT;
        _virtual.RightTrigger = r.RT;
        _virtual.LeftThumbX = r.LX;
        _virtual.LeftThumbY = r.LY;
        _virtual.RightThumbX = r.RX;
        _virtual.RightThumbY = r.RY;
        _submit.Invoke();
    }

    private void Send(VirtualReport r)
    {
        if (!_vigemReady || _virtual is null) return;
        lock (_outputGate)
        {
            try
            {
                WriteReportUnsafe(r);
            }
            catch (Exception ex)
            {
                Error = "ViGEm output: " + ex.Message;
                _vigemReady = false;
                StatusChanged?.Invoke();
            }
        }
    }

    private void SendNeutral()
    {
        lock (_outputGate)
        {
            try
            {
                if (_virtual is not null && _submit is not null)
                    WriteReportUnsafe(new VirtualReport());
            }
            catch
            {
                _vigemReady = false;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _watchdog?.Dispose(); } catch { }
        _watchdog = null;
        SendNeutral();
        try { _thread?.Join(500); } catch { }
        DestroyVirtualController();
        try { _client?.Dispose(); } catch { }
        _client = null;
    }
}

public sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly ControllerEngine _engine;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _controllerRetryTimer;
    private bool _controllerInitBusy;
    private bool _engineStarted;
    private readonly Panel _content = new();
    private readonly Label _modeBadge = new();
    private readonly Label _controllerBadge = new();
    private readonly Label _vigemBadge = new();
    private readonly Label _latencyBadge = new();
    private readonly Label _singleBadge = new();
    private readonly Button _langBtn = new();
    private SingleControllerState _singleController = new(
        false, false, false, false, false, false,
        "Checking Single Controller Mode...", 0, 0);
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
        _cfg.NormalizeAttackOnly();
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

        ShowPage("dashboard");

        Shown += async (_,_) =>
        {
            if (await AutoUpdater.CheckAndApplyAsync(this, false))
            {
                Close();
                return;
            }
            await InitializeControllerPipelineAsync(true);
        };

        _uiTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _uiTimer.Tick += (_,_) => UpdateStatus();
        _uiTimer.Start();

        _controllerRetryTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _controllerRetryTimer.Tick += async (_,_) =>
        {
            if (!_engineStarted && !_controllerInitBusy)
                await InitializeControllerPipelineAsync(false);
        };
        _controllerRetryTimer.Start();

        FormClosing += (_,_) =>
        {
            _controllerRetryTimer.Stop();
            _uiTimer.Stop();
            _cfg.Save();
            _engine.Dispose();
        };
    }

    private async Task InitializeControllerPipelineAsync(bool showMessage)
    {
        if (_controllerInitBusy || _engineStarted) return;
        _controllerInitBusy = true;
        try
        {
            _singleBadge.Text = T("… فحص اليد","… CHECKING PAD");
            _singleBadge.ForeColor = Color.Gold;

            var previousMessage = _singleController.Message;
            var state = await Task.Run(HidHideManager.Prepare);
            if (IsDisposed) return;

            _singleController = state;
            if ((_page == "dashboard" || _page == "controller") &&
                !string.Equals(previousMessage, state.Message, StringComparison.Ordinal))
                ShowPage(_page);
            UpdateStatus();

            if (state.Ready)
            {
                _engine.Start();
                _engineStarted = true;
                _controllerRetryTimer?.Stop();
                UpdateStatus();
                return;
            }

            if (showMessage)
            {
                MessageBox.Show(
                    state.Message,
                    "FC27 Assist — Single Controller Mode",
                    MessageBoxButtons.OK,
                    state.HidHideInstalled ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex);
            _singleController = new(
                false, false, false, false, false, false,
                "Single Controller setup failed: " + ex.Message, 0, 0);
            if (_page == "dashboard" || _page == "controller") ShowPage(_page);
            UpdateStatus();
            if (showMessage)
            {
                MessageBox.Show(
                    _singleController.Message + "\n\nCrash log: " + CrashLogger.Path,
                    "FC27 Assist",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            _controllerInitBusy = false;
        }
    }

    private void BuildShell()
    {
        var side = new Panel { Dock=DockStyle.Left, Width=225, BackColor=Color.FromArgb(13,18,29), Padding=new Padding(16) };
        Controls.Add(side);
        var logo = new Label { Text="FC27\nASSIST", AutoSize=false, Height=82, Dock=DockStyle.Top, ForeColor=_text, Font=new Font("Segoe UI Semibold",18,FontStyle.Bold), TextAlign=ContentAlignment.MiddleLeft };
        side.Controls.Add(logo);

        AddNav(side,"dashboard","◈  " + T("الرئيسية","Dashboard"));
        AddNav(side,"attack","⚡  " + T("الهجوم","Attack"));
        AddNav(side,"skills","✦  " + T("مكتبة المهارات","Skill Library"));
        AddNav(side,"controller","◎  " + T("اختبار اليد","Controller Test"));
        AddNav(side,"settings","⚙  " + T("الإعدادات","Settings"));

        var foot = new Label { Dock=DockStyle.Bottom, Height=60, Text=T("RS = مهارات   •   LB/LT/RT/LS = طبيعي","RS = Skills   •   LB/LT/RT/LS = Native"), ForeColor=_muted, TextAlign=ContentAlignment.MiddleLeft };
        side.Controls.Add(foot);

        var top = new Panel { Dock=DockStyle.Top, Height=74, BackColor=_bg, Padding=new Padding(24,14,24,10) };
        Controls.Add(top);
        _langBtn.Text = Ar ? "EN" : "عربي";
        _langBtn.Dock=DockStyle.Right; _langBtn.Width=70; StyleButton(_langBtn,true); _langBtn.Click += (_,_) => ToggleLanguage();
        top.Controls.Add(_langBtn);
        _latencyBadge.Dock=DockStyle.Right; _latencyBadge.Width=112; StyleBadge(_latencyBadge); top.Controls.Add(_latencyBadge);
        _singleBadge.Dock=DockStyle.Right; _singleBadge.Width=160; StyleBadge(_singleBadge); top.Controls.Add(_singleBadge);
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
            "attack" => BuildAttack(), "skills" => BuildSkills(),
            "controller" => BuildController(), "settings" => BuildSettings(), _ => BuildDashboard()
        };
        p.Dock=DockStyle.Top; _content.Controls.Add(p); _content.ResumeLayout();
    }

    private Control BuildDashboard()
    {
        var root=Stack(); root.Controls.Add(Title(T("FC27 Assist — لوحة التحكم","FC27 Assist — Control Center"),T("وضع هجوم فقط لتقليل التعارضات والحفاظ على حركة اليد الطبيعية.","Attack-only mode to minimize conflicts while keeping native movement controls.")));
        var row=Row(3,190);
        row.Controls.Add(Card(T("الوضع الحالي","CURRENT MODE"), T("هجوم فقط ⚡","ATTACK ONLY ⚡"), T("LT / RT / LS تعمل طبيعي","LT / RT / LS stay native")));
        row.Controls.Add(Card(T("Dirty Meta","DIRTY META"), T("مفعّل دائمًا","ALWAYS ON"), T("مهارات أسرع بدون لمس حركة LS","Faster skill layer without rewriting LS")));
        row.Controls.Add(Card(T("محرك اليد","CONTROLLER ENGINE"), _singleController.Ready ? T("يد واحدة","ONE CONTROLLER") : T("إعداد مطلوب","SETUP REQUIRED"), _singleController.Message));
        root.Controls.Add(row);

        var dirty=PanelCard(170); var lbl=BigLabel(T("DIRTY META — أساسي دائمًا","DIRTY META — ALWAYS ON")); dirty.Controls.Add(lbl);
        var ddesc=new Label{Text=T("Skill chaining أسرع + Explosive Exit. لا يتم تعديل LS حتى تبقى الحركة واتجاه التسديد تحت تحكمك بالكامل.","Faster skill chaining + Explosive Exit. LS is never rewritten, so movement and shot direction remain fully yours."),AutoSize=false,Height=72,Dock=DockStyle.Fill,ForeColor=_muted,Padding=new Padding(0,12,0,0)}; dirty.Controls.Add(ddesc); ddesc.BringToFront(); lbl.BringToFront();
        root.Controls.Add(dirty);

        var info=PanelCard(170); info.Controls.Add(BigLabel(T("منع التعارض","CONFLICT CONTROL")));
        info.Controls.Add(new Label{Text=T("LB وLT وRT وLS تمر مباشرة للعبة بدون تدخل. RS فقط للمهارات. Y تمريرة بينية أرضية طبيعية.","LB, LT, RT and LS pass directly to the game. Only RS is used for skills. Y remains the native ground through pass."),AutoSize=false,Height=72,Dock=DockStyle.Fill,ForeColor=_muted,Padding=new Padding(0,12,0,0)});
        root.Controls.Add(info);
        return root;
    }

    private Control BuildAttack()
    {
        var root=Stack(); root.Controls.Add(Title(T("الهجوم","Attack Only"),T("الهجوم دائم. RS فقط للمهارات، وLB يبقى طبيعي لتبديل اللاعبين.","Attack is always active. Only RS triggers skills; LB stays native for player switching.")));
        var grid=Row(1,360); grid.Controls.Add(BuildMappingCard(false)); root.Controls.Add(grid);
        var shot=PanelCard(235); shot.Controls.Add(BigLabel(T("التسديد والتمرير","SHOOTING & PASSING")));
        var txt=new Label{Dock=DockStyle.Fill,ForeColor=_muted,Text=T(
            $"B نقرة سريعة → Low Driven تلقائي\nB ضغط مستمر → شوت عادي قوي (حد القوة { _cfg.BNormalShotCapMs }ms)\nاتجاه التسديد → LS بيدك 100%\nA → Driven Ground Pass (RB+A)\nY → تمريرة بينية أرضية طبيعية",
            $"Quick B tap → automatic Low Driven\nHold B → strong normal shot (power cap {_cfg.BNormalShotCapMs}ms)\nShot direction → 100% your LS\nA → Driven Ground Pass (RB+A)\nY → native ground through pass"),Padding=new Padding(0,12,0,0)}; shot.Controls.Add(txt);
        root.Controls.Add(shot); return root;
    }

    private Control BuildMappingCard(bool lb)
    {
        var p=PanelCard(340);
        p.Controls.Add(BigLabel(T("RS — المهارات الأساسية","RS — CORE SKILLS")));

        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(0,12,0,0)};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,31));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,69));

        string[] dirs={"UP","RIGHT","LEFT","DOWN"};
        string[] arrows={"↑","→","←","↓"};
        var safe = SkillLibrary.RsOnlySafeSkills.Cast<object>().ToArray();

        for(int i=0;i<4;i++)
        {
            string key="RS_"+dirs[i];
            table.Controls.Add(new Label{Text="RS "+arrows[i],Dock=DockStyle.Fill,ForeColor=_muted,TextAlign=ContentAlignment.MiddleLeft},0,i);

            var cb=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=_panel2,ForeColor=_text,FlatStyle=FlatStyle.Flat};
            cb.Items.AddRange(safe);

            var fallback = AppConfig.DefaultAttackSkillMap()[key];
            var currentName = _cfg.SkillMap.TryGetValue(key,out var n) ? n : fallback;
            var current = SkillLibrary.RsOnlySafeSkills.FirstOrDefault(x=>x.Name==currentName)
                          ?? SkillLibrary.Get(fallback);
            cb.SelectedItem=current;

            cb.SelectedIndexChanged+=(_,_)=>
            {
                if(cb.SelectedItem is not SkillDef sd) return;

                var duplicate = _cfg.SkillMap
                    .Where(kv => kv.Key.StartsWith("RS_", StringComparison.OrdinalIgnoreCase) && kv.Key != key)
                    .Any(kv => kv.Value.Equals(sd.Name, StringComparison.OrdinalIgnoreCase));

                if (duplicate)
                {
                    MessageBox.Show(T("كل اتجاه لازم تكون له مهارة مختلفة.","Each RS direction must use a different skill."),
                        "FC27 Assist", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    cb.SelectedItem = current;
                    return;
                }

                _cfg.SkillMap[key]=sd.Name;
                _cfg.NormalizeAttackOnly();
                _cfg.Save();
                _engine.UpdateConfig(_cfg);
                current = sd;
            };
            table.Controls.Add(cb,1,i);
        }

        p.Controls.Add(table);
        return p;
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
        var root=Stack();
        root.Controls.Add(Title(T("اختبار يد Xbox","Xbox Controller Test"),T("راقب الإدخال الخام والوضع الحالي قبل فتح المباراة.","Watch raw input and current mode before opening a match.")));

        var p=PanelCard(430);
        p.Controls.Add(BigLabel(T("Live Input","LIVE INPUT")));
        var rescan=new Button{Name="rescanController",Text=T("إعادة فحص اليد","RESCAN CONTROLLER"),Dock=DockStyle.Bottom,Height=44};
        StyleButton(rescan,true);
        rescan.Click += async (_,_) => await InitializeControllerPipelineAsync(true);
        p.Controls.Add(rescan);
        var live=new Label{Name="liveInput",Dock=DockStyle.Fill,Font=new Font("Consolas",12),ForeColor=_cyan,Padding=new Padding(0,15,0,0)};
        p.Controls.Add(live);
        root.Controls.Add(p);

        var h=PanelCard(175);
        h.Controls.Add(BigLabel(T("Single Controller Mode","SINGLE CONTROLLER MODE")));
        var l=new Label{Dock=DockStyle.Fill,ForeColor=_muted,
            Text=_singleController.Message + "\n\n" +
                T("بعد فصل وتركيب USB البرنامج يعيد الفحص تلقائيًا كل ثانيتين.","After USB reconnect, the app rescans automatically every two seconds."),
            Padding=new Padding(0,10,0,0)};
        h.Controls.Add(l);
        root.Controls.Add(h);
        return root;
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
        AddNumeric(table,9,T("RS إعادة التسليح (ms)","RS Rearm Center (ms)"),_cfg.RsRearmMs,40,250,v=>_cfg.RsRearmMs=v);
        AddNumeric(table,10,T("Input Loop Hz","Input Loop Hz"),_cfg.InputLoopHz,250,1000,v=>_cfg.InputLoopHz=v);
        p.Controls.Add(table);root.Controls.Add(p);
        var buttons=PanelCard(135);
        var reset=new Button{Text=T("استعادة الإعدادات الافتراضية","RESET DEFAULTS"),Dock=DockStyle.Left,Width=220};StyleButton(reset,false);reset.Click+=(_,_)=>{var fresh=new AppConfig{Language=_cfg.Language};CopyConfig(fresh,_cfg);SaveAndRefresh("settings");};buttons.Controls.Add(reset);
        var update=new Button{Text=T("فحص التحديث","CHECK UPDATE"),Dock=DockStyle.Left,Width=190};StyleButton(update,false);update.Click+=async (_,_)=>{if(await AutoUpdater.CheckAndApplyAsync(this,true)) Close();};buttons.Controls.Add(update);update.BringToFront();
        var save=new Button{Text=T("حفظ","SAVE"),Dock=DockStyle.Right,Width=180};StyleButton(save,true);save.Click+=(_,_)=>SaveCfg();buttons.Controls.Add(save);
        root.Controls.Add(buttons);return root;
    }

    private void CopyConfig(AppConfig src, AppConfig dst)
    {
        var lang=dst.Language; var fresh=src;
        dst.ControllerSlot=fresh.ControllerSlot;dst.DirtyMeta=fresh.DirtyMeta;dst.RsTriggerDeadzone=fresh.RsTriggerDeadzone;dst.RsReleaseDeadzone=fresh.RsReleaseDeadzone;dst.RsRearmMs=fresh.RsRearmMs;dst.SkillStepMs=fresh.SkillStepMs;dst.SkillCooldownMs=fresh.SkillCooldownMs;dst.BTapThresholdMs=fresh.BTapThresholdMs;dst.LowDrivenChargeMs=fresh.LowDrivenChargeMs;dst.BNormalShotCapMs=fresh.BNormalShotCapMs;dst.LowDrivenSecondTapGapMs=fresh.LowDrivenSecondTapGapMs;dst.LowDrivenSecondTapMs=fresh.LowDrivenSecondTapMs;dst.LbChordWindowMs=fresh.LbChordWindowMs;dst.InputLoopHz=fresh.InputLoopHz;dst.AutoPress=fresh.AutoPress;dst.PressureStrength=fresh.PressureStrength;dst.SprintJockeyAssist=fresh.SprintJockeyAssist;dst.HardTackleAssist=fresh.HardTackleAssist;dst.SkillMap=new Dictionary<string,string>(fresh.SkillMap);dst.Language=lang;
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

    private bool ProbePhysicalController(out int slot, out XInputGamepad pad)
    {
        slot = _engine.PhysicalSlot;
        if (slot >= 0 && XInputNative.TryGetState(slot, out var lockedState))
        {
            pad = lockedState.Gamepad;
            return true;
        }

        // Only perform an all-slot scan before the engine/ViGEm pipeline is active.
        if (!_engine.ViGEmReady && XInputNative.TryFindFirst(out slot, out var state))
        {
            pad = state.Gamepad;
            return true;
        }

        pad = default;
        return false;
    }

    private void UpdateStatus()
    {
        if (IsDisposed) return;
        _modeBadge.Text=T("⚡ هجوم فقط","⚡ ATTACK ONLY");_modeBadge.ForeColor=_accent;
        bool physicalOk = ProbePhysicalController(out var physicalSlot, out var physicalPad);
        _controllerBadge.Text=physicalOk?T($"● اليد متصلة S{physicalSlot}","● Physical OK S"+physicalSlot):T("○ اليد غير متصلة","○ Physical Lost");
        _controllerBadge.ForeColor=physicalOk?_accent:Color.OrangeRed;
        _vigemBadge.Text=_engine.ViGEmReady?"● ViGEm OK":"○ ViGEm";_vigemBadge.ForeColor=_engine.ViGEmReady?_accent:Color.OrangeRed;
        _singleBadge.Text=_singleController.Ready?T("● يد واحدة","● ONE PAD"):T("○ إعداد اليد","○ PAD SETUP");_singleBadge.ForeColor=_singleController.Ready?_accent:Color.OrangeRed;
        _latencyBadge.Text=$"Loop {_engine.LoopHz:0} Hz";_latencyBadge.ForeColor=_engine.LoopHz>300?_accent:_muted;
        if(_page=="controller")
        {
            var live=FindByName(_content,"liveInput") as Label;
            if(live!=null)
            {
                var p = physicalOk ? physicalPad : _engine.LastPhysical;
                live.Text=$"Physical  : {(physicalOk ? "CONNECTED" : "NOT FOUND")}\nXInput    : {(physicalOk ? "Slot " + physicalSlot : "-")}\nMode      : {_engine.Mode}\nButtons   : 0x{p.Buttons:X4}\nLT / RT   : {p.LeftTrigger,3} / {p.RightTrigger,3}\nLS        : {p.ThumbLX,6} , {p.ThumbLY,6}\nRS        : {p.ThumbRX,6} , {p.ThumbRY,6}\nLoop      : {_engine.LoopHz:0} Hz\nLast      : {_engine.LastAction}\nViGEm     : {(_engine.ViGEmReady?"READY":"NOT READY")}\nError     : {_engine.Error}";
            }
        }
    }

    private static Control? FindByName(Control root,string name){if(root.Name==name)return root;foreach(Control c in root.Controls){var f=FindByName(c,name);if(f!=null)return f;}return null;}
    private void SaveCfg(){_cfg.NormalizeAttackOnly();_cfg.Save();_engine.UpdateConfig(_cfg);}
    private void SaveAndRefresh(string page){SaveCfg();ShowPage(page);UpdateStatus();}
    private void ToggleLanguage(){_cfg.Language=Ar?"en":"ar";_cfg.Save();BuildLanguageRefresh();}
    private void BuildLanguageRefresh(){_langBtn.Text=Ar?"EN":"عربي";foreach(var kv in _nav){kv.Value.Text=kv.Key switch{"dashboard"=>"◈  "+T("الرئيسية","Dashboard"),"attack"=>"⚡  "+T("الهجوم","Attack"),"skills"=>"✦  "+T("مكتبة المهارات","Skill Library"),"controller"=>"◎  "+T("اختبار اليد","Controller Test"),_=>"⚙  "+T("الإعدادات","Settings")};}ShowPage(_page);}
}
