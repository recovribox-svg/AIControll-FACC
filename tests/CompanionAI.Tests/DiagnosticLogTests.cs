using CompanionAI_Pathfinder.Core;
using Xunit;

namespace CompanionAI.Tests
{
    public class DiagnosticLogTests
    {
        [Fact]
        public void ARepeatedRoundOneCastReportsAgainInTheNextFight()
        {
            var reported = new System.Collections.Generic.HashSet<string>();
            string Key(int fight) => DiagnosticLogPolicy.RoundNoteKey("buffwhy", fight, "Lann", 1, "Dexterity2:Lann");
            Assert.True(reported.Add(Key(1)));
            Assert.False(reported.Add(Key(1)));
            Assert.True(reported.Add(Key(7))); // the gaps2 RT1 cast was hidden by the initial TB R1 placement
            Assert.False(reported.Add(Key(7)));
        }

        [Theory]
        [InlineData("restraint", 1, "Lann", 1, "Dexterity2:Lann")]
        [InlineData("buffwhy", 1, "other caster", 1, "Dexterity2:Lann")]
        [InlineData("buffwhy", 1, "Lann", 2, "Dexterity2:Lann")]
        [InlineData("buffwhy", 1, "Lann", 1, "Dexterity2:Tripper")]
        public void RoundNotesKeepDifferentKindsActorsRoundsAndRecipientsSeparate(string family, int fight, string unit, int round, string option)
        {
            Assert.NotEqual(DiagnosticLogPolicy.RoundNoteKey("buffwhy", 1, "Lann", 1, "Dexterity2:Lann"),
                            DiagnosticLogPolicy.RoundNoteKey(family, fight, unit, round, option));
        }

        [Fact]
        public void RepeatedComputedBonusIsQuietButChangedBonusRemainsVisible()
        {
            var gate = new DiagnosticChangeLog<int>();
            Assert.True(gate.Observe(1, "caster", 4));
            for (int i = 0; i < 261; i++) Assert.False(gate.Observe(1, "caster", 4));
            Assert.True(gate.Observe(1, "caster", 6));
            Assert.True(gate.Observe(1, "caster", 0));
            Assert.True(gate.Observe(1, "caster", 4));
        }

        [Fact]
        public void RecoveryAllowsTheSameBlockedReasonToBeReportedAgain()
        {
            var gate = new DiagnosticChangeLog<string>();
            Assert.True(gate.Observe(1, "unit", "Prone"));
            Assert.False(gate.Observe(1, "unit", "Prone"));
            Assert.True(gate.Observe(1, "unit", "getting up"));
            Assert.True(gate.Observe(1, "unit", null));
            Assert.False(gate.Observe(1, "unit", null));
            Assert.True(gate.Observe(1, "unit", "Prone"));
        }

        [Fact]
        public void ActorsAndNewFightsHaveIndependentFirstObservations()
        {
            var gate = new DiagnosticChangeLog<bool>();
            Assert.True(gate.Observe(1, "one", false)); Assert.True(gate.Observe(1, "two", false));
            Assert.False(gate.Observe(1, "one", false)); Assert.True(gate.Observe(2, "one", false));
            Assert.Equal(1, gate.Count);
        }

        [Fact]
        public void CapacityPreservesOldEntriesInsteadOfStartingARepeatBurst()
        {
            var gate = new DiagnosticChangeLog<int>(2);
            Assert.True(gate.Observe(1, "one", 4)); Assert.True(gate.Observe(1, "two", 4));
            for (int i = 0; i < 100; i++) Assert.False(gate.Observe(1, "other" + i, 4));
            Assert.Equal(2, gate.Count); Assert.False(gate.Observe(1, "one", 4));
            Assert.True(gate.Observe(1, "one", 6)); Assert.True(gate.Observe(2, "other", 4));
        }

        [Fact]
        public void InvalidIdentitiesDoNotConsumeDiagnosticCapacity()
        {
            var gate = new DiagnosticChangeLog<int>(1);
            Assert.False(gate.Observe(1, null, 4)); Assert.False(gate.Observe(1, "", 4));
            Assert.Equal(0, gate.Count); Assert.True(gate.Observe(1, "valid", 4));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidCapacityIsRejected(int capacity) =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new DiagnosticChangeLog<int>(capacity));

        [Theory]
        [InlineData(false, false, false, false, true)]
        [InlineData(false, false, true, false, true)]
        [InlineData(true, true, false, false, true)]
        [InlineData(true, true, true, false, true)]
        [InlineData(true, false, false, false, false)]
        [InlineData(true, false, false, true, false)]
        [InlineData(true, false, true, false, false)]
        [InlineData(true, false, true, true, true)]
        public void QuietWaitsSkipFormattingWithoutLosingActionsOrTbTurns(bool rt, bool chosen, bool options,
                                                                          bool verbose, bool expected)
        {
            Assert.Equal(expected, DiagnosticLogPolicy.WriteDecision(rt, chosen, options, verbose));
        }
    }
}
