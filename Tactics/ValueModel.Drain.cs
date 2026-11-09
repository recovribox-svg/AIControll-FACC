// ★ v0.3.28 test 13 (author's request: ability-drain spells and enemies recognised as the threat they are): ability damage
// and negative levels in the hit points they are worth, by the game's own rules.
//   - Ability damage (ContextActionDealDamage type AbilityDamage, RuleDealStatDamage): a score taken below 1 marks the creature
//     for death (UnitState.MarkedForDeath) - in this game a drained score is a second life bar, not only a penalty. Worth the
//     share of the score it takes of the hit points the target has left; the whole score is a kill.
//   - Negative levels (type EnergyDrain, RuleDrainEnergy, UnitPartNegativeLevels, NegativeLevelComponent): each takes 5 hit
//     points and 1 from attacks, saves, skills, CMD and caster level; as many as the creature's character level kill it
//     (UnitLifeController.ForceTickOnUnit). Worth the 5 hit points each, then the share of the way to that level of what is
//     left.
// Until test 12 both were valued as hit-point damage of their dice: Enervation's 1d4 levels as 2.5 damage, a Shadow's 1d6
// Strength as nothing in its threat.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>Hit points one negative level takes (NegativeLevelComponent: HitPoints -5 per level).</summary>
        public const float HitPointsPerNegativeLevel = 5f;

        /// <summary>UnitLifeController compares accumulated UnitPartNegativeLevels.Count with character level.
        /// The remaining part of that limit is the denominator for the next drain, not the original full level.</summary>
        public static int LevelsBeforeDrainDeath(int characterLevel, int alreadyDrained) =>
            Math.Max(0, Math.Max(1, characterLevel) - Math.Max(0, alreadyDrained));

        /// <summary>Read non-HP harm is a lower bound on the value of removing its source. Keep the existing disabled
        /// discount; no extra class/tag multiplier and no change to the calibrated incoming-HP threat.</summary>
        public static float NonHitPointRemovalWorth(float ordinaryWorth, float nonHitPointHarm, bool disabled) =>
            Math.Max(Math.Max(0f, ordinaryWorth), Math.Max(0f, nonHitPointHarm) * (disabled ? 0.25f : 1f));

        /// <summary>
        /// The hit points <paramref name="points"/> of ability damage to a score of <paramref name="score"/> are worth on a
        /// target with <paramref name="hpLeft"/> left: the share of the score (<see cref="AbilityDamageShare"/>) of those hit
        /// points, all of them when the score is taken (a kill). 0 for a score the target does not have.
        /// </summary>
        public static float AbilityDamageEquivalent(float points, int score, float hpLeft)
        {
            if (!(hpLeft > 0f)) return 0f;
            return AbilityDamageShare(points, score) * hpLeft;
        }

        /// <summary>
        /// The hit points <paramref name="levels"/> negative levels are worth on a target of character level
        /// <paramref name="characterLevel"/> with <paramref name="hpLeft"/> left: 5 each, then the share of the way to its
        /// character level of the rest; all of them when the levels reach it (a kill).
        /// </summary>
        public static float EnergyDrainEquivalent(float levels, int characterLevel, float hpLeft)
        {
            if (!(levels > 0f) || !(hpLeft > 0f)) return 0f;
            if (characterLevel <= 0 || levels >= characterLevel) return hpLeft;
            float lost = Math.Min(hpLeft, HitPointsPerNegativeLevel * levels);
            return Math.Min(hpLeft, lost + levels / characterLevel * (hpLeft - lost));
        }

        /// <summary>
        /// ★ v0.3.28 test 14: the chance a saving throw of <paramref name="save"/> fails against <paramref name="dc"/> (a d20
        /// that must reach the DC; a 1 always fails, a 20 always passes).
        /// </summary>
        public static float SaveFailChance(int dc, int save) => Clamp((dc - save - 1) / 20f, 0.05f, 0.95f);

        /// <summary>
        /// ★ v0.3.28 test 14: what an affliction (a weapon hit's poison, a swarm's round) is worth in hit points on a victim
        /// with <paramref name="hpLeft"/> left: its hit-point damage plus its ability damage and negative levels in hit points
        /// (<paramref name="drainHp"/>, already read per score), at most what is left, plus the rounds it controls the victim
        /// (<paramref name="controlRounds"/>, hard control 1 a round, soft 0.4) at the victim's worth a round - the measure
        /// the threat model gives a controlling spell (EnemyThreat.BestAbility).
        /// </summary>
        public static float AfflictionWorth(float hitPoints, float drainHp, float controlRounds, float hpLeft, float victimPerRound)
        {
            float harm = Math.Min(Math.Max(0f, hpLeft), Math.Max(0f, hitPoints) + Math.Max(0f, drainHp));
            return harm + Math.Max(0f, controlRounds) * Math.Max(0f, victimPerRound);
        }

        /// <summary>
        /// ★ v0.3.29 test 1: the rounds of control <paramref name="applications"/> applications in one round are expected to
        /// cost a victim, when one controls it with chance <paramref name="chance"/> for <paramref name="control"/> /
        /// <paramref name="chance"/> rounds: control does not add up (a second paralysis in the same round costs no more
        /// turns), so it is that duration times the chance at least one lands, 1 - (1 - chance)^applications. Up to one
        /// application, or with no chance known, it scales as it is.
        /// </summary>
        public static float RepeatedControl(float control, float chance, float applications)
        {
            if (!(control > 0f) || !(applications > 0f) || float.IsInfinity(control)) return 0f;
            if (applications <= 1f || !(chance > 0.001f)) return control * applications;
            float duration = control / chance;
            float landed = 1f - (float)Math.Pow(1f - Math.Min(1f, chance), applications);
            return Math.Min(control * applications, duration * landed);
        }

        /// <summary>
        /// ★ v0.3.28 test 14: the share of a victim an affliction takes for the fight's worth model (the walk and revive
        /// prices: share x its worth): the ability shares (<paramref name="abilityShare"/>), negative levels over its
        /// character level, and the control rounds over the 3 rounds the worth counts; at most all of it.
        /// </summary>
        public static float AfflictionShare(float abilityShare, float negativeLevels, int characterLevel, float controlRounds)
        {
            float share = Math.Max(0f, abilityShare) + Math.Max(0f, controlRounds) / 3f;
            if (negativeLevels > 0f) share += characterLevel <= 0 ? 1f : negativeLevels / characterLevel;
            return Math.Min(1f, share);
        }
    }
}
