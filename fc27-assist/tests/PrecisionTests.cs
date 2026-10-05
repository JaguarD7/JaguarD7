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
    public void BallRollSpinUsesLbAndDirectionThenForward()
    {
        foreach (var name in new[]{"Ball Roll Spin Right","Ball Roll Spin Left"})
        {
            var s = SkillLibrary.Get(name).Build(52);
            Assert.True((s[0].Down & XButtons.LeftShoulder) != 0);
            Assert.True((s[1].Down & XButtons.LeftShoulder) != 0);
            Assert.Equal(Dir.Forward, s[1].Rs);
            Assert.True((s[^1].Up & XButtons.LeftShoulder) != 0);
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
    public void DragTurnStartsForward()
    {
        Assert.Equal(Dir.Forward, SkillLibrary.Get("Drag Turn Right").Build(52)[0].Rs);
        Assert.Equal(Dir.Forward, SkillLibrary.Get("Drag Turn Left").Build(52)[0].Rs);
    }
}
