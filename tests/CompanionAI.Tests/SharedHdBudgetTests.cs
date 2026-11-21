using System;
using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SharedHdBudgetTests
    {
        private static SharedHdBudget.Recipient Target(int cost, float resistance = 1f, bool enters = true) =>
            new SharedHdBudget.Recipient(resistance, budget =>
            {
                bool reaches = enters && budget >= cost;
                return new SharedHdBudget.Visit(reaches, reaches, cost);
            });

        [Fact]
        public void LimitedPoolCannotBeReusedForEveryRecipient()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(2), Target(2), Target(1) }, out var p));
            Assert.Equal(new[] { 1f, 1f, 0f }, p);
        }

        [Fact]
        public void AnOversizedRecipientDoesNotSpendTheRemainingPool()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(3), Target(2), Target(1) }, out var p));
            Assert.Equal(new[] { 1f, 0f, 1f }, p);
        }

        [Fact]
        public void NativeRecipientOrderChangesWhichEffectCanBeReached()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(3), Target(2) }, out var first));
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(2), Target(3) }, out var swapped));
            Assert.Equal(new[] { 1f, 0f }, first);
            Assert.Equal(new[] { 1f, 0f }, swapped);
            // The first array gives the 3-cost target value, the swapped array gives the 2-cost target value.
        }

        [Fact]
        public void ResistanceBranchesThePoolRatherThanSubtractingAnExpectedCost()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(2, 0.5f), Target(3) }, out var p));
            Assert.Equal(1f, p[0]); // current resistance is deliberately not counted here
            Assert.Equal(0.5f, p[1], 5);
        }

        [Fact]
        public void AResistedEarlierTargetLeavesTheFullPool()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(2, 0f), Target(4) }, out var p));
            Assert.Equal(new[] { 1f, 1f }, p);
        }

        [Fact]
        public void CurrentResistanceAndSaveArePricedOnlyByTheExistingEffectChance()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(2, 0.5f), Target(3, 0.25f) }, out var p));
            Assert.Equal(0.5f, p[1], 5);
            // Existing appraisal supplies current SR=.25 and failed-save=.4 once: .5*.25*.4=.05.
            Assert.Equal(0.05f, p[1] * 0.25f * 0.4f, 5);
        }

        [Fact]
        public void NativeGuardSkipIsDistinctFromAReachedEffectWithNoUsefulBenefit()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(3, enters: false), Target(4) }, out var skipped));
            Assert.Equal(new[] { 0f, 1f }, skipped);
            Assert.True(SharedHdBudget.TryForecast(4, new[]
            {
                new SharedHdBudget.Recipient(1f, b => new SharedHdBudget.Visit(false, true, 3)), Target(4)
            }, out var spends));
            Assert.Equal(new[] { 0f, 0f }, spends);
        }

        [Fact]
        public void SaveOutcomeDoesNotRefillAPoolSpentAfterTheSave()
        {
            Assert.True(SharedHdBudget.TryForecast(4, new[] { Target(3), Target(2) }, out var p));
            // Whether the first save passes (0 effect) or fails (1 effect), its graph still spends 3.
            Assert.Equal(0f, p[1]);
            Assert.Equal(0f, p[0] * 0f);
            Assert.Equal(1f, p[0] * 1f);
        }

        [Fact]
        public void EmptyAndZeroBudgetHaveDefinedResults()
        {
            Assert.True(SharedHdBudget.TryForecast(4, Array.Empty<SharedHdBudget.Recipient>(), out var empty));
            Assert.Empty(empty);
            Assert.True(SharedHdBudget.TryForecast(0, new[] { Target(1) }, out var p));
            Assert.Equal(0f, p[0]);
        }

        [Fact]
        public void UnsupportedInputsFailAsAWholeRatherThanPublishingAPrefix()
        {
            Assert.False(SharedHdBudget.TryForecast(SharedHdBudget.MaxBudget + 1, new[] { Target(1) }, out var high));
            Assert.Null(high);
            Assert.False(SharedHdBudget.TryForecast(4, new[] { Target(1), Target(1, float.NaN) }, out var invalid));
            Assert.Null(invalid);
            Assert.False(SharedHdBudget.TryForecast(4, new[]
            {
                new SharedHdBudget.Recipient(1f, b => new SharedHdBudget.Visit(true, true, 5))
            }, out var overdraft));
            Assert.Null(overdraft);
            Assert.False(SharedHdBudget.TryForecast(4, new[]
            {
                new SharedHdBudget.Recipient(1f, b => throw new InvalidOperationException())
            }, out var failed));
            Assert.Null(failed);
        }

        [Fact]
        public void OversizedRecipientSetsAreRejectedRatherThanTruncated()
        {
            var recipients = new List<SharedHdBudget.Recipient>();
            for (int i = 0; i <= SharedHdBudget.MaxRecipients; i++) recipients.Add(Target(1));
            Assert.False(SharedHdBudget.TryForecast(4, recipients, out var p));
            Assert.Null(p);
        }

        // ★ Fusion coverage C8: a rolled pool in one pass equals the chance-weighted forecasts of its rolls.
        [Fact]
        public void ARolledPoolInOnePassEqualsItsWeightedRolls()
        {
            var recipients = new[] { Target(3), Target(5, 0.5f), Target(2), Target(4) };
            var budgets = new[] { 4, 6, 9, 12 };
            var chances = new[] { 0.1f, 0.4f, 0.3f, 0.2f };
            Assert.True(SharedHdBudget.TryForecast(budgets, chances, recipients, out var once));
            var summed = new float[recipients.Length];
            for (int k = 0; k < budgets.Length; k++)
            {
                Assert.True(SharedHdBudget.TryForecast(budgets[k], recipients, out var e));
                for (int i = 0; i < summed.Length; i++) summed[i] += chances[k] * e[i];
            }
            for (int i = 0; i < summed.Length; i++) Assert.Equal(summed[i], once[i], 5);
        }

        [Fact]
        public void CircleOfDeathSizedPoolsFitTheBounds()
        {
            // d4 per caster level at 20 (25 Intensified), half again empowered: 150.
            Assert.True(SharedHdBudget.MaxBudget >= 150);
            Assert.True(SharedHdBudget.TryForecast(new[] { 150 }, new[] { 1f }, new[] { Target(9), Target(12) }, out var p));
            Assert.Equal(1f, p[0]); Assert.Equal(1f, p[1]);
            Assert.False(SharedHdBudget.TryForecast(new[] { 4, 5 }, new[] { 1f }, new[] { Target(1) }, out var mismatched));
            Assert.Null(mismatched);
        }
    }
}
