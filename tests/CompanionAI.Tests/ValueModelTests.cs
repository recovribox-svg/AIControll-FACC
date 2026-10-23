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

        // --- 0.3.19-test11: buffs by what they do ---

        [Fact]
        public void AttackBonus_WorthMoreWhenHitsAreHard()
        {
            // +1 for a fighter dealing 10 a round: at 30 % it adds a sixth, at 90 % only a little (the natural 20 limit).
            float hard = ValueModel.AttackBonusGain(10f, 0.30f, 1f);
            float easy = ValueModel.AttackBonusGain(10f, 0.90f, 1f);
            Assert.Equal(10f * 0.05f / 0.30f, hard, 3);
            Assert.True(easy < hard * 0.4f);
            Assert.Equal(0f, ValueModel.AttackBonusGain(10f, 0.95f, 2f), 3);
            // A bigger bonus is never worth less; a penalty is a loss.
            Assert.True(ValueModel.AttackBonusGain(10f, 0.5f, 4f) > ValueModel.AttackBonusGain(10f, 0.5f, 1f));
            Assert.True(ValueModel.AttackBonusGain(10f, 0.5f, -1f) < 0f);
            // No attacks, nothing.
            Assert.Equal(0f, ValueModel.AttackBonusGain(0f, 0.5f, 2f), 3);
        }

        [Fact]
        public void ArmorBonus_KeepsDamageOffInProportion()
        {
            // +2 AC against attacks that hit at 50 %: a fifth of 10 a round.
            Assert.Equal(2f, ValueModel.ArmorGain(10f, 0.5f, 2f), 3);
            Assert.Equal(0f, ValueModel.ArmorGain(0f, 0.5f, 2f), 3);          // nothing attacks it
            Assert.True(ValueModel.ArmorGain(10f, 0.5f, -2f) < 0f);           // Rage's -2 AC
            Assert.True(ValueModel.ArmorGain(10f, 0.05f, 4f) <= 0.01f);       // they only hit on a 20 anyway
        }

        [Fact]
        public void TempHp_CountsOnlyWhatCanBeTaken()
        {
            // Light of the Angels: 1 temporary HP is worth 1 HP at most, however much comes in.
            Assert.Equal(1f, ValueModel.TempHpAbsorbed(1f, 20f, 3f), 3);
            Assert.Equal(6f, ValueModel.TempHpAbsorbed(30f, 2f, 3f), 3);       // more than will ever be taken
            Assert.Equal(0f, ValueModel.TempHpAbsorbed(10f, 0f, 3f), 3);
        }

        [Fact]
        public void MirrorImages_AtMostOneHitEach()
        {
            float some = ValueModel.MirrorImagesAbsorbed(4f, 2f, 6f, 3f);
            Assert.InRange(some, 0f, 4f * 6f);
            Assert.Equal(0f, ValueModel.MirrorImagesAbsorbed(4f, 0f, 6f, 3f), 3);   // nobody attacks it
            Assert.True(ValueModel.MirrorImagesAbsorbed(4f, 3f, 6f, 3f) >= ValueModel.MirrorImagesAbsorbed(2f, 3f, 6f, 3f));
        }

        [Fact]
        public void Prevention_ValuesHpAndTheChanceToDrop()
        {
            float frontliner = ValueModel.PreventionValue(3f, 4f, 20f, 15f, 30f);
            float safe = ValueModel.PreventionValue(3f, 4f, 60f, 4f, 30f);
            Assert.True(frontliner > safe);                       // the one about to drop gains more
            Assert.Equal(0f, ValueModel.PreventionValue(0f, 4f, 20f, 15f, 30f), 3);
            Assert.True(ValueModel.PreventionValue(-2f, 4f, 20f, 15f, 30f) < 0f);   // a penalty is a cost
        }

        [Fact]
        public void UsefulRounds_RealDurationWithinTheFight()
        {
            Assert.Equal(1f, ValueModel.UsefulRounds(1f, 5f, 3f), 3);    // one round
            Assert.Equal(4f, ValueModel.UsefulRounds(10f, 4f, 3f), 3);   // a minute, a 4-round fight
            Assert.Equal(3f, ValueModel.UsefulRounds(-1f, 5f, 3f), 3);   // unknown duration
            Assert.Equal(6f, ValueModel.UsefulRounds(600f, 9f, 3f), 3);  // capped
        }

        [Fact]
        public void Restraint_BuffMustClearlyBeatTheImmediateAction()
        {
            Assert.False(ValueModel.BuffBeatsImmediate(10f, 9.5f));   // near tie: attack
            Assert.False(ValueModel.BuffBeatsImmediate(11f, 10f));    // 10 % better: still the attack
            Assert.True(ValueModel.BuffBeatsImmediate(12f, 10f));     // 20 % better: the buff
            Assert.True(ValueModel.BuffBeatsImmediate(3f, 0f));       // nothing else to do
        }

        [Fact]
        public void Scenario_BlessOnAPartyBeatsOneAttackEarly_NotLate()
        {
            // Bless (+1 attack, morale) on four allies dealing 10 a round at 55 %: early (5 rounds) against a 9-point
            // attack it wins clearly; with one round left it does not.
            float perAlly = ValueModel.AttackBonusGain(10f, 0.55f, 1f);
            float early = 4f * perAlly * ValueModel.UsefulRounds(10f, 5f, 3f);
            float late = 4f * perAlly * ValueModel.UsefulRounds(10f, 1f, 3f);
            Assert.True(ValueModel.BuffBeatsImmediate(early, 9f));
            Assert.False(ValueModel.BuffBeatsImmediate(late, 9f));
        }

        [Fact]
        public void Scenario_GuidanceOnOneRollIsNotAnAction()
        {
            // Guidance: +1 on one attack (a 55 % hit for 6): about a fifth of a point - any attack beats it.
            float guidance = ValueModel.AttackBonusGain(0.55f * 6f, 0.55f, 1f);
            Assert.InRange(guidance, 0f, 0.5f);
            Assert.False(ValueModel.BuffBeatsImmediate(guidance, 1.2f));
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
