// ★ v0.3.22: behavioral checks for kineticist plan values and legal configurations.
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

public class KineticConfigurationsTests
{
    [Fact]
    public void GroupsCompeteForTheirRuntimeCapacityAndIndependentModesCombine()
    {
        var options = new[]
        {
            new KineticConfigurations.Option { Group = 1, Weight = 1, Capacity = 1 },
            new KineticConfigurations.Option { Group = 1, Weight = 1, Capacity = 1 },
            new KineticConfigurations.Option { Group = 2, Weight = 1, Capacity = 1 }
        };
        var sets = KineticConfigurations.Enumerate(options, 20, out bool complete);
        Assert.True(complete);
        Assert.Equal(6, sets.Count);
        Assert.DoesNotContain(sets, s => s[0] && s[1]);
        Assert.Contains(sets, s => s[0] && s[2]);
        Assert.Contains(sets, s => s[1] && s[2]);
        Assert.Contains(sets, s => s.All(v => !v));
    }

    [Fact]
    public void PlayerOwnedChoiceConsumesCapacityAndCannotBeReplaced()
    {
        var options = new[]
        {
            new KineticConfigurations.Option { Group = 1, Weight = 1, Capacity = 1, Current = true, Locked = true },
            new KineticConfigurations.Option { Group = 1, Weight = 1, Capacity = 1 },
            new KineticConfigurations.Option { Group = 2, Weight = 1, Capacity = 1, Locked = true }
        };
        var sets = KineticConfigurations.Enumerate(options, 20, out bool complete);
        Assert.True(complete);
        Assert.Single(sets);
        Assert.Equal(new[] { true, false, false }, sets[0]);
    }

    [Fact]
    public void RuntimeCapacityAndWeightsAreNotAssumedToBeOne()
    {
        var options = new[]
        {
            new KineticConfigurations.Option { Group = 1, Weight = 2, Capacity = 3 },
            new KineticConfigurations.Option { Group = 1, Weight = 1, Capacity = 3 },
            new KineticConfigurations.Option { Group = 1, Weight = 2, Capacity = 3 }
        };
        var sets = KineticConfigurations.Enumerate(options, 20, out bool complete);
        Assert.True(complete);
        Assert.Contains(sets, s => s[0] && s[1]);
        Assert.DoesNotContain(sets, s => s[0] && s[2]);
    }

    [Fact]
    public void ExhaustedSearchBudgetCannotReturnABiasedPartialChoice()
    {
        var options = Enumerable.Range(0, 5).Select(g =>
            new KineticConfigurations.Option { Group = g, Weight = 1, Capacity = 1 }).ToArray();
        Assert.Empty(KineticConfigurations.Enumerate(options, 3, out bool complete));
        Assert.False(complete);
        Assert.Equal(32, KineticConfigurations.Enumerate(options, 32, out complete).Count);
        Assert.True(complete);
    }
}
