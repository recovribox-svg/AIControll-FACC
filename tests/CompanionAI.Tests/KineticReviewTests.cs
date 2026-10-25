// ★ v0.3.22: review regressions for bounded search, optional-load failure and explicitly partial audits.
using System;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using ProfileAudit;
using Xunit;
public class KineticReviewTests
{
    [Fact]
    public void ManyGroupsGrowBySumInsteadOfProduct()
    {
        var options = Enumerable.Range(0, 8).SelectMany(g => Enumerable.Range(0, 6).Select(i =>
            new KineticConfigurations.Option { Group = g, Weight = 1, Capacity = 1 })).ToArray();
        var sets = KineticConfigurations.OneGroupNeighbors(options, 128, out bool complete);
        Assert.True(complete);
        Assert.Equal(49, sets.Count); // exhaustive cross-product would be 7^8
        Assert.All(sets, set => Assert.True(set.Select((on, i) => on ? options[i].Group : -1).Where(g => g >= 0).Distinct().Count() <= 1));
    }
    [Fact]
    public void NeighborsPreservePlayerCapacityAndUnchangedGroups()
    {
        var options = new[] {
            new KineticConfigurations.Option { Group = 1, Capacity = 1, Weight = 1, Current = true, Locked = true },
            new KineticConfigurations.Option { Group = 1, Capacity = 1, Weight = 1 },
            new KineticConfigurations.Option { Group = 2, Capacity = 2, Weight = 2, Current = true },
            new KineticConfigurations.Option { Group = 2, Capacity = 2, Weight = 1 },
            new KineticConfigurations.Option { Group = 3, Capacity = 1, Weight = 1 }
        };
        var sets = KineticConfigurations.OneGroupNeighbors(options, 128, out bool complete);
        Assert.True(complete);
        Assert.All(sets, s => { Assert.True(s[0]); Assert.False(s[1]); Assert.False(s[2] && s[3]); });
        Assert.Contains(sets, s => s[4] && s[2]);
        Assert.DoesNotContain(sets, s => s[4] && s[3]);
    }
    [Fact]
    public void InterruptedNeighborSearchPublishesNoPrefix()
    {
        var options = Enumerable.Range(0, 8).Select(i => new KineticConfigurations.Option { Group = i, Capacity = 1, Weight = 1 }).ToArray();
        int checkpoints = 0;
        Assert.Empty(KineticConfigurations.OneGroupNeighbors(options, 128, out bool complete, () => ++checkpoints < 12));
        Assert.False(complete);
        Assert.Empty(KineticConfigurations.OneGroupNeighbors(options, 3, out complete));
        Assert.False(complete);
    }
    [Fact]
    public void SearchStopsOnEitherClockOrEvaluationBudgetAndStaysStopped()
    {
        double elapsed = 0;
        var budget = new KineticSearchBudget(40, 2, () => elapsed);
        budget.EvaluateAim(); budget.EvaluateAim();
        Assert.Throws<KineticBudgetExceededException>(() => budget.EvaluateAim());
        Assert.False(budget.Continue());
        var timeBudget = new KineticSearchBudget(40, 64, () => elapsed);
        elapsed = 40;
        Assert.Throws<KineticBudgetExceededException>(() => timeBudget.Check());
        elapsed = 0;
        Assert.False(timeBudget.Continue());
    }
    [Fact]
    public void OptionalPatchFailureRollsBackWithoutEscaping()
    {
        bool installed = false, rolledBack = false;
        Assert.False(OptionalPatchInstallation.TryInstall(() => { installed = true; throw new InvalidOperationException("Mono failure"); },
            () => { installed = false; rolledBack = true; }, out var failure));
        Assert.False(installed); Assert.True(rolledBack); Assert.IsType<InvalidOperationException>(failure);
        Assert.False(OptionalPatchInstallation.TryInstall(() => throw new Exception("patch"), () => throw new Exception("cleanup"), out failure));
        Assert.IsType<AggregateException>(failure);
    }
    [Fact]
    public void SuccessfulOptionalInstallationDoesNotRollBack()
    {
        bool installed = false;
        Assert.True(OptionalPatchInstallation.TryInstall(() => installed = true, () => throw new Exception("must not run"), out var failure));
        Assert.True(installed); Assert.Null(failure);
    }
    [Theory]
    [InlineData(4098, 0, 0, true)]
    [InlineData(4098, 1, 0, false)]
    [InlineData(4098, 1, 1, true)]
    [InlineData(4098, 2, 1, false)]
    [InlineData(0, 0, 99, false)]
    [InlineData(19, 4079, 9999, false)]
    public void AuditAllowanceCannotHideEmptyOrMassFailedLoads(int loaded, int failed, int allow, bool expected) =>
        Assert.Equal(expected, new AuditOptions { AllowFailures = allow }.CanAudit(loaded, failed));
    [Fact]
    public void AuditFlagsDoNotBecomeDumpFolders()
    {
        Assert.True(AuditOptions.TryParse(new[] { "--kinetic-checks", "dump", "--allow-failures", "1", "out.txt" }, out var options, out _));
        Assert.True(options.KineticChecks); Assert.Equal(1, options.AllowFailures);
        Assert.Equal(new[] { "dump" }, options.Roots); Assert.Equal("out.txt", options.Output);
        foreach (var invalid in new[] { new[] { "--allow-failures", "out" }, new[] { "--allow-failures", "-1", "dump", "out" }, new[] { "--unknown", "dump", "out" } })
            Assert.False(AuditOptions.TryParse(invalid, out _, out _));
    }
}
