using FC27Assist;
using Xunit;

public class PrecisionTests
{
    [Fact]
    public void CoreMappingsExistAndAreUnique()
    {
        Assert.Equal(SkillLibrary.Skills.Count, SkillLibrary.Skills.Select(x => x.Name).Distinct().Count());
        var cfg = new AppConfig();
        foreach (var key in new[]{"RS_UP","RS_RIGHT","RS_LEFT","RS_DOWN","LB_RS_UP","LB_RS_RIGHT","LB_RS_LEFT","LB_RS_DOWN"})
        {
            Assert.True(cfg.SkillMap.ContainsKey(key), $"Missing mapping {key}");
            Assert.Contains(SkillLibrary.Skills, s => s.Name == cfg.SkillMap[key]);
        }
    }

    [Fact]
    public void ShotThresholdsAreOrderedSafely()
    {
        var c = new AppConfig();
        Assert.InRange(c.BTapThresholdMs, 90, 300);
        Assert.True(c.LowDrivenChargeMs > c.BTapThresholdMs);
        Assert.True(c.BNormalShotCapMs > c.BTapThresholdMs);
        Assert.InRange(c.LowDrivenSecondTapGapMs, 10, 100);
        Assert.InRange(c.LowDrivenSecondTapMs, 20, 100);
        Assert.InRange(c.LbChordWindowMs, 30, 160);
        Assert.InRange(c.InputLoopHz, 250, 1000);
    }

    [Fact]
    public void ExplosiveStepoverKeepsLbHeld()
    {
        var s = SkillLibrary.Get("Explosive Stepover").Build(52);
        Assert.True(s.Count >= 3);
        Assert.True((s[0].Down & XButtons.LeftShoulder) != 0);
        Assert.True((s[1].Down & XButtons.LeftShoulder) != 0);
        Assert.True((s[^1].Up & XButtons.LeftShoulder) != 0);
    }

    [Fact]
    public void BallRollSpinUsesDirectionThenForwardWithoutModifier()
    {
        foreach (var name in new[]{"Ball Roll Spin Right","Ball Roll Spin Left"})
        {
            var steps = SkillLibrary.Get(name).Build(52);
            Assert.Equal((XButtons)0, steps[0].Down & XButtons.LeftShoulder);
            Assert.Equal((XButtons)0, steps[1].Down & XButtons.LeftShoulder);
            Assert.Equal(Dir.Forward, steps[1].Rs);
        }
        Assert.Equal(Dir.Right, SkillLibrary.Get("Ball Roll Spin Right").Build(52)[0].Rs);
        Assert.Equal(Dir.Left, SkillLibrary.Get("Ball Roll Spin Left").Build(52)[0].Rs);
    }

    [Fact]
    public void StepoverBallUsesLbForwardThenSide()
    {
        foreach (var name in new[]{"Stepover Ball Right","Stepover Ball Left"})
        {
            var s = SkillLibrary.Get(name).Build(52);
            Assert.Equal(Dir.Forward, s[0].Rs);
            Assert.True((s[0].Down & XButtons.LeftShoulder) != 0);
            Assert.True((s[1].Down & XButtons.LeftShoulder) != 0);
        }
    }

    [Fact]
    public void LateralHeelToHeelUsesLb()
    {
        var s = SkillLibrary.Get("Lateral Heel to Heel").Build(52);
        Assert.Equal(Dir.Left, s[0].Rs);
        Assert.Equal(Dir.Right, s[1].Rs);
        Assert.True((s[0].Down & XButtons.LeftShoulder) != 0);
        Assert.True((s[1].Down & XButtons.LeftShoulder) != 0);
    }

    [Fact]
    public void SkilledBridgeUsesLtForwardBack()
    {
        var s = SkillLibrary.Get("Skilled Bridge").Build(52);
        Assert.Equal(Dir.Forward, s[0].Rs);
        Assert.Equal(Dir.Back, s[1].Rs);
        Assert.Equal((byte)255, s[0].Lt);
        Assert.Equal((byte)255, s[1].Lt);
    }

    [Fact]
    public void StopAndGoUsesLtBackForward()
    {
        var s = SkillLibrary.Get("Stop and Go").Build(52);
        Assert.Equal(Dir.Back, s[0].Rs);
        Assert.Equal(Dir.Forward, s[1].Rs);
        Assert.Equal((byte)255, s[0].Lt);
        Assert.Equal((byte)255, s[1].Lt);
    }

    [Fact]
    public void TricksterFakeShotHasLbShotPassAndRsExit()
    {
        var s = SkillLibrary.Get("Trickster Fake Shot").Build(52);
        Assert.True((s[0].Down & XButtons.LeftShoulder) != 0);
        Assert.True((s[0].Down & XButtons.B) != 0);
        Assert.True((s[1].Down & XButtons.A) != 0);
        Assert.NotEqual(Dir.None, s[2].Rs);
    }

    [Fact]
    public void DragTurnStartsBackThenExitsSide()
    {
        Assert.Equal(Dir.Back, SkillLibrary.Get("Drag Turn Right").Build(52)[0].Rs);
        Assert.Equal(Dir.Right, SkillLibrary.Get("Drag Turn Right").Build(52)[1].Rs);
        Assert.Equal(Dir.Back, SkillLibrary.Get("Drag Turn Left").Build(52)[0].Rs);
        Assert.Equal(Dir.Left, SkillLibrary.Get("Drag Turn Left").Build(52)[1].Rs);
    }

