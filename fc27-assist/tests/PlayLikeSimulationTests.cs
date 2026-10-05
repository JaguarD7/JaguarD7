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
    public void MasterButtonOffMakesControllerRawOneToOne()
    {
        var cfg = new AppConfig { DirtyMeta = true };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.SetAssistEnabled(false);
        var p = Pad(
            buttons:XButtons.A | XButtons.LeftShoulder,
            lt:123, rt:210,
            lx:-20000, ly:17000,
            rx:14000, ry:-15000);

        var r = e.ProcessFrameForTest(p);

        Assert.Equal(p.Buttons, r.Buttons);
        Assert.Equal(p.LeftTrigger, r.LT);
        Assert.Equal(p.RightTrigger, r.RT);
        Assert.Equal(p.ThumbLX, r.LX);
        Assert.Equal(p.ThumbLY, r.LY);
        Assert.Equal(p.ThumbRX, r.RX);
        Assert.Equal(p.ThumbRY, r.RY);

        e.SetAssistEnabled(true);
        Assert.True(e.AssistEnabled);
    }

    [Fact]
    public void AttackModeIsPermanentAndMovementCurvePreservesAngle()
    {
        var cfg = new AppConfig { DirtyMeta=true, MoveResponsePercent=118 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        short lx = -12000, ly = 18000;
        var r = e.ProcessFrameForTest(Pad(lt:211, rt:177, lx:lx, ly:ly), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);

        double inMag = Math.Sqrt((double)lx*lx + (double)ly*ly);
        double outMag = Math.Sqrt((double)r.LX*r.LX + (double)r.LY*r.LY);
        Assert.True(outMag >= inMag);

        double inAngle = Math.Atan2(lx, ly);
        double outAngle = Math.Atan2(r.LX, r.LY);
        double diff = Math.Abs(inAngle - outAngle);
        if (diff > Math.PI) diff = Math.Abs(diff - Math.PI*2);
        Assert.InRange(diff, 0, 0.01);
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
        Assert.Equal("Fast Driven Ground Pass", e.LastAction);

        Thread.Sleep(120);
        for (int i=0;i<5;i++)
        {
            e.ProcessFrameForTest(Pad(ry:30000), cfg);
            Thread.Sleep(15);
        }

        // Holding RS in the same direction must not fire another skill after the pass.
        Assert.Equal("Fast Driven Ground Pass", e.LastAction);

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
    public void YUsesPrecisionGroundThroughWithoutSyntheticHold()
    {
        var cfg = new AppConfig();
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        short lx = 17000, ly = 21000;
        var pressed = e.ProcessFrameForTest(
            Pad(XButtons.Y | XButtons.LeftShoulder, lx:lx, ly:ly), cfg);

        Assert.True(Has(pressed, XButtons.Y));
        Assert.True(Has(pressed, XButtons.RightShoulder));
        Assert.False(Has(pressed, XButtons.LeftShoulder));
        Assert.Equal(lx, pressed.LX);
        Assert.Equal(ly, pressed.LY);
        Assert.Equal("Precision Ground Through Pass", e.LastAction);

        var released = e.ProcessFrameForTest(Pad(lx:lx, ly:ly), cfg);
        Assert.False(Has(released, XButtons.Y));
        Assert.False(Has(released, XButtons.RightShoulder));
    }

    [Fact]
    public void QuickBTapLocksUsersReleaseAimThroughLowDrivenTail()
    {
        var cfg = new AppConfig
        {
            BTapThresholdMs=160,
            LowDrivenChargeMs=250,
            LowDrivenSecondTapGapMs=22,
            LowDrivenSecondTapMs=34
        };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        var down = e.ProcessFrameForTest(Pad(XButtons.B, lx:18000, ly:22000), cfg);
        Assert.True(Has(down, XButtons.B));

        Thread.Sleep(40);

        // User chooses a new aim at physical B release. This exact LS direction becomes the shot aim.
        var released = e.ProcessFrameForTest(Pad(lx:-21000, ly:16000), cfg);
        Assert.Equal("Low Driven Shot", e.LastAction);
        Assert.True(Has(released, XButtons.B));
        Assert.Equal((short)-21000, released.LX);
        Assert.Equal((short)16000, released.LY);

        // Moving LS after release must not randomly redirect the synthetic shot tail.
        var syntheticTail = e.ProcessFrameForTest(Pad(lx:25000, ly:-19000), cfg);
        Assert.Equal((short)-21000, syntheticTail.LX);
        Assert.Equal((short)16000, syntheticTail.LY);
    }

    [Fact]
    public void NormalShotTracksLsWhileHeldThenLocksLastUserAim()
    {
        var cfg = new AppConfig { BTapThresholdMs=100, BNormalShotCapMs=300 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B, lx:-19000, ly:26000), cfg);
        Thread.Sleep(125);

        // While B is still held, aim follows the physical LS live.
        var held = e.ProcessFrameForTest(Pad(XButtons.B, lx:15000, ly:27000), cfg);
        Assert.Equal("Normal Shot", e.LastAction);
        Assert.True(Has(held, XButtons.B));
        Assert.Equal((short)15000, held.LX);
        Assert.Equal((short)27000, held.LY);

        // Physical B release chooses/fixes the final user aim.
        var released = e.ProcessFrameForTest(Pad(lx:-23000, ly:12000), cfg);
        Assert.True(Has(released, XButtons.B));
        Assert.Equal((short)-23000, released.LX);
        Assert.Equal((short)12000, released.LY);

        // Later LS movement cannot randomly redirect the still-automated charge.
        var tail = e.ProcessFrameForTest(Pad(lx:26000, ly:-15000), cfg);
        Assert.Equal((short)-23000, tail.LX);
        Assert.Equal((short)12000, tail.LY);
    }

    [Fact]
    public void NormalShotUsesTwoBarPowerCapAndReleasesB()
    {
        var cfg = new AppConfig
        {
            DirtyMeta = true,
            BTapThresholdMs = 100,
            BNormalShotCapMs = 340
        };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B, lx:12000, ly:18000), cfg);
        Thread.Sleep(120);
        e.ProcessFrameForTest(Pad(XButtons.B, lx:12000, ly:18000), cfg);

        // Release physical B; app may finish the calibrated charge.
        var release = e.ProcessFrameForTest(Pad(lx:12000, ly:18000), cfg);
        Assert.True(Has(release, XButtons.B));

        Thread.Sleep(240);
        var capped = e.ProcessFrameForTest(Pad(lx:12000, ly:18000), cfg);
        Assert.False(Has(capped, XButtons.B));
    }

    [Fact]
    public void NormalShotAimStrengthPreservesUsersExactAngle()
    {
        var cfg = new AppConfig
        {
            DirtyMeta = true,
            BTapThresholdMs = 80,
            BNormalShotCapMs = 340,
            NormalShotAimMinMagnitude = 23000
        };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        short lx = 6000, ly = 9000;
        e.ProcessFrameForTest(Pad(XButtons.B, lx:lx, ly:ly), cfg);
        Thread.Sleep(100);
        e.ProcessFrameForTest(Pad(XButtons.B, lx:lx, ly:ly), cfg);

        var r = e.ProcessFrameForTest(Pad(lx:lx, ly:ly), cfg);

        double inputAngle = Math.Atan2(lx, ly);
        double outputAngle = Math.Atan2(r.LX, r.LY);
        double diff = Math.Abs(inputAngle - outputAngle);
        if (diff > Math.PI) diff = Math.Abs(diff - Math.PI * 2);

        double inputMag = Math.Sqrt((double)lx*lx + (double)ly*ly);
        double outputMag = Math.Sqrt((double)r.LX*r.LX + (double)r.LY*r.LY);

        Assert.InRange(diff, 0, 0.01);
        Assert.True(outputMag >= inputMag);
        Assert.InRange(outputMag, 22500, 23500);
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
    public void DirtyMovementCurveBoostsMagnitudeWithoutChangingDirection()
    {
        var cfg = new AppConfig { DirtyMeta=true, MoveResponsePercent=125 };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);

        short lx = 9000, ly = 15000;
        var r = e.ProcessFrameForTest(Pad(lx:lx, ly:ly), cfg);

        double inMag = Math.Sqrt((double)lx*lx + (double)ly*ly);
        double outMag = Math.Sqrt((double)r.LX*r.LX + (double)r.LY*r.LY);
        Assert.True(outMag > inMag);

        double a = Math.Atan2(lx, ly);
        double b = Math.Atan2(r.LX, r.LY);
        double d = Math.Abs(a-b);
        if (d > Math.PI) d = Math.Abs(d-Math.PI*2);
        Assert.InRange(d, 0, 0.01);
    }

    [Fact]
    public void RandomizedDirtyFramesStayBoundedAndDoNotThrow()
    {
        var cfg = new AppConfig { DirtyMeta=true };
        cfg.NormalizeAttackOnly();
        using var e = Engine(cfg);
        var rnd = new Random(27028);

        var buttons = new[]
        {
            XButtons.A, XButtons.X, XButtons.Y,
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
            Assert.InRange((int)r.LX, short.MinValue, short.MaxValue);
            Assert.InRange((int)r.LY, short.MinValue, short.MaxValue);
            Assert.InRange((int)r.RX, short.MinValue, short.MaxValue);
            Assert.InRange((int)r.RY, short.MinValue, short.MaxValue);

            // Face-button actions keep LS exact for shot/pass precision.
            if ((b & (XButtons.A|XButtons.X|XButtons.Y)) != 0)
            {
                Assert.Equal(lx,r.LX);
                Assert.Equal(ly,r.LY);
            }

            if(i%9==0)
                e.ProcessFrameForTest(Pad(),cfg);
        }
    }

}
