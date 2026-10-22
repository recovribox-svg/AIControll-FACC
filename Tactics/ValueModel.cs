// ★ v0.3.18: the engine's shared value formulas in one place (docs/AI_TUNING_AUDIT.md C4, B2, B4b, B5, N5), with no
// game types so they are unit-tested (tests/CompanionAI.Tests/ValueModelTests.cs).
//
// Values are damage-equivalent points, the scale every option of TacticalEngine is compared on.
//   - A combatant's worth per round (4 + 3 x level, or CR for enemies) is the scale's convention for "what removing or
//     keeping a unit is worth": it stays as it is, so options keep their relative order.
//   - Absolute game quantities are calibrated from the fight itself: how many rounds the fight still lasts (the party's
//     damage per round, B2) and how much damage the enemies really deal per round (the drop-chance model, N5). Both
//     start from a prior and move towards what has been observed in this fight.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class ValueModel
    {
        #region Worth of a combatant

        /// <summary>What a combatant of this level (character level, or CR for an enemy) is worth per round.</summary>
        public static float PerRound(float level) => 4f + 3f * Math.Max(1f, level);

        /// <summary>A summon is temporary and replaceable: worth half (v0.3.11).</summary>
        public const float SummonFactor = 0.5f;

        /// <summary>Rounds an effect or a unit's output is counted for: the expected rounds, at least 1, at most <paramref name="cap"/>.</summary>
        public static float Horizon(float expectedRounds, float cap) => Math.Min(Math.Max(1f, expectedRounds), cap);

        /// <summary>Keeping an ally in the fight: its worth per round for up to 3 rounds; half for a summon.</summary>
        public static float AllyWorth(int level, float expectedRounds, bool summon) =>
            PerRound(level) * Horizon(expectedRounds, 3f) * (summon ? SummonFactor : 1f);

        /// <summary>
        /// ★ v0.3.19 (test7): bringing a downed ally back: its worth per round for the rest of the fight (up to 6 rounds),
        /// not 3 - a fallen companion stays out until someone heals it. At 3 rounds an attack outscored Camellia's revive
        /// of Seelah every round from RT2 to RT9 (0.3.19-test6 real-time log, fight 2).
        /// </summary>
        public static float ReviveWorth(int level, float expectedRounds, bool summon) =>
            PerRound(level) * Horizon(expectedRounds, 6f) * (summon ? SummonFactor : 1f);

        /// <summary>Per-round base of a buff (and an enchantment) of this spell level.</summary>
        public static float BuffPerRound(int spellLevel) => 1f + 1.5f * Math.Max(0, spellLevel);

        #endregion

        #region Fight length (B2)

        /// <summary>
        /// Damage per round the party is assumed to deal before anything is observed, per character level. The 28
        /// turn-based fights of the 0.3.17 test logs: 1.0 / 1.4 / 1.8 per level (quartiles); the old assumption was 4,
        /// and fights lasted about twice the expected rounds.
        /// </summary>
        public const float PartyDamagePerLevel = 1.5f;

        /// <summary>Weight of a prior, in rounds of observation: after two rounds, what was seen counts as much.</summary>
        public const float PriorRounds = 2f;

        /// <summary>
        /// The party's damage per round: the prior from its levels, blended with the damage it dealt in the completed
        /// rounds of this fight (<paramref name="observedDamage"/> over <paramref name="roundsObserved"/>).
        /// </summary>
        public static float PartyDamagePerRound(float partyLevels, float observedDamage, float roundsObserved)
        {
            float prior = Math.Max(4f, PartyDamagePerLevel * Math.Max(1f, partyLevels));
            if (roundsObserved <= 0f) return prior;
            float blended = (prior * PriorRounds + Math.Max(0f, observedDamage)) / (PriorRounds + roundsObserved);
            return Math.Max(1f, blended);
        }

        /// <summary>Rounds the fight is expected to last from now: 1 + the enemies' HP over the party's damage per round, 1..6.</summary>
        public static float ExpectedRounds(float enemyHp, float partyDamagePerRound) =>
            Clamp(1f + Math.Max(0f, enemyHp) / Math.Max(1f, partyDamagePerRound), 1f, 6f);

        #endregion

        #region Enemy damage (N5)

        /// <summary>Lowest and highest scale of the predicted enemy damage.</summary>
        public const float MinDangerScale = 0.4f, MaxDangerScale = 2f;

        /// <summary>
        /// How much of the predicted damage the enemies really deal: the observed damage to the party over the damage the
        /// engine predicted for the same rounds (the sum of each ally's incoming damage at the start of each round), with
        /// <see cref="PriorRounds"/> rounds of "as predicted" as the prior, 0.4..2. Below 1 the drop-chance model (heals,
        /// withdraw, exposure) relaxes; above 1 (late-game enemies hit harder than CR suggests) it is more careful.
        /// <paramref name="roundsPredicted"/>: rounds that had a prediction above 0.
        /// </summary>
        public static float DangerScale(float observed, float predicted, int roundsPredicted)
        {
            if (roundsPredicted <= 0 || predicted <= 0f) return 1f;
            float perRound = predicted / roundsPredicted;
            float scale = (Math.Max(0f, observed) + PriorRounds * perRound) / (predicted + PriorRounds * perRound);
            return Clamp(scale, MinDangerScale, MaxDangerScale);
        }

        #endregion

        #region Damage and kills (B5)

        /// <summary>
        /// Chance that damage which lands with <paramref name="landChance"/> and then averages <paramref name="damageIfLands"/>
        /// takes <paramref name="hp"/> HP (the damage spread of RiskMath.ChanceAtLeast).
        /// </summary>
        public static float KillChance(float landChance, float damageIfLands, float hp) =>
            Clamp(landChance, 0f, 1f) * RiskMath.ChanceAtLeast(damageIfLands, Math.Max(1f, hp));

        /// <summary>
        /// Damage dealt to an enemy, capped at its HP, plus the value of taking it out of the fight: its whole worth
        /// (<paramref name="threat"/>) times the chance to kill it, and a quarter of it times the share of its HP otherwise
        /// (progress towards a kill). 0.3.17 and earlier gave the whole threat as soon as the expected damage reached the
        /// HP and a quarter of the share below: a fourfold jump at one point, whatever the chance to kill.
        /// </summary>
        public static float DamageValue(float expectedDamage, float hp, float threat, float killChance)
        {
            if (expectedDamage <= 0f) return 0f;
            hp = Math.Max(1f, hp);
            float kill = Clamp(killChance, 0f, 1f);
            return Math.Min(expectedDamage, hp) + threat * (kill + 0.25f * (1f - kill) * Math.Min(1f, expectedDamage / hp));
        }

        #endregion

        #region Healing (B4b)

        /// <summary>
        /// Worth of each HP a heal restores to a conscious ally at <paramref name="hpShare"/> of its HP, with the heal
        /// threshold <paramref name="threshold"/>. One continuous curve: 0 at the threshold, rising over its last 15 %,
        /// 0.6 + 2.4 x depth below it (depth 0 at the threshold, 1 at 0 HP), at most 2.4. The danger of dropping is
        /// valued apart (the drop-chance term); 0.3.17 also multiplied this part by 1.25 when the ally was engaged and
        /// by 1 / 2 / 3 by HP band, a jump at 50 % and 25 % and from 0 to 1.2 at the threshold.
        /// </summary>
        public static float HealPerHp(float hpShare, float threshold)
        {
            if (threshold <= 0f || hpShare >= threshold) return 0f;
            float depth = Clamp((threshold - hpShare) / threshold, 0f, 1f);
            float weight = Math.Min(2.4f, 0.6f + 2.4f * depth);
            return weight * Math.Min(1f, depth / 0.15f);
        }

        #endregion

        #region Knockdown areas (0.3.19-test9)

        /// <summary>
        /// Turn-based cost of being caught in an area that only knocks down (Grease), in a round's worth
        /// <paramref name="perRound"/>: a unit it knocks down at the start of its own turn loses that whole turn (the game
        /// ends it at once), so standing inside, the next turn is lost at <paramref name="affect"/> (the chance it falls
        /// this round); passing through, only when it falls on the way (and so lies inside) and then again. A fall whose
        /// turn is not lost costs standing up: a quarter of a round, plus the attacks of opportunity that provokes
        /// (<paramref name="standUpProvoke"/>), at the chance of a fall.
        /// </summary>
        public static float KnockdownAreaCost(float perRound, float affect, bool endsInside, float standUpProvoke)
        {
            float a = Clamp(affect, 0f, 1f);
            float lostTurn = endsInside ? a : a * a;
            return lostTurn * perRound + a * (1f - a) * 0.25f * perRound + a * Math.Max(0f, standUpProvoke);
        }

        #endregion

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
