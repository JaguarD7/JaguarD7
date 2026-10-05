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
    public void AttackModeIsPermanentAndMovementControlsStayNative()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(
            buttons:XButtons.LeftShoulder,
            lt:211, rt:177, lx:-23000, ly:25000), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.True(Has(r, XButtons.LeftShoulder));
        Assert.Equal((byte)211, r.LT);
        Assert.Equal((byte)177, r.RT);
        Assert.Equal((short)-23000, r.LX);
        Assert.Equal((short)25000, r.LY);
    }

    [Fact]
    public void LbIsNeverConsumedEvenOnFirstFrame()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var first = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        var held = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);

        Assert.True(Has(first, XButtons.LeftShoulder));
        Assert.True(Has(held, XButtons.LeftShoulder));
    }

    [Fact]
    public void YRemainsNativeGroundThroughPassWithoutLbOrRbInjection()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.Y), cfg);

        Assert.True(Has(r, XButtons.Y));
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void ADrivenPassPreservesPhysicalLb()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.A | XButtons.LeftShoulder), cfg);

        Assert.True(Has(r, XButtons.A));
        Assert.True(Has(r, XButtons.RightShoulder));
        Assert.True(Has(r, XButtons.LeftShoulder));
    }

    [Fact]
    public void RsDirectionsUseFourDifferentDefaultSkills()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();

        var keys = new[] {"RS_UP","RS_RIGHT","RS_LEFT","RS_DOWN"};
        var names = keys.Select(k => cfg.SkillMap[k]).ToArray();

        Assert.Equal(4, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var name in names)
            Assert.True(SkillLibrary.IsRsOnlySafe(name));
    }

    [Fact]
    public void RightAndLeftFlicksResolveToDifferentSkills()
    {
        var cfg = new AppConfig { SkillCooldownMs=60, RsRearmMs=50 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(rx:30000), cfg);
        var rightAction = e.LastAction;
        Assert.Contains("RIGHT", rightAction);

        // Let the first macro fully complete while RS is centered, then re-arm.
        for (int i = 0; i < 8; i++)
        {
            e.ProcessFrameForTest(Pad(), cfg);
            Thread.Sleep(25);
        }
        Thread.Sleep(cfg.RsRearmMs + 15);
        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(70);

        e.ProcessFrameForTest(Pad(rx:-30000), cfg);
        var leftAction = e.LastAction;
        Assert.Contains("LEFT", leftAction);

        Assert.NotEqual(rightAction, leftAction);
    }

    [Fact]
    public void HeldRsDirectionTriggersOnlyOnceUntilCentered()
    {
        var cfg = new AppConfig { SkillCooldownMs=60, RsRearmMs=70 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        var firstAction = e.LastAction;
        Assert.Contains("UP", firstAction);

        e.ProcessFrameForTest(Pad(XButtons.A, ry:30000), cfg);
        Assert.Equal("Manual override", e.LastAction);

        Thread.Sleep(cfg.SkillCooldownMs + 100);
        for (int i = 0; i < 6; i++)
        {
            e.ProcessFrameForTest(Pad(ry:30000), cfg);
            Thread.Sleep(20);
        }

        Assert.Equal("Manual override", e.LastAction);

        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(20);
        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Manual override", e.LastAction);

        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(cfg.RsRearmMs + 20);
        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(70);
        e.ProcessFrameForTest(Pad(ry:30000), cfg);

        Assert.Contains("UP", e.LastAction);
        Assert.NotEqual("Manual override", e.LastAction);
    }

    [Fact]
    public void SkillMacroPreservesLbLtRtAndLs()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(
            XButtons.LeftShoulder,
            lt:180, rt:220, lx:16000, ly:-21000, rx:30000), cfg);

        Assert.True(Has(r, XButtons.LeftShoulder));
        Assert.Equal((byte)180, r.LT);
        Assert.Equal((byte)220, r.RT);
        Assert.Equal((short)16000, r.LX);
        Assert.Equal((short)-21000, r.LY);
    }

    [Fact]
    public void ManualFaceButtonCancelsRunningSkill()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Contains("UP", e.LastAction);

        var r = e.ProcessFrameForTest(Pad(XButtons.A), cfg);
        Assert.Equal("Manual override", e.LastAction);
        Assert.True(Has(r, XButtons.A));
        Assert.True(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void QuickBReleaseBecomesCalibratedLowDriven()
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

        Thread.Sleep(230);
        var gap = e.ProcessFrameForTest(Pad(lx:18000, ly:22000), cfg);
        Assert.False(Has(gap, XButtons.B));
        Assert.Equal((short)18000, gap.LX);
        Assert.Equal((short)22000, gap.LY);
    }

    [Fact]
    public void HoldBUsesProgramPowerButNeverChangesShotDirection()
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

        var released = e.ProcessFrameForTest(Pad(lx:-19000, ly:26000), cfg);
        Assert.True(Has(released, XButtons.B));
        Assert.Equal((short)-19000, released.LX);
        Assert.Equal((short)26000, released.LY);
    }

    [Fact]
    public void DirtyMetaNeverRewritesLs()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        for (int i=0; i<50; i++)
        {
            short lx = (short)(-28000 + i * 900);
            short ly = (short)(25000 - i * 700);
            var r = e.ProcessFrameForTest(Pad(lx:lx, ly:ly), cfg);
            Assert.Equal(lx, r.LX);
            Assert.Equal(ly, r.LY);
        }
    }

    [Fact]
    public void RandomizedAttackFramesStayAttackOnlyAndKeepNativeMovement()
    {
        var cfg = new AppConfig
        {
            AutoPress=true,
            SprintJockeyAssist=true,
            HardTackleAssist=true,
            BTapThresholdMs=120,
            LowDrivenChargeMs=240,
            BNormalShotCapMs=320
        };
        cfg.NormalizeAttackOnly();

        using var e = Engine(cfg);
        var rnd = new Random(27028);
        var buttons = new[]
        {
            XButtons.A, XButtons.B, XButtons.X, XButtons.Y,
            XButtons.LeftShoulder, XButtons.RightShoulder,
            XButtons.A | XButtons.LeftShoulder,
            XButtons.B | XButtons.LeftShoulder,
            XButtons.Y | XButtons.LeftShoulder,
            (XButtons)0
        };

        for (int i=0; i<5000; i++)
        {
            var b = buttons[rnd.Next(buttons.Length)];
            short lx = (short)rnd.Next(-32767,32768);
            short ly = (short)rnd.Next(-32767,32768);
            short rx = (short)rnd.Next(-32767,32768);
            short ry = (short)rnd.Next(-32767,32768);
            byte lt = (byte)rnd.Next(0,256);
            byte rt = (byte)rnd.Next(0,256);

            var r = e.ProcessFrameForTest(Pad(b, lt:lt, rt:rt, lx:lx, ly:ly, rx:rx, ry:ry), cfg);

            Assert.Equal(PlayMode.Attack, e.Mode);
            Assert.Equal(lx, r.LX);
            Assert.Equal(ly, r.LY);
            Assert.Equal(lt, r.LT);
            Assert.Equal(rt, r.RT);

            if ((b & XButtons.LeftShoulder) != 0)
                Assert.True(Has(r, XButtons.LeftShoulder));

            if ((b & XButtons.Y) != 0)
            {
                Assert.True(Has(r, XButtons.Y));
                if ((b & XButtons.LeftShoulder) == 0)
                    Assert.False(Has(r, XButtons.LeftShoulder));
            }

            if (i % 7 == 0)
                e.ProcessFrameForTest(Pad(), cfg);
        }
    }
}
