using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ shifter-forms (docs/SHIFTER_FORM_DECISIONS.md, docs/TACTICAL_AI.md §174)
    public class ShifterFormDecisionTests
    {
        [Theory]
        [InlineData(4.4f, true, 3.4f)]   // fusion2g1b TB R1: the shift takes the standard action, attacks from next round
        [InlineData(6f, true, 5f)]
        [InlineData(1f, true, 0f)]       // last round: no form attack left to gain
        [InlineData(6f, false, 6f)]      // a swift or free shift keeps this round's attack
        [InlineData(0.5f, true, 0f)]     // never negative
        public void FormAttacksBeginAfterAShiftThatTakesTheTurnsAttack(float rounds, bool takesAttack, float expected)
            => Assert.Equal(expected, ValueModel.FormAttackRounds(rounds, takesAttack), 3);

        [Fact]
        public void FuryWaitsWhenTheFormClearlyBeatsTheAttackAndTheFurysRound()
            => Assert.True(ValueModel.HoldFuryForForm(formValue: 40f, bestImmediate: 20f, furyRoundGain: 8f));   // 40 >= 28 x 1.15

        [Fact]
        public void FuryIsTakenWhenTheFormOnlyBeatsTheAttackAlone()
            // fusion2g1b fight 3 R1: Griffon Shape 22.8 lost to the full attack (28.4); the Fury's round decides as well.
            => Assert.False(ValueModel.HoldFuryForForm(formValue: 30f, bestImmediate: 24f, furyRoundGain: 8f));

        [Fact]
        public void FuryIsTakenWhenTheFormDoesNotPay()
        {
            Assert.False(ValueModel.HoldFuryForForm(formValue: 0f, bestImmediate: 0f, furyRoundGain: 5f));
            Assert.False(ValueModel.HoldFuryForForm(formValue: -3f, bestImmediate: 10f, furyRoundGain: 0f));
        }

        [Fact]
        public void FormWithNoImmediateActionStillHasToBeatTheFurysRound()
        {
            Assert.True(ValueModel.HoldFuryForForm(formValue: 12f, bestImmediate: 0f, furyRoundGain: 10f));
            Assert.False(ValueModel.HoldFuryForForm(formValue: 11f, bestImmediate: 0f, furyRoundGain: 10f));
        }

        [Theory]
        [InlineData(34.9f, 0f, true)]     // stopped paying (fusion2g1b RT1 after Griffon Aspect)
        [InlineData(-6.9f, 23.3f, true)]  // started paying
        [InlineData(23.3f, 20.2f, false)] // small move: no new row
        [InlineData(23.3f, 17f, true)]    // more than a quarter
        [InlineData(4f, 6.5f, false)]     // under 3 points
        [InlineData(0.2f, 0.4f, false)]   // both not paying
        public void FormAppraisalIsLoggedAgainOnlyWhenItTurnsOrMovesMaterially(float logged, float now, bool expected)
            => Assert.Equal(expected, ShifterRules.FormNoteChanged(logged, now));

        [Fact]
        public void FullNoteLedgerKeepsItsExistingRepeatLimit()
        {
            var notes = new ShifterRules.FormNoteLog(1);
            Assert.True(notes.Observe(1, "form", 40, out bool first, out _)); Assert.True(first);
            for (int i = 0; i < ShifterRules.MaxFormNoteRepeats; i++)
                Assert.True(notes.Observe(1, "form", i % 2 == 0 ? 0 : 40, out _, out _));
            for (int i = 0; i < 100; i++)
                Assert.False(notes.Observe(1, "other-" + i, 40, out _, out _));
            Assert.False(notes.Observe(1, "form", 0, out _, out _));
        }

        [Fact]
        public void NewFightRestoresNoteCapacityAndFirstObservation()
        {
            var notes = new ShifterRules.FormNoteLog(1);
            Assert.True(notes.Observe(1, "old-form", 40, out _, out _));
            Assert.True(notes.Observe(2, "new-form", 40, out bool first, out _)); Assert.True(first);
            Assert.True(notes.Observe(2, "new-form", 0, out first, out float previous));
            Assert.False(first); Assert.Equal(40, previous);
        }

        [Fact]
        public void SmallChangesAccumulateAgainstTheLastLoggedValue()
        {
            var notes = new ShifterRules.FormNoteLog();
            Assert.True(notes.Observe(1, "form", 40, out _, out _));
            Assert.False(notes.Observe(1, "form", 35, out _, out _));
            Assert.True(notes.Observe(1, "form", 29, out _, out float previous)); Assert.Equal(40, previous);
        }
    }
}