    [Fact]
    public void FakeDirectionMovesUseSmoothHalfArcs()
    {
        var l = SkillLibrary.Get("Fake Left Go Right").Build(52);
        Assert.Equal(new[]{Dir.Left,Dir.BackLeft,Dir.Back,Dir.BackRight,Dir.Right}, l.Take(5).Select(x=>x.Rs).ToArray());
        var r = SkillLibrary.Get("Fake Right Go Left").Build(52);
        Assert.Equal(new[]{Dir.Right,Dir.BackRight,Dir.Back,Dir.BackLeft,Dir.Left}, r.Take(5).Select(x=>x.Rs).ToArray());
    }

    [Fact]
    public void LbModeTransitionIsConsumedUntilRelease()
    {
        Assert.True(ConflictRules.SuppressLb(true, false, 500, 85));
        Assert.True(ConflictRules.SuppressLb(false, true, 500, 85));
        Assert.True(ConflictRules.SuppressLb(false, false, 40, 85));
        Assert.False(ConflictRules.SuppressLb(false, false, 120, 85));
    }

    [Fact]
    public void ManualFaceButtonsOverrideSkillMacros()
    {
        Assert.True(ConflictRules.ManualFaceOverride((ushort)XButtons.A));
        Assert.True(ConflictRules.ManualFaceOverride((ushort)XButtons.B));
        Assert.True(ConflictRules.ManualFaceOverride((ushort)XButtons.X));
        Assert.True(ConflictRules.ManualFaceOverride((ushort)XButtons.Y));
        Assert.False(ConflictRules.ManualFaceOverride((ushort)XButtons.RightShoulder));
    }

    [Fact]
    public void AutoPressIsBlockedByDefensiveManualActions()
    {
        Assert.True(ConflictRules.BlockAutoPress((ushort)XButtons.A, false));
        Assert.True(ConflictRules.BlockAutoPress((ushort)XButtons.B, false));
        Assert.True(ConflictRules.BlockAutoPress((ushort)XButtons.X, false));
        Assert.True(ConflictRules.BlockAutoPress((ushort)XButtons.Y, false));
        Assert.True(ConflictRules.BlockAutoPress(0, true));
        Assert.False(ConflictRules.BlockAutoPress(0, false));
    }

    [Fact]
    public void ShotAutomationOwnsLbRbModifiers()
    {
        Assert.True(ConflictRules.ShotOwnsModifiers(true, false));
        Assert.True(ConflictRules.ShotOwnsModifiers(false, true));
        Assert.False(ConflictRules.ShotOwnsModifiers(false, false));
    }

    [Fact]
    public void EightWayVectorsIncludeDiagonals()
    {
        var f = MacroRunner.RotatedVector(Dir.Forward, 0);
        var fr = MacroRunner.RotatedVector(Dir.ForwardRight, 0);
        var r = MacroRunner.RotatedVector(Dir.Right, 0);

        // XInput contract: +X is right, -X is left, +Y is up.
        Assert.True(f.y > 25000 && Math.Abs(f.x) < 1000);
        Assert.True(fr.x > 18000 && fr.y > 18000);
        Assert.True(r.x > 25000 && Math.Abs(r.y) < 1000);

        var l = MacroRunner.RotatedVector(Dir.Left, 0);
        Assert.True(l.x < -25000 && Math.Abs(l.y) < 1000);
    }

    [Fact]
    public void MacroRunnerNeverSkipsRequiredEdgesAfterSchedulerStall()
    {
        var runner = new MacroRunner();
        var steps = new List<MacroStep>
        {
            new(10, Down:XButtons.A),
            new(10, Up:XButtons.A, Down:XButtons.B),
            new(10, Up:XButtons.B, Down:XButtons.X),
            new(10, Up:XButtons.X)
        };

        runner.Start("stall-test", steps, 0);
        Thread.Sleep(45);

        var first = new VirtualReport();
        runner.Apply(first);

        // A long OS stall may advance one macro edge, but must not jump straight to completion.
        Assert.True(runner.Active);
        Assert.False((first.Buttons & (ushort)XButtons.A) != 0);
        Assert.True((first.Buttons & (ushort)XButtons.B) != 0);
    }

    [Fact]
    public void ExplosiveStepoverUsesIntermediateDiagonal()
    {
        var steps = SkillLibrary.Get("Explosive Stepover").Build(52);
        Assert.Equal(Dir.Forward, steps[0].Rs);
        Assert.Equal(Dir.ForwardRight, steps[1].Rs);
        Assert.Equal(Dir.Right, steps[2].Rs);
    }


    [Fact]
    public void RelativeDirectionsRotateCorrectlyWithPlayerFacing()
    {
        // Facing right: local Forward = screen/right (+X), local Right = screen/back (-Y).
        var facingRight = Math.PI / 2;
        var forward = MacroRunner.RotatedVector(Dir.Forward, facingRight);
        var localRight = MacroRunner.RotatedVector(Dir.Right, facingRight);
        var localLeft = MacroRunner.RotatedVector(Dir.Left, facingRight);

        Assert.True(forward.x > 25000 && Math.Abs(forward.y) < 1000);
        Assert.True(localRight.y < -25000 && Math.Abs(localRight.x) < 1000);
        Assert.True(localLeft.y > 25000 && Math.Abs(localLeft.x) < 1000);
    }

}
