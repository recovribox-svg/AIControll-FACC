using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SelectedApproachFilterTests
    {
        private sealed class Plan
        {
            public string Key;
            public bool Walks, Quick, Ready;
            public Plan(string key, bool walks = false, bool ready = false, bool quick = false)
            { Key = key; Walks = walks; Ready = ready; Quick = quick; }
        }

        private static Plan SelectMain(IReadOnlyList<Plan> plans) => plans.FirstOrDefault(p => !p.Quick);
        private static bool NeedsCheck(Plan plan) => !plan.Quick && plan.Walks;

        [Fact]
        public void InReachMainDoesNotScanLowerWalkingPlans()
        {
            var plans = new[] { new Plan("in reach"), new Plan("distant", walks: true) };
            int calls = 0;
            var result = SelectedApproachFilter.Apply(plans, SelectMain, NeedsCheck, p => { calls++; return p.Ready; });
            Assert.Equal(0, calls);
            Assert.Equal(plans, result);
        }

        [Fact]
        public void RefusedMainUsesAlreadyScoredFallbackBeforeAnyBooster()
        {
            var boost = new Plan("boost", quick: true);
            var bad = new Plan("blocked", walks: true);
            var good = new Plan("reachable", walks: true, ready: true);
            var untouched = new Plan("lower", walks: true);
            var calls = new List<string>();
            var result = SelectedApproachFilter.Apply(new[] { boost, bad, good, untouched }, SelectMain, NeedsCheck,
                p => { calls.Add(p.Key); return p.Ready; });
            Assert.Equal(new[] { "blocked", "reachable" }, calls);
            Assert.DoesNotContain(bad, result);
            Assert.Contains(boost, result);
            Assert.Contains(untouched, result);
            Assert.Same(good, SelectMain(result));
        }

        [Fact]
        public void WorkLimitPreservesQuickAndInReachOptions_AndDoesNotClaimRemainingPathsFailed()
        {
            var quick = new Plan("quick", walks: true, quick: true);
            var walking = Enumerable.Range(0, 6).Select(i => new Plan(i.ToString(), walks: true)).ToArray();
            var near = new Plan("near");
            int calls = 0, omitted = 0;
            var rejected = new List<Plan>();
            var result = SelectedApproachFilter.Apply(new[] { quick }.Concat(walking).Concat(new[] { near }), SelectMain,
                NeedsCheck, p => { calls++; return false; }, rejected.Add, count => omitted = count);
            Assert.Equal(SelectedApproachFilter.MaxChecks, calls);
            Assert.Equal(3, rejected.Count);
            Assert.Equal(3, omitted);
            Assert.Equal(new[] { quick, near }, result);
        }

        [Fact]
        public void ChangedRangeOrNewDecisionCanTrySameTargetAgain()
        {
            var melee = new Plan("same target/melee", walks: true);
            var ranged = new Plan("same target/ranged");
            var first = SelectedApproachFilter.Apply(new[] { melee, ranged }, SelectMain, NeedsCheck, p => p.Ready);
            Assert.Same(ranged, SelectMain(first));
            melee.Ready = true; // moved obstacle/new actor position/range is read afresh on the next invocation
            var second = SelectedApproachFilter.Apply(new[] { melee, ranged }, SelectMain, NeedsCheck, p => p.Ready);
            Assert.Same(melee, SelectMain(second));
        }

        [Fact]
        public void QuickOnlyDecisionDoesNotPathfind()
        {
            int calls = 0;
            var result = SelectedApproachFilter.Apply(new[] { new Plan("quick", quick: true) }, SelectMain, NeedsCheck,
                p => { calls++; return false; });
            Assert.Single(result);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void RefusedLowerTierExposesPreviouslyHeldHigherTier()
        {
            // The higher tier has the better value, but the policy initially holds it while a lower tier is eligible.
            var high = new Plan("high tier", walks: true, ready: true);
            var low = new Plan("low tier", walks: true);
            var calls = new List<Plan>();
            Plan SelectPolicyMain(IReadOnlyList<Plan> plans) => plans.Contains(low) ? low : SelectMain(plans);
            var result = SelectedApproachFilter.Apply(new[] { high, low }, SelectPolicyMain, NeedsCheck,
                p => { calls.Add(p); return p.Ready; });
            Assert.Equal(new[] { low, high }, calls);
            Assert.DoesNotContain(low, result);
            Assert.Same(high, SelectPolicyMain(result));
        }
    }
}
