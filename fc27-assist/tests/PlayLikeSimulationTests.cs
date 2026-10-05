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
        => new(cfg ?? new AppConfig { AutoPress = false, DirtyMeta = false });

    [Fact]
    public void AttackModeIsPermanentAndLtRemainsNative()
    {
        var cfg = new AppConfig { AutoPress=true, SprintJockeyAssist=true, HardTackleAssist=true };
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(lt:255, rt:180, lx:-20000, ly:24000), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.Equal((byte)255, r.LT);
        Assert.Equal((byte)180, r.RT);
        Assert.Equal((short)-20000, r.LX);
        Assert.Equal((short)24000, r.LY);
    }

    [Fact]
    public void DefenseAutomationCanNeverInjectRb()
    {
        var cfg = new AppConfig { AutoPress=true, SprintJockeyAssist=true, HardTackleAssist=true };
        using var e = Engine(cfg);

        for (int i = 0; i < 20; i++)
        {
            var r = e.ProcessFrameForTest(Pad(lt:255, lx:20000), cfg);
            Assert.False(Has(r, XButtons.RightShoulder));
            Assert.Equal(PlayMode.Attack, e.Mode);
        }
    }

    [Fact]
    public void NativeLbAfterIntentWindowCannotLaterTurnIntoLbSkill()
    {
        var cfg = new AppConfig();
        using var e = Engine(cfg);

        var start = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.False(Has(start, XButtons.LeftShoulder));

        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var native = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.True(Has(native, XButtons.LeftShoulder));

        var withRs = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);
        Assert.True(Has(withRs, XButtons.LeftShoulder));
        Assert.Equal((short)30000, withRs.RX);
        Assert.NotEqual("Skilled Bridge", e.LastAction);
    }

    [Fact]
    public void SimultaneousLbRsExecutesSecondLayerWithoutRawLb()
    {
        var cfg = new AppConfig();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);

        Assert.Equal("Skilled Bridge", e.LastAction);
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.Equal((byte)255, r.LT);
    }

    [Fact]
    public void HeldRsDirectionTriggersOnlyOnceUntilCentered()
    {
        var cfg = new AppConfig { DirtyMeta=false, SkillCooldownMs=60, RsRearmMs=70 };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);

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
        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);
    }

    [Fact]
    public void RunningSkillSuppressesPhysicalSprintAndModifiers()
    {
        var cfg = new AppConfig();
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(rt:255, ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);
        Assert.Equal((byte)0, r.RT);
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void ManualFaceButtonCancelsRunningSkill()
    {
        var cfg = new AppConfig();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);

        var r = e.ProcessFrameForTest(Pad(XButtons.A), cfg);
        Assert.Equal("Manual override", e.LastAction);
        Assert.True(Has(r, XButtons.A));
        Assert.True(Has(r, XButtons.RightShoulder));
        Assert.False(Has(r, XButtons.LeftShoulder));
    }

    [Fact]
    public void QuickBReleaseBecomesCalibratedLowDriven()
    {
        var cfg = new AppConfig { BTapThresholdMs=180, LowDrivenChargeMs=260, LowDrivenSecondTapGapMs=30, LowDrivenSecondTapMs=45 };
        using var e = Engine(cfg);

        var down = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.True(Has(down, XButtons.B));

        Thread.Sleep(45);
        var released = e.ProcessFrameForTest(Pad(), cfg);
        Assert.Equal("Low Driven Shot", e.LastAction);
        Assert.True(Has(released, XButtons.B));

        Thread.Sleep(230);
        var gap = e.ProcessFrameForTest(Pad(), cfg);
        Assert.False(Has(gap, XButtons.B));
    }

    [Fact]
    public void HoldBUsesProgramPowerEvenIfFingerReleasesEarly()
    {
        var cfg = new AppConfig { BTapThresholdMs=100, BNormalShotCapMs=280 };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Thread.Sleep(125);
        var classified = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.Equal("Normal Strong Shot", e.LastAction);
        Assert.True(Has(classified, XButtons.B));

        var earlyPhysicalRelease = e.ProcessFrameForTest(Pad(), cfg);
        Assert.True(Has(earlyPhysicalRelease, XButtons.B));

        Thread.Sleep(180);
        var capped = e.ProcessFrameForTest(Pad(), cfg);
        Assert.False(Has(capped, XButtons.B));
    }

    [Fact]
    public void ReleasingNativeLbWhileRsHeldDoesNotTriggerBaseSkill()
    {
        var cfg = new AppConfig();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var native = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);
        Assert.True(Has(native, XButtons.LeftShoulder));
        Assert.Equal((short)30000, native.RX);

        var releaseLb = e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.NotEqual("Ball Roll Spin Right", e.LastAction);
        Assert.Equal((short)0, releaseLb.RX);

        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(cfg.RsRearmMs + 20);
        e.ProcessFrameForTest(Pad(), cfg);
        e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.Equal("Ball Roll Spin Right", e.LastAction);
    }

    [Fact]
    public void RandomizedAttackFramesStayAttackOnlyAndDoNotThrow()
    {
        var cfg = new AppConfig
        {
            AutoPress=true,
            SprintJockeyAssist=true,
            HardTackleAssist=true,
            DirtyMeta=true,
            BTapThresholdMs=120,
            LowDrivenChargeMs=240,
            BNormalShotCapMs=320
        };

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
            Assert.InRange(r.LX, short.MinValue, short.MaxValue);
            Assert.InRange(r.LY, short.MinValue, short.MaxValue);
            Assert.InRange(r.RX, short.MinValue, short.MaxValue);
            Assert.InRange(r.RY, short.MinValue, short.MaxValue);

            if (i % 7 == 0)
                e.ProcessFrameForTest(Pad(), cfg);
        }
    }
}
