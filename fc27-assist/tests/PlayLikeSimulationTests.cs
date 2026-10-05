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

    private static void EnterDefense(ControllerEngine e, AppConfig cfg)
    {
        e.ProcessFrameForTest(Pad(lt:255), cfg);
        Assert.Equal(PlayMode.Defense, e.Mode);
        e.ProcessFrameForTest(Pad(), cfg);
    }

    [Fact]
    public void DefenseToAttackLbIsConsumedForWholePhysicalPress()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        var first = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.False(Has(first, XButtons.LeftShoulder));

        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var held = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.False(Has(held, XButtons.LeftShoulder));

        e.ProcessFrameForTest(Pad(), cfg); // release transition press

        var nativeStart = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.False(Has(nativeStart, XButtons.LeftShoulder)); // chord decision window
        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var nativeHeld = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.True(Has(nativeHeld, XButtons.LeftShoulder));
    }

    [Fact]
    public void SimultaneousLbRsFromDefenseExecutesSecondLayerWithoutRawLb()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.Equal("Skilled Bridge", e.LastAction);
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.Equal((byte)255, r.LT);
    }

    [Fact]
    public void StaleDefenseRsCannotBecomeAttackSkill()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        var switchFrame = e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.Equal((short)30000, switchFrame.RX); // native player switching

        var transition = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.NotEqual("Skilled Bridge", e.LastAction);
        Assert.Equal((short)0, transition.RX);
        Assert.False(Has(transition, XButtons.LeftShoulder));

        e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg); // RS back to center
        var freshFlick = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);
        Assert.Equal("Skilled Bridge", e.LastAction);
        Assert.Equal((byte)255, freshFlick.LT);
    }

    [Fact]
    public void NativeLbAfterIntentWindowCannotLaterTurnIntoLbSkill()
    {
        var cfg = new AppConfig { AutoPress=false };
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
    public void HeldRsDirectionTriggersOnlyOnceUntilCentered()
    {
        var cfg = new AppConfig { AutoPress=false, DirtyMeta=false, SkillCooldownMs=60, RsRearmMs=70 };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);

        // Cancel the macro manually, then keep the same physical RS direction held.
        e.ProcessFrameForTest(Pad(XButtons.A, ry:30000), cfg);
        Assert.Equal("Manual override", e.LastAction);

        Thread.Sleep(cfg.SkillCooldownMs + 100);
        for (int i = 0; i < 6; i++)
        {
            e.ProcessFrameForTest(Pad(ry:30000), cfg);
            Thread.Sleep(20);
        }

        // A held stick must not start the command again.
        Assert.Equal("Manual override", e.LastAction);

        // A brief center bounce is not enough to re-arm.
        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(20);
        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Manual override", e.LastAction);

        // Only a deliberate center hold re-arms the next flick.
        e.ProcessFrameForTest(Pad(), cfg);
        Thread.Sleep(cfg.RsRearmMs + 20);
        e.ProcessFrameForTest(Pad(), cfg);
        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);
    }

    [Fact]
    public void RunningSkillSuppressesPhysicalSprintAndModifiers()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(rt:255, ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);
        Assert.Equal((byte)0, r.RT);
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void ManualFaceButtonCancelsRunningSkill()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);

        var r = e.ProcessFrameForTest(Pad(XButtons.A), cfg);
        Assert.Equal("Manual override", e.LastAction);
        Assert.True(Has(r, XButtons.A));
        Assert.True(Has(r, XButtons.RightShoulder)); // Driven Pass
        Assert.False(Has(r, XButtons.LeftShoulder));
    }

    [Fact]
    public void QuickBReleaseBecomesCalibratedLowDriven()
    {
        var cfg = new AppConfig { AutoPress=false, BTapThresholdMs=180, LowDrivenChargeMs=260, LowDrivenSecondTapGapMs=30, LowDrivenSecondTapMs=45 };
        using var e = Engine(cfg);

        var down = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.True(Has(down, XButtons.B));

        Thread.Sleep(45);
        var released = e.ProcessFrameForTest(Pad(), cfg);
        Assert.Equal("Low Driven Shot", e.LastAction);
        Assert.True(Has(released, XButtons.B)); // app continues calibrated first charge

        Thread.Sleep(230);
        var gap = e.ProcessFrameForTest(Pad(), cfg);
        Assert.False(Has(gap, XButtons.B));

        Thread.Sleep(cfg.LowDrivenSecondTapGapMs + 5);
        e.ProcessFrameForTest(Pad(), cfg); // advance release-gap phase
        var secondTap = e.ProcessFrameForTest(Pad(), cfg);
        Assert.True(Has(secondTap, XButtons.B));
    }

    [Fact]
    public void HoldBUsesProgramPowerEvenIfFingerReleasesEarly()
    {
        var cfg = new AppConfig { AutoPress=false, BTapThresholdMs=100, BNormalShotCapMs=280 };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Thread.Sleep(125);
        var classified = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.Equal("Normal Strong Shot", e.LastAction);
        Assert.True(Has(classified, XButtons.B));

        var earlyPhysicalRelease = e.ProcessFrameForTest(Pad(), cfg);
        Assert.True(Has(earlyPhysicalRelease, XButtons.B)); // virtual hold continues

        Thread.Sleep(180);
        var capped = e.ProcessFrameForTest(Pad(), cfg);
        Assert.False(Has(capped, XButtons.B));
    }

    [Fact]
    public void ShotAutomationStripsLbAndRbModifiers()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        var r = e.ProcessFrameForTest(Pad(XButtons.B | XButtons.LeftShoulder | XButtons.RightShoulder), cfg);
        Assert.True(Has(r, XButtons.B));
        Assert.False(Has(r, XButtons.LeftShoulder));
        Assert.False(Has(r, XButtons.RightShoulder));
    }

    [Fact]
    public void ManualPassCancelsLowDrivenTail()
    {
        var cfg = new AppConfig { AutoPress=false, LowDrivenChargeMs=300 };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Thread.Sleep(40);
        e.ProcessFrameForTest(Pad(), cfg);
        Assert.Equal("Low Driven Shot", e.LastAction);

        var pass = e.ProcessFrameForTest(Pad(XButtons.A), cfg);
        Assert.Equal("Shot cancelled by manual input", e.LastAction);
        Assert.False(Has(pass, XButtons.B));
        Assert.True(Has(pass, XButtons.A));
        Assert.True(Has(pass, XButtons.RightShoulder));
    }

    [Fact]
    public void DefenseAutoPressDropsRbDuringManualTackle()
    {
        var cfg = new AppConfig { AutoPress=true, PressureStrength="Balanced", HardTackleAssist=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        Thread.Sleep(145);
        var pressure = e.ProcessFrameForTest(Pad(), cfg);
        Assert.True(Has(pressure, XButtons.RightShoulder));

        var tackle = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.True(Has(tackle, XButtons.B));
        Assert.False(Has(tackle, XButtons.RightShoulder));
    }

    [Fact]
    public void DefenseAutoPressDropsRbDuringRsPlayerSwitch()
    {
        var cfg = new AppConfig { AutoPress=true, PressureStrength="Balanced" };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        Thread.Sleep(145);
        Assert.True(Has(e.ProcessFrameForTest(Pad(), cfg), XButtons.RightShoulder));

        var switchPlayer = e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.Equal((short)30000, switchPlayer.RX);
        Assert.False(Has(switchPlayer, XButtons.RightShoulder));
    }

    [Fact]
    public void LtDuringSkillCancelsMacroAndImmediatelyDefends()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg);
        Assert.Equal("Explosive Stepover", e.LastAction);

        var defense = e.ProcessFrameForTest(Pad(lt:255), cfg);
        Assert.Equal(PlayMode.Defense, e.Mode);
        Assert.Equal("DEFENSE MODE", e.LastAction);
        Assert.Equal((byte)255, defense.LT);
        Assert.False(Has(defense, XButtons.LeftShoulder));
    }

    [Fact]
    public void DirtyMetaNeverOverridesLsDuringFaceButtonAction()
    {
        var cfg = new AppConfig { AutoPress=false, DirtyMeta=true };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ly:30000), cfg);
        e.ProcessFrameForTest(Pad(lx:30000), cfg); // create rapid fidget snap

        var manual = e.ProcessFrameForTest(Pad(XButtons.A, ly:-25000), cfg);
        Assert.Equal((short)0, manual.LX);
        Assert.Equal((short)-25000, manual.LY);
    }


    [Fact]
    public void HeldAttackShotCannotLeakIntoDefenseAsTackle()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        var transition = e.ProcessFrameForTest(Pad(XButtons.B, lt:255), cfg);

        Assert.Equal(PlayMode.Defense, e.Mode);
        Assert.False(Has(transition, XButtons.B));

        var stillHeld = e.ProcessFrameForTest(Pad(XButtons.B, lt:255), cfg);
        Assert.False(Has(stillHeld, XButtons.B));

        e.ProcessFrameForTest(Pad(lt:255), cfg); // physical B release
        var newTackle = e.ProcessFrameForTest(Pad(XButtons.B, lt:255), cfg);
        Assert.True(Has(newTackle, XButtons.B));
    }

    [Fact]
    public void HeldDefenseFaceButtonCannotLeakIntoAttackCommand()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        e.ProcessFrameForTest(Pad(XButtons.B), cfg); // defensive tackle begins
        var transition = e.ProcessFrameForTest(Pad(XButtons.B | XButtons.LeftShoulder), cfg);

        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.False(Has(transition, XButtons.B));
        Assert.False(Has(transition, XButtons.LeftShoulder));

        e.ProcessFrameForTest(Pad(), cfg); // release everything
        var newShot = e.ProcessFrameForTest(Pad(XButtons.B), cfg);
        Assert.True(Has(newShot, XButtons.B));
    }

    [Fact]
    public void ReleasingNativeLbWhileRsHeldDoesNotTriggerBaseSkill()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var native = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, rx:30000), cfg);
        Assert.True(Has(native, XButtons.LeftShoulder));
        Assert.Equal((short)30000, native.RX);

        var releaseLb = e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.NotEqual("Ball Roll Spin Right", e.LastAction);
        Assert.Equal((short)0, releaseLb.RX);

        e.ProcessFrameForTest(Pad(), cfg); // re-arm after RS center
        e.ProcessFrameForTest(Pad(rx:30000), cfg);
        Assert.Equal("Ball Roll Spin Right", e.LastAction);
    }

    [Fact]
    public void RandomizedPlayFramesDoNotThrowOrEmitAutoPressWithManualDefenseFaceInput()
    {
        var cfg = new AppConfig { AutoPress=true, HardTackleAssist=false, DirtyMeta=true };
        using var e = Engine(cfg);
        var rnd = new Random(27027);

        // Enter Defense, then fuzz common manual combinations.
        EnterDefense(e, cfg);
        var face = new[]{XButtons.A, XButtons.B, XButtons.X, XButtons.Y};

        for (int i=0; i<2000; i++)
        {
            var f = face[rnd.Next(face.Length)];
            short lx = (short)rnd.Next(-32767,32768);
            short ly = (short)rnd.Next(-32767,32768);
            short rx = (short)rnd.Next(-32767,32768);
            short ry = (short)rnd.Next(-32767,32768);
            var r = e.ProcessFrameForTest(Pad(f, lt:255, lx:lx, ly:ly, rx:rx, ry:ry), cfg);

            Assert.True(Has(r, f));
            Assert.False(Has(r, XButtons.RightShoulder));
            e.ProcessFrameForTest(Pad(lt:255), cfg); // release face input
        }
    }


    [Fact]
    public void RandomizedAttackFramesDoNotLeakShotModifiersOrThrow()
    {
        var cfg = new AppConfig
        {
            AutoPress=false,
            DirtyMeta=true,
            BTapThresholdMs=120,
            LowDrivenChargeMs=240,
            BNormalShotCapMs=320
        };
        using var e = Engine(cfg);
        var rnd = new Random(27028);
        var attackButtons = new[]
        {
            XButtons.A, XButtons.B, XButtons.X, XButtons.Y,
            XButtons.LeftShoulder, XButtons.RightShoulder,
            XButtons.A | XButtons.LeftShoulder,
            XButtons.B | XButtons.LeftShoulder,
            XButtons.B | XButtons.RightShoulder,
            XButtons.Y | XButtons.LeftShoulder,
            (XButtons)0
        };

        for (int i=0; i<3000; i++)
        {
            var b = attackButtons[rnd.Next(attackButtons.Length)];
            short lx = (short)rnd.Next(-32767,32768);
            short ly = (short)rnd.Next(-32767,32768);
            short rx = (short)rnd.Next(-32767,32768);
            short ry = (short)rnd.Next(-32767,32768);
            byte rt = (byte)rnd.Next(0,256);

            var r = e.ProcessFrameForTest(Pad(b, rt:rt, lx:lx, ly:ly, rx:rx, ry:ry), cfg);

            Assert.InRange(r.LX, short.MinValue, short.MaxValue);
            Assert.InRange(r.LY, short.MinValue, short.MaxValue);
            Assert.InRange(r.RX, short.MinValue, short.MaxValue);
            Assert.InRange(r.RY, short.MinValue, short.MaxValue);

            if (e.LastAction is "Low Driven Shot" or "Normal Strong Shot")
            {
                Assert.False(Has(r, XButtons.LeftShoulder));
                Assert.False(Has(r, XButtons.RightShoulder));
            }

            if ((b & (XButtons.A | XButtons.X | XButtons.Y)) != 0)
                Assert.False(Has(r, XButtons.B));

            if (i % 7 == 0)
                e.ProcessFrameForTest(Pad(), cfg);
        }
    }

    [Fact]
    public void RepeatedModeSwitchesNeverReplayTheSameTransition()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        var d1 = e.ProcessFrameForTest(Pad(lt:255), cfg);
        Assert.Equal(PlayMode.Defense, e.Mode);
        e.ProcessFrameForTest(Pad(lt:255), cfg);
        Assert.Equal(PlayMode.Defense, e.Mode);

        e.ProcessFrameForTest(Pad(), cfg);
        var a1 = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.False(Has(a1, XButtons.LeftShoulder));

        var a2 = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.False(Has(a2, XButtons.LeftShoulder)); // same transition press remains consumed

        e.ProcessFrameForTest(Pad(), cfg);
        e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Thread.Sleep(cfg.LbChordWindowMs + 20);
        var native = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder), cfg);
        Assert.True(Has(native, XButtons.LeftShoulder)); // new LB press while already attacking is native
    }


    [Fact]
    public void AttackRsHeldIntoDefenseIsQuarantinedUntilCenter()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);

        e.ProcessFrameForTest(Pad(ry:30000), cfg); // attack skill starts
        var transition = e.ProcessFrameForTest(Pad(lt:255, ry:30000), cfg);

        Assert.Equal(PlayMode.Defense, e.Mode);
        Assert.Equal((short)0, transition.RX);
        Assert.Equal((short)0, transition.RY);

        var stillHeld = e.ProcessFrameForTest(Pad(lt:255, ry:30000), cfg);
        Assert.Equal((short)0, stillHeld.RX);
        Assert.Equal((short)0, stillHeld.RY);

        e.ProcessFrameForTest(Pad(lt:255), cfg); // RS center releases quarantine
        var freshSwitch = e.ProcessFrameForTest(Pad(lt:255, rx:30000), cfg);
        Assert.Equal((short)30000, freshSwitch.RX);
    }

    [Fact]
    public void LtHeldWhileSwitchingBackToAttackDoesNotLeakAsAttackModifier()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        var transition = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, lt:255), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.Equal((byte)0, transition.LT);
        Assert.False(Has(transition, XButtons.LeftShoulder));

        var stillHeld = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder, lt:255), cfg);
        Assert.Equal((byte)0, stillHeld.LT);

        e.ProcessFrameForTest(Pad(), cfg); // release LT and LB
        var freshLt = e.ProcessFrameForTest(Pad(lt:255), cfg);
        Assert.Equal(PlayMode.Defense, e.Mode);
        Assert.Equal((byte)255, freshLt.LT);
    }

    [Fact]
    public void RbHeldAcrossDefenseToAttackIsBlockedUntilRelease()
    {
        var cfg = new AppConfig { AutoPress=false };
        using var e = Engine(cfg);
        EnterDefense(e, cfg);

        var transition = e.ProcessFrameForTest(Pad(XButtons.LeftShoulder | XButtons.RightShoulder), cfg);
        Assert.Equal(PlayMode.Attack, e.Mode);
        Assert.False(Has(transition, XButtons.RightShoulder));

        var stillHeld = e.ProcessFrameForTest(Pad(XButtons.RightShoulder), cfg);
        Assert.False(Has(stillHeld, XButtons.RightShoulder));

        e.ProcessFrameForTest(Pad(), cfg);
        var freshRb = e.ProcessFrameForTest(Pad(XButtons.RightShoulder), cfg);
        Assert.True(Has(freshRb, XButtons.RightShoulder));
    }
}
