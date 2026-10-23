using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.20 (audit LIF-001): casts count once the game performed them (Tactics/CastLedger.cs).</summary>
    public class CastLedgerTests
    {
        [Theory]
        [InlineData(true, true, true)]     // started and acted: cast
        [InlineData(false, true, false)]   // turn-based force-finish before it started ("Success", acted, never started)
        [InlineData(true, false, false)]   // interrupted after starting, before acting
        [InlineData(false, false, false)]  // interrupted before starting (new order, replan, combat start)
        public void Performed_NeedsStartAndAct(bool started, bool acted, bool performed) =>
            Assert.Equal(performed, CastLedger.Performed(started, acted));

        [Fact]
        public void ConfirmedCast_Counts()
        {
            var ledger = new CastLedger();
            ledger.Record("orvath", "grease", "goblin", 2);
            ledger.Confirm("orvath");
            Assert.Equal(2, ledger.LastRound("orvath", "grease", "goblin"));
            Assert.Equal(1, ledger.TimesUsed("orvath", "grease"));
            Assert.False(ledger.IsOpen("orvath"));
        }

        [Fact]
        public void TakenBack_FirstCast_LeavesNothing()
        {
            var ledger = new CastLedger();
            ledger.Record("camellia", "enchant", "camellia", 1);
            Assert.True(ledger.TakeBack("camellia"));
            Assert.Null(ledger.LastRound("camellia", "enchant", "camellia"));
            Assert.Equal(0, ledger.TimesUsed("camellia", "enchant"));
            Assert.Empty(ledger.Targets("camellia", "enchant"));
        }

        [Fact]
        public void TakenBack_RepeatCast_RestoresTheEarlierOne()
        {
            var ledger = new CastLedger();
            ledger.Record("seelah", "demoralize", "cultist", 1);
            ledger.Confirm("seelah");
            ledger.Record("seelah", "demoralize", "cultist", 3);
            Assert.Equal(2, ledger.TimesUsed("seelah", "demoralize"));
            ledger.TakeBack("seelah");
            Assert.Equal(1, ledger.LastRound("seelah", "demoralize", "cultist"));
            Assert.Equal(1, ledger.TimesUsed("seelah", "demoralize"));
        }

        [Fact]
        public void TakeBack_OnlyTheOpenRecord()
        {
            var ledger = new CastLedger();
            ledger.Record("lann", "holy-water", "dretch", 1);
            ledger.Confirm("lann");
            Assert.False(ledger.TakeBack("lann"));                    // nothing open: a confirmed cast stays
            Assert.Equal(1, ledger.TimesUsed("lann", "holy-water"));
        }

        [Fact]
        public void NewRecord_KeepsAnEarlierOpenOne()
        {
            var ledger = new CastLedger();
            ledger.Record("orvath", "sleep", "wolf", 1);
            ledger.Record("orvath", "color-spray", "goblin", 1);     // the first one was never settled: it stands
            ledger.TakeBack("orvath");                               // takes back only the second
            Assert.Equal(1, ledger.LastRound("orvath", "sleep", "wolf"));
            Assert.Null(ledger.LastRound("orvath", "color-spray", "goblin"));
        }

        [Fact]
        public void NoTarget_IsAKeyOfItsOwn()
        {
            var ledger = new CastLedger();
            ledger.Record("orvath", "summon", null, 2);
            ledger.Confirm("orvath");
            Assert.Equal(2, ledger.LastRound("orvath", "summon", null));
            Assert.Single(ledger.Targets("orvath", "summon"));
            Assert.Equal("-", ledger.Targets("orvath", "summon")[0].Key);
        }

        [Fact]
        public void Casters_AreSeparate()
        {
            var ledger = new CastLedger();
            ledger.Record("a", "x", "t", 1);
            ledger.Record("b", "x", "t", 1);
            ledger.TakeBack("a");
            Assert.Null(ledger.LastRound("a", "x", "t"));
            Assert.Equal(1, ledger.LastRound("b", "x", "t"));
        }
    }
}
