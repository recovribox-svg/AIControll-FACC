using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.18: the engine's shared value formulas (Tactics/ValueModel.cs, docs/AI_TUNING_AUDIT.md C4/B2/B4b/B5/N5).</summary>
    public class ValueModelTests
    {
        // --- C4: unchanged formulas, pinned to the 0.3.17 values ---

        [Fact]
        public void WorthPerRound_IsUnchanged()
        {
            Assert.Equal(7f, ValueModel.PerRound(1f), 3);
            Assert.Equal(10f, ValueModel.PerRound(2f), 3);
            Assert.Equal(7f, ValueModel.PerRound(0f), 3);        // CR / level below 1 counts as 1
            Assert.Equal(64f, ValueModel.PerRound(20f), 3);
        }

        [Fact]
        public void AllyWorth_ThreeRoundsAtMost_HalfForSummons()
        {
            Assert.Equal(30f, ValueModel.AllyWorth(2, 5f, summon: false), 3);
            Assert.Equal(20f, ValueModel.AllyWorth(2, 2f, summon: false), 3);
            Assert.Equal(10f, ValueModel.AllyWorth(2, 0.5f, summon: false), 3);   // at least one round
            Assert.Equal(15f, ValueModel.AllyWorth(2, 5f, summon: true), 3);
        }

        [Fact]
        public void ReviveWorth_RestOfTheFight_SixRoundsAtMost()
        {
            Assert.Equal(50f, ValueModel.ReviveWorth(2, 5f, summon: false), 3);
            Assert.Equal(60f, ValueModel.ReviveWorth(2, 9f, summon: false), 3);    // capped at 6 rounds
            Assert.Equal(10f, ValueModel.ReviveWorth(2, 0.5f, summon: false), 3);  // at least one round
            Assert.Equal(25f, ValueModel.ReviveWorth(2, 5f, summon: true), 3);
            // never less than keeping the ally up for 3 rounds
            Assert.True(ValueModel.ReviveWorth(6, 4f, false) >= ValueModel.AllyWorth(6, 4f, false));
        }

        [Fact]
        public void BuffPerRound_IsUnchanged()
        {
            Assert.Equal(1f, ValueModel.BuffPerRound(0), 3);
            Assert.Equal(5.5f, ValueModel.BuffPerRound(3), 3);
        }

        // --- 0.3.19-test9: knockdown areas (Grease), turn-based ---

        [Fact]
        public void KnockdownArea_StandingInsideLosesTheNextTurnAtTheFallChance()
        {
            // A poor Reflex (falls 80 %): the next turn is lost at 80 %, plus standing up when it is not.
            Assert.Equal(0.8f * 10f + 0.8f * 0.2f * 0.25f * 10f, ValueModel.KnockdownAreaCost(10f, 0.8f, endsInside: true, 0f), 3);
            // Never falls: nothing.
            Assert.Equal(0f, ValueModel.KnockdownAreaCost(10f, 0f, endsInside: true, 5f), 3);
            // Always falls: the whole next turn and the stand-up's attacks of opportunity.
            Assert.Equal(10f + 3f, ValueModel.KnockdownAreaCost(10f, 1f, endsInside: true, 3f), 3);
        }

        [Fact]
        public void KnockdownArea_PassingThroughCostsLessThanStayingInside()
        {
            for (float a = 0.05f; a < 1f; a += 0.05f)
                Assert.True(ValueModel.KnockdownAreaCost(10f, a, false, 1f) <= ValueModel.KnockdownAreaCost(10f, a, true, 1f));
            // A nimble unit (falls 20 %) pays far less than a quarter of the paladin's.
            Assert.True(ValueModel.KnockdownAreaCost(10f, 0.2f, true, 0f) < 0.3f * ValueModel.KnockdownAreaCost(10f, 0.8f, true, 0f));
        }

        // --- B2: fight length ---

        [Fact]
        public void PartyDamage_PriorFromLevels()
        {
            Assert.Equal(15f, ValueModel.PartyDamagePerRound(10f, 0f, 0f), 3);   // five level-2 characters
            Assert.Equal(4f, ValueModel.PartyDamagePerRound(1f, 0f, 0f), 3);     // floor
        }

        [Fact]
        public void PartyDamage_MovesTowardsWhatWasSeen()
        {
            // Prior 15/round; the party dealt 60 in 2 rounds (30/round): half way after two rounds.
            Assert.Equal(22.5f, ValueModel.PartyDamagePerRound(10f, 60f, 2f), 3);
            // After 10 rounds of 8/round the prior hardly counts.
            float late = ValueModel.PartyDamagePerRound(10f, 80f, 10f);
            Assert.InRange(late, 9f, 10f);
            // Nothing dealt yet (walking up): lower, never 0.
            Assert.InRange(ValueModel.PartyDamagePerRound(10f, 0f, 2f), 1f, 8f);
        }

        [Fact]
        public void ExpectedRounds_Clamped()
        {
            Assert.Equal(1f, ValueModel.ExpectedRounds(0f, 15f), 3);
            Assert.Equal(5f, ValueModel.ExpectedRounds(60f, 15f), 3);
            Assert.Equal(6f, ValueModel.ExpectedRounds(500f, 15f), 3);
        }

        [Fact]
        public void ExpectedRounds_MatchTheMeasuredFights()
        {
            // 0.3.17-test2: 8 enemies, 124 HP, five level-2 characters, the fight lasted 13 rounds: 0.3.17 expected 4.1.
            float before = 1f + 124f / 40f;
            float now = ValueModel.ExpectedRounds(124f, ValueModel.PartyDamagePerRound(10f, 0f, 0f));
            Assert.True(now > before);
            Assert.Equal(6f, now, 3);
        }

        // --- N5: enemy damage ---

        [Fact]
        public void DangerScale_StartsAsPredicted()
        {
            Assert.Equal(1f, ValueModel.DangerScale(0f, 0f, 0), 3);
            Assert.Equal(1f, ValueModel.DangerScale(5f, 0f, 0), 3);
        }

        [Fact]
        public void DangerScale_FollowsWhatTheEnemiesDealt()
        {
            // Predicted 20 a round for 2 rounds, dealt 10: (10 + 40) / (40 + 40) = 0.625.
            Assert.Equal(0.625f, ValueModel.DangerScale(10f, 40f, 2), 3);
            // Enemies that hit far harder than predicted: more careful, at most twice.
            Assert.Equal(2f, ValueModel.DangerScale(400f, 40f, 2), 3);
            // Nothing dealt for many rounds: never below 0.4 (a big hitter may still be on its way).
            Assert.Equal(0.4f, ValueModel.DangerScale(0f, 200f, 10), 3);
        }

        // --- B5: kill chance ---

        [Fact]
        public void KillChance_FromTheDamageSpread()
        {
            Assert.Equal(0.5f, ValueModel.KillChance(1f, 10f, 10f), 3);   // average damage against equal HP
            Assert.Equal(0.25f, ValueModel.KillChance(0.5f, 10f, 10f), 3);
            Assert.Equal(1f, ValueModel.KillChance(1f, 20f, 10f), 3);
            Assert.Equal(0f, ValueModel.KillChance(1f, 5f, 10f), 3);
        }

        [Fact]
        public void DamageValue_NoJumpAtTheEnemysHp()
        {
            // Threat 10, HP 10. 0.3.17: 9.99 expected damage was worth 9.99 + 2.5, 10 was worth 20 (x1.6 for 0.01).
            float below = ValueModel.DamageValue(9.9f, 10f, 10f, ValueModel.KillChance(1f, 9.9f, 10f));
            float at = ValueModel.DamageValue(10f, 10f, 10f, ValueModel.KillChance(1f, 10f, 10f));
            Assert.InRange(at - below, 0f, 0.5f);
            // "Exactly enough" is a coin flip: about 0.6 of the removal.
            Assert.Equal(10f + 10f * 0.625f, at, 3);
            // Overkill gets it all.
            Assert.Equal(20f, ValueModel.DamageValue(25f, 10f, 10f, 1f), 3);
            // No kill chance: a quarter of the threat times the share, as before.
            Assert.Equal(5f + 10f * 0.25f * 0.5f, ValueModel.DamageValue(5f, 10f, 10f, 0f), 3);
            Assert.Equal(0f, ValueModel.DamageValue(0f, 10f, 10f, 1f), 3);
        }

        [Fact]
        public void DamageValue_RisesWithTheChanceToKill()
        {
            float prev = -1f;
            for (float e = 1f; e <= 30f; e += 0.5f)
            {
                float v = ValueModel.DamageValue(e, 10f, 10f, ValueModel.KillChance(1f, e, 10f));
                Assert.True(v >= prev, $"value fell at expected damage {e}");
                prev = v;
            }
        }

        // --- B4b: healing ---

        [Fact]
        public void HealPerHp_ZeroAtTheThreshold_NoCliff()
        {
            Assert.Equal(0f, ValueModel.HealPerHp(0.5f, 0.5f), 3);
            Assert.Equal(0f, ValueModel.HealPerHp(0.9f, 0.5f), 3);
            Assert.InRange(ValueModel.HealPerHp(0.49f, 0.5f), 0f, 0.15f);   // 0.3.17: 1.2 (x1.25 engaged)
        }

        [Fact]
        public void HealPerHp_RisesSmoothlyAsHpFalls()
        {
            float prev = 0f;
            for (float hp = 0.5f; hp >= 0f; hp -= 0.01f)
            {
                float w = ValueModel.HealPerHp(hp, 0.5f);
                Assert.True(w >= prev - 1e-4f, $"weight fell at {hp:F2}");
                Assert.True(w - prev < 0.3f, $"jump at {hp:F2}");
                prev = w;
            }
        }

        [Fact]
        public void HealPerHp_LowHpKeepsItsValue()
        {
            // 0.3.17, not engaged: x1.8 at 25 % HP and below (threshold 50 %).
            Assert.Equal(1.8f, ValueModel.HealPerHp(0.25f, 0.5f), 3);
            Assert.Equal(2.4f, ValueModel.HealPerHp(0.05f, 0.5f), 3);
            // Mildly hurt (45 %): far less than 0.3.17's 1.2.
            Assert.InRange(ValueModel.HealPerHp(0.45f, 0.5f), 0.5f, 0.6f);
        }
    }
}
