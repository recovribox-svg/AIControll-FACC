using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    // ★ sneak attack refinements: fortification and the flanking a second attacker creates (docs/BURST_AIM_AND_TACTICS_PLAN.md §6).
    public class SneakRulesTests
    {
        [Theory]
        [InlineData(0, 1f)]
        [InlineData(25, 0.75f)]     // light fortification
        [InlineData(75, 0.25f)]     // heavy fortification
        [InlineData(100, 0f)]
        [InlineData(150, 0f)]       // clamped
        [InlineData(-10, 1f)]
        public void FortificationKeepsTheRestOfTheRoll(int chance, float keep)
        {
            Assert.Equal(keep, SneakRules.FortificationKeep(chance), 4);
        }

        [Fact]
        public void FortificationTakesSneakAndCriticalButNotTheHit()
        {
            // 60 % to hit for 10, 7 sneak per hit, 2 critical extra in expectation
            Assert.Equal(0.6f * 10f + 0.6f * 7f + 2f, SneakRules.LineDamage(0.6f, 10f, 7f, 2f, 1f), 4);
            Assert.Equal(0.6f * 10f + 0.25f * (0.6f * 7f + 2f), SneakRules.LineDamage(0.6f, 10f, 7f, 2f, 0.25f), 4);
            Assert.Equal(0.6f * 10f, SneakRules.LineDamage(0.6f, 10f, 7f, 2f, 0f), 4);
        }

        [Theory]
        // native flanked, cannot be flanked, attacker engages, other engagers -> flanked when the attack lands
        [InlineData(true, false, false, 0, true)]      // flanked already
        [InlineData(false, false, true, 1, true)]      // the second engager
        [InlineData(false, false, true, 2, true)]
        [InlineData(false, false, true, 0, false)]     // the only engager
        [InlineData(false, false, false, 1, false)]    // a ranged attack does not engage
        [InlineData(false, true, true, 3, false)]      // CannotBeFlanked
        [InlineData(true, true, false, 0, false)]
        public void FlankingCountsEngagersNotPositions(bool native, bool cannot, bool engages, int others, bool expected)
        {
            Assert.Equal(expected, SneakRules.PredictsFlank(native, cannot, engages, others));
        }

        [Theory]
        // engaged now, can engage, threat range, attack range, totally concealed now -> engages when the attack lands
        [InlineData(true, false, 0f, 3f, true, true)]          // the game's own engagement answers it
        [InlineData(false, true, 1.5f, 1.5f, false, true)]     // same hand: its reach covers the attack
        [InlineData(false, true, 3f, 1.5f, false, true)]       // longer threat reach
        [InlineData(false, true, 1.5f, 3f, false, false)]      // reach weapon beyond the threat hand: no engagement
        [InlineData(false, true, 1.5f, 1.5f, true, false)]     // invisible target, no See Invisibility
        [InlineData(false, false, 1.5f, 1.5f, false, false)]   // cannot act or no threat hand
        [InlineData(false, true, 1.495f, 1.5f, false, false)]  // genuinely shorter; native ranges do not gain an extra centimetre
        [InlineData(false, true, 1.499999f, 1.5f, false, false)]
        [InlineData(false, true, float.NaN, 1.5f, false, false)]
        public void EngagementProjectionChecksNativeStateAndRangeCompatibility(bool engagedNow, bool canEngage, float threat, float attack,
                                                         bool concealed, bool expected)
        {
            Assert.Equal(expected, SneakRules.WillEngage(engagedNow, canEngage, threat, attack, concealed));
        }

        [Fact]
        public void APartnerGainsTheFlankingBonusAndItsSneakDice()
        {
            // 50 % to hit, 60 % flanked (+2), 12 per hit, 10.5 new sneak (3d6), no fortification
            Assert.Equal(0.1f * 12f + 0.6f * 10.5f, SneakRules.PartnerLineGain(6f, 7.2f, 0.6f, 10.5f, 1f), 4);
            // heavy fortification keeps a quarter of the sneak dice, all of the extra hits
            Assert.Equal(0.1f * 12f + 0.6f * 10.5f * 0.25f, SneakRules.PartnerLineGain(6f, 7.2f, 0.6f, 10.5f, 0.25f), 4);
            // no sneak dice: only the bonus; already sneaking: newSneak 0
            Assert.Equal(0.1f * 12f, SneakRules.PartnerLineGain(6f, 7.2f, 0.6f, 0f, 1f), 4);
            // a ranged line (no bonus) gains only the sneak dice
            Assert.Equal(0.5f * 10.5f, SneakRules.PartnerLineGain(6f, 6f, 0.5f, 10.5f, 1f), 4);
        }

        [Fact]
        public void BadInputsGainNothingNegative()
        {
            Assert.Equal(0f, SneakRules.PartnerLineGain(6f, 5f, 0.5f, 0f, 1f), 4);     // never a loss
            Assert.Equal(0f, SneakRules.PartnerLineGain(5f, 5f, -3f, -2f, 1f), 4);
        }

        [Fact]
        public void PartnerExtraHitsIncludeExistingSneakAndCriticalsOnce()
        {
            // Already flat-footed: 10.5 sneak applies before the flank. Better AB still adds hits and confirmations.
            float before = SneakRules.LineDamage(0.5f, 12f, 10.5f, 2f, 0.25f);
            float after = SneakRules.LineDamage(0.6f, 12f, 10.5f, 3f, 0.25f);
            Assert.Equal(1.2f + 0.25f * (1.05f + 1f), SneakRules.PartnerLineGain(before, after, 0.6f, 0f, 0.25f), 4);
        }
    }
}
