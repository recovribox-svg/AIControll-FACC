// ★ v0.3.22: behavioral checks for kineticist plan values and legal configurations.
using CompanionAI_Pathfinder.Tactics;
using Xunit;

public class KineticEffectsTests
{
    [Fact]
    public void BladeDamageCountsEveryHitButPersistentControlOnlyOnce()
    {
        var hits = new[] { 0.8f, 0.55f, 0.3f, 0.8f }; // an extra attack retains its own chance
        Assert.Equal(2.45f, ValueModel.RepeatedHitEffect(hits, 1f, true), 4);
        Assert.Equal(0.9874f, ValueModel.RepeatedHitEffect(hits, 1f, false), 4);
    }

    [Fact]
    public void EachBladeHitCanRetryAFailedInfusionSaveOrManeuver()
    {
        var hits = new[] { 0.8f, 0.8f };
        Assert.Equal(0.64f, ValueModel.RepeatedHitEffect(hits, 0.5f, false), 4);
        Assert.Equal(0f, ValueModel.RepeatedHitEffect(hits, 0f, false));
    }

    [Fact]
    public void RoundSavesReduceOngoingDamageAndRespectTheFightHorizon()
    {
        Assert.Equal(3f, ValueModel.ExpectedTicks(3f, 1f));
        Assert.Equal(1.75f, ValueModel.ExpectedTicks(3f, 0.5f));
        Assert.Equal(1.625f, ValueModel.ExpectedTicks(2.5f, 0.5f));
        Assert.Equal(0f, ValueModel.ExpectedTicks(0f, 1f));
    }
    [Fact]
    public void FutureDiscountRequiresAUsefulFollowUpBeforeCombatEnds()
    {
        Assert.Equal(0f, ValueModel.FutureUseValue(8f, 20f, 1f));
        Assert.Equal(0f, ValueModel.FutureUseValue(8f, 0f, 4f));
        Assert.Equal(4f, ValueModel.FutureUseValue(8f, 20f, 1.5f));
        Assert.Equal(6f, ValueModel.FutureUseValue(8f, 6f, 4f));
    }

    [Fact]
    public void LimitedDispelsFollowNativeOrderIncludingUselessAndHarmfulBuffs()
    {
        Assert.Equal(0f, ValueModel.LimitedSuccessValue(new[] { 1f, 1f }, new[] { 0f, 20f }, 1));
        Assert.Equal(10f, ValueModel.LimitedSuccessValue(new[] { 0.5f, 1f }, new[] { 0f, 20f }, 1));
        Assert.Equal(-3f, ValueModel.LimitedSuccessValue(new[] { 1f, 1f }, new[] { -3f, 20f }, 1));
        Assert.Equal(17f, ValueModel.LimitedSuccessValue(new[] { 1f, 1f }, new[] { -3f, 20f }, 2));
    }
    [Fact]
    public void AShortEffectOnAPassedSaveIsStillUsefulWithoutInflatingTheFailedDuration()
    {
        Assert.Equal(1f, ValueModel.SaveDuration(0f, 10f, 1f, 3f));
        Assert.Equal(1.2f, ValueModel.SaveDuration(0.1f, 10f, 1f, 3f), 4);
        Assert.Equal(3f, ValueModel.SaveDuration(1f, 10f, 1f, 3f));
    }
}
