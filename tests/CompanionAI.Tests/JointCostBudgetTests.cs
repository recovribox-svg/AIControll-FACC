using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Xunit;
using Charge = CompanionAI_Pathfinder.Tactics.JointCostBudget.Charge;

namespace CompanionAI.Tests
{
    public class JointCostBudgetTests
    {
        private static readonly Charge[] Free = Array.Empty<Charge>();
        private static Charge[] Pool(object key, int available, int amount = 1, int level = -1, bool consume = true) =>
            new[] { Charge.FromPool(key, level, available, amount, consume) };
        private static Charge[] Prepared(int amount, params object[] slots) => new[] { Charge.FromPrepared(slots, amount) };

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void TwoSpellsShareTheActualBookAndSlotLevel(int left, bool expected)
        {
            var book = new object();
            Assert.Equal(expected, new JointCostBudget().CanPay(Pool(book, left, level: 3), Pool(book, left, level: 3)));
        }

        [Fact]
        public void DifferentBooksAndLevelsDoNotSpendEachOthersSlots()
        {
            var first = new object(); var second = new object(); var budget = new JointCostBudget();
            Assert.True(budget.CanPay(Pool(first, 1, level: 3), Pool(second, 1, level: 3)));
            Assert.True(budget.CanPay(Pool(first, 1, level: 3), Pool(first, 1, level: 4)));
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        public void ExtraSpellSlotCostConsumesMoreThanOneEntry(int left, bool expected)
        {
            var book = new object();
            Assert.Equal(expected, new JointCostBudget().CanPay(Pool(book, left, amount: 2, level: 4), Pool(book, left, level: 4)));
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        public void SetupMainAndSecondCastMustAllBeAffordable(int left, bool expected)
        {
            var resource = new object(); var plan = Pool(resource, left);
            Assert.Equal(expected, new JointCostBudget().CanPay(plan, plan, plan));
        }

        [Fact]
        public void SetupCannotUseTheLastPointRequiredByItsMainSpell()
        {
            var reservoir = new object(); var book = new object();
            var main = Pool(reservoir, 1).Concat(Pool(book, 2, level: 2)).ToArray();
            Assert.False(new JointCostBudget().CanPay(Pool(reservoir, 1), main));
        }

        [Fact]
        public void DifferentPreparedCastsCannotReuseOneSourceSlot()
        {
            var source = new object();
            Assert.False(new JointCostBudget().CanPay(Prepared(1, source), Prepared(1, source)));
        }

        [Fact]
        public void SeparatePreparedCopiesOfTheSameSpellCanBothPay()
        {
            var first = new object(); var second = new object(); var copies = Prepared(1, second, first);
            Assert.True(new JointCostBudget().CanPay(copies, copies));
        }

        [Fact]
        public void PreparedAllocationFollowsNativeSpendOrder()
        {
            var common = new object(); var alternative = new object(); var budget = new JointCostBudget();
            Assert.False(budget.CanPay(Prepared(1, common, alternative), Prepared(1, common)));
            Assert.True(budget.CanPay(Prepared(1, alternative, common), Prepared(1, common)));
        }

        [Fact]
        public void PreparedExtraCostNeedsDistinctAvailableSlots()
        {
            var first = new object(); var second = new object(); var third = new object();
            Assert.False(new JointCostBudget().CanPay(Prepared(2, first, second), Prepared(1, first, second)));
            Assert.True(new JointCostBudget().CanPay(Prepared(2, first, second, third), Prepared(1, first, second, third)));
        }

        [Fact]
        public void DuplicateSlotReferencesDoNotCreateAnotherPreparedCharge()
        {
            var slot = new object();
            Assert.False(new JointCostBudget().CanPay(Prepared(2, slot, slot), Free));
        }

        [Fact]
        public void NonconsumingRestrictionAndOverridePaymentUseTheirOwnPools()
        {
            var required = new object(); var paid = new object();
            var setup = Pool(required, 2, amount: 2, consume: false).Concat(Pool(paid, 2, amount: 2)).ToArray();
            Assert.True(new JointCostBudget().CanPay(setup, Pool(required, 2, amount: 2)));
            Assert.False(new JointCostBudget().CanPay(setup, Pool(paid, 2)));
        }

        [Fact]
        public void DailyUsePrecedesResourceLogicRestrictionOnTheSamePool()
        {
            var resource = new object();
            var cast = Pool(resource, 2)
                .Concat(Pool(resource, 2, amount: 2, consume: false))
                .Concat(Pool(resource, 2, amount: 2)).ToArray();
            Assert.False(new JointCostBudget().CanPay(Free, cast));
        }

        [Fact]
        public void ARestrictionObservesWhatEarlierActionsAlreadySpent()
        {
            var resource = new object();
            Assert.False(new JointCostBudget().CanPay(Pool(resource, 1), Pool(resource, 1, consume: false)));
        }

        [Fact]
        public void FailedAlternativesDoNotLeakReservationsIntoTheNextAttempt()
        {
            var resource = new object(); var slot = new object(); var budget = new JointCostBudget();
            Assert.False(budget.CanPay(Pool(resource, 1), Pool(resource, 1)));
            Assert.True(budget.CanPay(Free, Pool(resource, 1)));
            Assert.False(budget.CanPay(Prepared(1, slot), Prepared(1, slot)));
            Assert.True(budget.CanPay(Free, Prepared(1, slot)));
        }

        [Fact]
        public void UnknownAndKnownFreeAreDifferentIncludingTheThirdAction()
        {
            var budget = new JointCostBudget();
            Assert.True(budget.CanPay(Free, Free));
            Assert.True(budget.CanPay(Free, Free, Free));
            Assert.False(budget.CanPay(null, Free));
            Assert.False(budget.CanPay(Free, null));
            Assert.False(budget.CanPay(Free, Free, null));
        }

        [Fact]
        public void ContradictorySnapshotsCannotInventAvailableUnits()
        {
            var resource = new object();
            Assert.False(new JointCostBudget().CanPay(Pool(resource, 1), Pool(resource, 3)));
        }

        [Fact]
        public void InputsAndPreparedCollectionsRemainUnchanged()
        {
            var a = new object(); var b = new object(); var slots = new[] { b, a }; var resource = new object();
            var first = Prepared(1, slots); var second = Pool(resource, 2);
            Assert.True(new JointCostBudget().CanPay(first, second));
            Assert.Equal(new[] { b, a }, slots);
            Assert.Equal(2, second[0].Available);
            Assert.Equal(1, first[0].Amount);
        }

        private sealed class NativeIdentity
        {
            public override bool Equals(object obj) => throw new InvalidOperationException("Virtual/native equality must not run");
            public override int GetHashCode() => throw new InvalidOperationException("Virtual/native hash must not run");
        }

        [Fact]
        public void PoolAndSlotIdentityNeverCallNativeEqualityOrHash()
        {
            var a = new NativeIdentity(); var b = new NativeIdentity(); var budget = new JointCostBudget();
            Assert.True(budget.CanPay(Pool(a, 1), Pool(b, 1)));
            Assert.True(budget.CanPay(Prepared(1, a, b), Prepared(1, a, b)));
            Assert.False(budget.CanPay(Pool(a, 1), Pool(a, 1)));
        }

        [Fact]
        public void LargeCostsCannotWrapIntoAffordability()
        {
            var resource = new object(); var entire = Pool(resource, int.MaxValue, int.MaxValue);
            Assert.False(new JointCostBudget().CanPay(entire, entire));
        }

        [Fact]
        public void MalformedOrUnboundedPlansFailClosed()
        {
            var resource = new object(); var budget = new JointCostBudget();
            Assert.False(budget.CanPay(Pool(resource, 1, -1), Free));
            Assert.False(budget.CanPay(Pool(resource, -1), Free));
            Assert.False(budget.CanPay(Pool(null, 1), Free));
            Assert.False(budget.CanPay(Prepared(1, new object[] { null }), Free));
            Assert.False(budget.CanPay(Enumerable.Repeat(Charge.FromPool(resource, 100, 100, 1), JointCostBudget.MaxCharges + 1).ToArray(), Free));
            Assert.False(budget.CanPay(Prepared(1, Enumerable.Range(0, JointCostBudget.MaxPreparedSlots + 1).Select(_ => new object()).ToArray()), Free));
        }
    }
}
