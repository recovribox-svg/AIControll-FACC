// ★ v0.3.22: behavioral checks for kineticist plan values and legal configurations.
using CompanionAI_Pathfinder.Tactics;
using Xunit;

public class KineticGatherTests
{
    [Fact]
    public void OneGatherLeavesAStandardBlastButTwoRequireAQuickenedBlast()
    {
        Assert.True(Fits(1, true, true, true, false, false));
        Assert.False(Fits(2, true, true, true, false, false));
        Assert.True(Fits(2, true, true, true, false, true));
        Assert.False(Fits(3, true, true, true, false, true));
    }

    [Fact]
    public void ASpentMoveCanUseStandardOnlyIfTheBlastIsSwift()
    {
        Assert.False(Fits(1, false, true, true, false, false));
        Assert.True(Fits(1, false, true, true, false, true));
        Assert.False(Fits(1, false, true, false, false, true));
    }

    [Fact]
    public void SurpriseOrStaggeredBudgetCannotGatherAndCastAStandardBlast()
    {
        Assert.False(Fits(1, true, true, true, true, false));
        Assert.True(Fits(1, true, true, true, true, true));
        Assert.False(Fits(2, true, true, true, true, true));
    }

    [Fact]
    public void UsefulMovementCanOutweighBurnSavingsOnTheSameScale()
    {
        float noGather = 18f;
        Assert.True(ValueModel.PreparedBlastValue(24f, 0f, 0f, 0f, 0f, 0f) > noGather);
        Assert.True(ValueModel.PreparedBlastValue(24f, 9f, 0f, 0f, 0f, 0f) < noGather);
    }

    [Fact]
    public void NativeDelayAndProvocationLowerTheCompletePlanValue()
    {
        float direct = ValueModel.PreparedBlastValue(24f, 0f, 6f, 0f, 0f, 0f);
        float delayed = ValueModel.PreparedBlastValue(24f, 0f, 6f, 3f, 0f, 0f);
        float risky = ValueModel.PreparedBlastValue(24f, 0f, 6f, 3f, 4f, 0.5f);
        Assert.Equal(24f, direct);
        Assert.Equal(16f, delayed);
        Assert.Equal(4f, risky);
    }

    [Fact]
    public void PreparationMustImproveTheMainActionBeforeTheFightEnds()
    {
        Assert.Equal(0f, ValueModel.PreparationGain(10f, 12f, 4f, 1f));
        Assert.Equal(0f, ValueModel.PreparationGain(20f, 12f, 1f, 2f));
        Assert.Equal(16f, ValueModel.PreparationGain(20f, 12f, 3f, 1f));
    }

    private static bool Fits(int n, bool move, bool standard, bool swift, bool limited, bool quick) =>
        KineticConfigurations.GatherFits(n, move, standard, swift, limited,
            castMove: false, castStandard: !quick, castSwift: quick, castFullRound: false, out _, out _);
}
