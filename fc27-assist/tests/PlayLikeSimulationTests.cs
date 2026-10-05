using FC27Assist;
using Xunit;

public class PlayLikeSimulationTests
{
    private static XInputGamepad Pad(
        XButtons buttons = 0, byte lt = 0, byte rt = 0,
        short lx = 0, short ly = 0, short rx = 0, short ry = 0)
        => new()
        {
            Buttons = (ushort)buttons,
            LeftTrigger = lt, RightTrigger = rt,
            ThumbLX = lx, ThumbLY = ly, ThumbRX = rx, ThumbRY = ry
        };

    private static bool Has(VirtualReport r, XButtons b)
        => (r.Buttons & (ushort)b) != 0;

    private static ControllerEngine Engine(AppConfig? cfg = null)
    {
        cfg ??= new AppConfig();
        cfg.NormalizeAttackOnly();
        return new ControllerEngine(cfg);
    }

    [Fact]
    public void AttackModeIsPermanentAndLsAlwaysStaysPhysical()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(lt:211, rt:177, lx:-23000, ly:25000), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.Equal((short)-23000, r.LX);
        Assert.Equal((short)25000, r.LY);
    }

    [Fact]
    public void QuickLbTapIsReplayedForNormalPlayerSwitching()
    {
        var cfg = new AppConfig { LbChordWindowMs = 65 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var down = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.False(Has(down, XButtons.LeftShoulder));

        var released = e.ProcessFrameForTest(Pad(), cfg);
        Assert.True(Has(released, XButtons.LeftShoulder));
    }

    [Fact]
    public void HeldLbBecomesNativeAfterIntentWindow()
    {
        var cfg = new AppConfig { LbChordWindowMs = 50 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var first = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.False(Has(first, XButtons.LeftShoulder));

        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var held = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.True(Has(held, XButtons.LeftShoulder));
    }

    [Fact]
    public void LbPlusRsUsesSecondaryLayerAndConsumesSelector()
    {
        var cfg = new AppConfig { LbChordWindowMs = 70 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        var r = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);

        Assert.Contains("LB+RIGHT", e.LastAction);
        Assert.Contains(cfg.SkillMap["LB_RS_RIGHT"], e.LastAction);
        Assert.False(Has(r, XButtons.LeftShoulder));
    }

    [Fact]
    public void RsWithoutLbUsesCoreLayer()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(rx:30000), cfg);

        Assert.Contains("RIGHT", e.LastAction);
        Assert.DoesNotContain("LB+", e.LastAction);
        Assert.Contains(cfg.SkillMap["RS_RIGHT"], e.LastAction);
    }

    [Fact]
    public void EightDefaultShortcutsAreAllDifferent()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();

        Assert.Equal(8, cfg.SkillMap.Count);
        Assert.Equal(8, cfg.SkillMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("RS_UP", cfg.SkillMap.Keys);
        Assert.Contains("LB_RS_UP", cfg.SkillMap.Keys);
    }

    [Fact]
    public void HeldRsTriggersOnlyOnceUntilCentered()
    {
        var cfg = new AppConfig { SkillCooldownMs=60, RsRearmMs=60 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        var first = e.LastAction;
        Assert.Contains("UP", first);

        e.ProcessFrameForTest(Pad(XButtons.A, ry:30000), cfg);
        Assert.Equal("Manual override", e.LastAction);

        Thread.Sleep(120);
        for (int i=0;i<5;i++)
        {
            e.ProcessFrameForTest(Pad(ry:30000), cfg);
            Thread.Sleep(15);
        }
        Assert.Equal("Manual override", e.LastAction);

        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(cfg.RsRearmMs + 20);
        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(80);
        e.ProcessFrameForTest(Pad(ry:30000), cfg);

        Assert.Contains("UP", e.LastAction);
    }

    [Fact]
    public void RightAndLeftShortcutsResolveToDifferentCommands()
    {
        var cfg = new AppConfig { SkillCooldownMs=60, RsRearmMs=50 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(rx:30000), cfg);
        var right = e.LastAction;

        for(int i=0;i<10;i++)
        {
            e.ProcessFrameForTest(Pad(), cfg);
            Thread.Sleep(25);
        }
        Thread.Sleep(80);

        e.ProcessFrameForTest(Pad(rx:-30000), cfg);
        var left = e.LastAction;

        Assert.Contains("RIGHT", right);
        Assert.Contains("LEFT", left);
        Assert.NotEqual(right, left);
    }

    [Fact]
    public void AIsFastDrivenGroundPass()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.A | XButtons.LeftShoulder), cfg);

        Assert.True(Has(r, XButtons.A));
        Assert.True(Has(r, XButtons.RightShoulder));
        Assert.False(Has(r, XButtons.LeftShoulder));
    }

    [Fact]
    public void YIsAlwaysGroundThroughPass()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.Y | XButtons.LeftShoulder | XButtons.RightShoulder), cfg);

        Assert.True(Has(r, XButtons.Y));
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void QuickBTapBecomesLowDrivenAndKeepsUserAim()
    {
        var cfg = new AppConfig
        {
            BTapThresholdMs=180,
            LowDrivenChargeMs=260,
            LowDrivenSecondTapGapMs=30,
            LowDrivenSecondTapMs=45
        };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var down = e.ProcessFrameForTest(Pad(XButtons.B, lx:18000, ly:22000), cfg);
        Assert.True(Has(down, XButtons.B));
        Assert.Equal((short)18000, down.LX);
        Assert.Equal((short)22000, down.LY);

        Thread.Sleep(45);
        var released = e.ProcessFrameForTest(Pad(lx:18000, ly:22000), cfg);

        Assert.Equal("Low Driven Shot", e.LastAction);
        Assert.True(Has(released, XButtons.B));
        Assert.Equal((short)18000, released.LX);
        Assert.Equal((short)22000, released.LY);
    }

    [Fact]
    public void HoldBUsesProgramPowerAndKeepsUserAim()
    {
        var cfg = new AppConfig { BTapThresholdMs=100, BNormalShotCapMs=280 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B, lx:-19000, ly:26000), cfg);
        Thread.Sleep(125);

        var classified = e.ProcessFrameForTest(Pad(XButtons.B, lx:-19000, ly:26000), cfg);

        Assert.Equal("Normal Strong Shot", e.LastAction);
        Assert.True(Has(classified, XButtons.B));
        Assert.Equal((short)-19000, classified.LX);
        Assert.Equal((short)26000, classified.LY);
    }

    [Fact]
    public void ShotAutomationStripsShoulderShotModifiers()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.B | XButtons.LeftShoulder | XButtons.RightShoulder), cfg);

        Assert.True(Has(r, XButtons.B));
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void DirtyMetaNeverRewritesLs()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        for (int i=0;i<50;i++)
        {
            short lx=(short)(-28000+i*900);
            short ly=(short)(25000-i*700);
            var r=e.ProcessFrameForTest(Pad(lx:lx,ly:ly),cfg);
            Assert.Equal(lx,r.LX);
            Assert.Equal(ly,r.LY);
        }
    }

    [Fact]
    public void RandomizedAttackFramesStayAttackOnlyAndNeverAlterLs()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);
        var rnd = new Random(27028);

        var buttons = new[]
        {
            XButtons.A, XButtons.B, XButtons.X, XButtons.Y,
            XButtons.LeftShoulder,
            XButtons.LeftShoulder | XButtons.A,
            XButtons.LeftShoulder | XButtons.Y,
            (XButtons)0
        };

        for(int i=0;i<3000;i++)
        {
            var b=buttons[rnd.Next(buttons.Length)];
            short lx=(short)rnd.Next(-32767,32768);
            short ly=(short)rnd.Next(-32767,32768);
            short rx=(short)rnd.Next(-32767,32768);
            short ry=(short)rnd.Next(-32767,32768);
            byte lt=(byte)rnd.Next(0,256);
            byte rt=(byte)rnd.Next(0,256);

            var r=e.ProcessFrameForTest(Pad(b,lt,rt,lx,ly,rx,ry),cfg);

            Assert.Equal(PlayMode.Attack,e.Mode);
            Assert.Equal(lx,r.LX);
            Assert.Equal(ly,r.LY);

            if(i%9==0)
                e.ProcessFrameForTest(Pad(),cfg);
        }
    }
}
