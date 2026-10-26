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
    public static partial class ValueModel
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

        #region Learned damage share (0.3.20)

        /// <summary>Pseudo-hits at "as estimated" a learned damage share starts from, and its range.</summary>
        public const float LearnedPriorHits = 3f, MinLearnedShare = 0.67f, MaxLearnedShare = 1.6f;

        /// <summary>
        /// ★ v0.3.20: what an ability's hits really dealt against what was estimated for them, from
        /// <see cref="LearnedPriorHits"/> pseudo-hits "as estimated", 0.67..1.6; 1 with fewer than two hits (one hit's roll is
        /// too noisy). Used for kinetic blasts (DamageObserver.BlastFactor).
        /// </summary>
        public static float LearnedShare(float dealt, float estimated, int hits)
        {
            if (hits < 2 || estimated <= 0f) return 1f;
            float perHit = estimated / hits;
            float share = (Math.Max(0f, dealt) + LearnedPriorHits * perHit) / (estimated + LearnedPriorHits * perHit);
            return Clamp(share, MinLearnedShare, MaxLearnedShare);
        }

        #endregion

        #region Enemy importance (0.3.20)

        /// <summary>Range of an enemy's importance against the fight's average (EnemyThreat).</summary>
        public const float MinThreatShare = 0.5f, MaxThreatShare = 2f;

        /// <summary>
        /// ★ v0.3.20 (test4): an enemy's share of the fight's importance. <paramref name="offence"/> is what it can do a round
        /// (EnemyThreat), <paramref name="worth"/> its CR worth (<see cref="PerRound"/>); the totals are over every enemy of
        /// the fight. 1 when it does as much for its CR as the average enemy; more for one that does more, 0.5..2. The fight's
        /// total stays what the CRs give (shares average 1), so only the order between enemies changes, not how fights
        /// weigh against heals or buffs - which the danger calibration (<see cref="DangerScale"/>) already tunes.
        /// </summary>
        public static float ThreatShare(float offence, float worth, float totalOffence, float totalWorth)
        {
            if (offence <= 0f && totalOffence <= 0f) return 1f;
            if (worth <= 0f || totalWorth <= 0f || totalOffence <= 0f) return 1f;
            float share = (Math.Max(0f, offence) / worth) / (totalOffence / totalWorth);
            return Clamp(share, MinThreatShare, MaxThreatShare);
        }

        /// <summary>
        /// ★ v0.3.20 (test4): an enemy's damage a round from what it dealt in this fight's completed rounds, with
        /// <see cref="PriorRounds"/> rounds of <paramref name="predicted"/> as the prior (one lucky critical does not make it the
        /// most dangerous; three rounds of hitting hard do).
        /// </summary>
        public static float ObservedOffence(float dealt, int rounds, float predicted) =>
            (Math.Max(0f, dealt) + PriorRounds * Math.Max(0f, predicted)) / (Math.Max(0, rounds) + PriorRounds);

        /// <summary>Most an enemy's removal gains from the ally it is about to drop.</summary>
        public const float MaxUrgency = 0.5f;

        /// <summary>
        /// ★ v0.3.20 (test4): what taking down (or holding) an enemy is worth more when the ally it is attacking may drop to it
        /// this round: 1 + half the drop chance, a quarter for a summon (replaceable).
        /// </summary>
        public static float Urgency(float dropChance, bool victimIsSummon) =>
            1f + MaxUrgency * Clamp(dropChance, 0f, 1f) * (victimIsSummon ? 0.5f : 1f);

        #endregion

        #region Enemy damage (N5)

        /// <summary>Lowest and highest scale of the predicted enemy damage.</summary>
        /// <remarks>★ v0.3.20 (0.3.19 release logs): the floor was 0.4. All nine fights of the turn-based and real-time
        /// logs ended at it: the enemies dealt 0-25 % of the prediction (3 of 226, 73 of 566, 96 of 389), the smoothed
        /// share 0.2-0.3, so danger stayed 1.5-2x what was seen - Mage Armor on a paladin in melee "takes 11.6 a round"
        /// in a fight where the whole party took 3.</remarks>
        public const float MinDangerScale = 0.25f, MaxDangerScale = 2f;

        /// <summary>
        /// How much of the predicted damage the enemies really deal: the observed damage to the party over the damage the
        /// engine predicted for the same rounds (the sum of each ally's incoming damage at the start of each round), with
        /// <see cref="PriorRounds"/> rounds of "as predicted" as the prior, 0.25..2. Below 1 the drop-chance model (heals,
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
        /// ★ v0.3.21 (gaps): a regenerating enemy (a troll) brought to 0 HP only falls unconscious and stands up again once it
        /// has healed (UnitLifeController, AddEffectRegeneration): a "kill" takes it out for a while, a share
        /// <see cref="RegeneratingKnockdown"/> of a real one - unless the damage stops the regeneration (fire, acid), or it is
        /// stopped already.
        /// </summary>
        public static float RegeneratingKill(float killChance, bool regenerating, bool endsRegeneration) =>
            regenerating && !endsRegeneration ? killChance * RegeneratingKnockdown : killChance;

        public const float RegeneratingKnockdown = 0.35f;

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

        #region Buffs by what they do (0.3.19-test11)

        /// <summary>
        /// Extra damage a round from <paramref name="bonus"/> on the attack roll, for a unit dealing
        /// <paramref name="damagePerRound"/> at <paramref name="hitChance"/>: each point is 5 % of the d20, relative to the
        /// hits it lands now, within the natural 1 / 20 limits. A penalty is a loss.
        /// </summary>
        public static float AttackBonusGain(float damagePerRound, float hitChance, float bonus)
        {
            if (damagePerRound <= 0f || hitChance <= 0f || bonus == 0f) return 0f;
            float h = Clamp(hitChance, 0.05f, 0.95f);
            float delta = bonus > 0f ? Math.Min(0.05f * bonus, 0.95f - h) : Math.Max(0.05f * bonus, 0.05f - h);
            return damagePerRound * delta / h;
        }

        /// <summary>Extra damage a round from <paramref name="bonus"/> damage on each of <paramref name="hitsPerRound"/>.</summary>
        public static float DamageBonusGain(float hitsPerRound, float bonus) => Math.Max(0f, hitsPerRound) * bonus;

        /// <summary>
        /// Damage a round kept off a unit by <paramref name="armorBonus"/> AC, against <paramref name="incoming"/> a round from
        /// attacks that hit it at <paramref name="enemyHitChance"/> (5 % of the d20 a point, within the natural limits). A
        /// penalty lets more through (negative).
        /// </summary>
        public static float ArmorGain(float incoming, float enemyHitChance, float armorBonus)
        {
            if (incoming <= 0f || armorBonus == 0f) return 0f;
            float h = Clamp(enemyHitChance, 0.05f, 0.95f);
            float delta = armorBonus > 0f ? Math.Min(0.05f * armorBonus, h - 0.05f) : -Math.Min(-0.05f * armorBonus, 0.95f - h);
            return incoming * delta / h;
        }

        /// <summary>
        /// Worth of damage kept off an ally: <paramref name="perRound"/> for <paramref name="rounds"/>, a third of a point per
        /// HP (HP someone would have to heal later, as a move's damage is priced) plus the lower chance to drop
        /// (RiskMath.RoundDropChance with the incoming damage less what is kept off) times the ally's worth. Negative for a
        /// penalty.
        /// </summary>
        public static float PreventionValue(float perRound, float rounds, float hp, float incoming, float allyWorth)
        {
            if (perRound == 0f || rounds <= 0f) return 0f;
            float before = RiskMath.RoundDropChance(hp, incoming);
            float after = RiskMath.RoundDropChance(hp, Math.Max(0f, incoming - perRound));
            float drop = Clamp((before - after) * rounds, -1f, 1f);
            return 0.3f * perRound * rounds + drop * allyWorth;
        }

        /// <summary>Damage temporary hit points can take before they run out or the window ends.</summary>
        public static float TempHpAbsorbed(float tempHp, float incomingPerRound, float rounds) =>
            Math.Max(0f, Math.Min(tempHp, Math.Max(0f, incomingPerRound) * Math.Max(0f, rounds)));

        /// <summary>
        /// Damage <paramref name="images"/> mirror images take instead of their owner: each attack picks one figure of
        /// images + 1, an image taken by a hit is gone; at most one hit per image.
        /// </summary>
        public static float MirrorImagesAbsorbed(float images, float hitsPerRound, float damagePerHit, float rounds)
        {
            if (images <= 0f || hitsPerRound <= 0f || damagePerHit <= 0f || rounds <= 0f) return 0f;
            float taken = Math.Min(images, hitsPerRound * rounds * images / (images + 1f));
            return taken * damagePerHit;
        }

        /// <summary>
        /// Rounds a buff is useful for: its real duration (unknown: <paramref name="unknownRounds"/>), at most the rounds the
        /// fight is expected to go on, at least one.
        /// </summary>
        public static float UsefulRounds(float durationRounds, float expectedRounds, float unknownRounds) =>
            Clamp(Math.Min(durationRounds > 0f ? durationRounds : unknownRounds, Math.Max(1f, expectedRounds)), 1f, 6f);

        /// <summary>
        /// The player's preference for restraint: a Standard or full-round action that only buffs is taken over the best
        /// immediate main action only when clearly better - by <see cref="BuffMargin"/> - otherwise the immediate action wins.
        /// </summary>
        public static bool BuffBeatsImmediate(float buffValue, float bestImmediate) =>
            bestImmediate <= 0f || buffValue >= bestImmediate * (1f + BuffMargin);

        public const float BuffMargin = 0.15f;

        #endregion

        #region Debuffs by their penalties (0.3.21)

        /// <summary>
        /// The share of an enemy's worth per round a debuff takes, from its penalties (positive numbers = how much is
        /// taken away): to-hit and damage on its attacks (about 5 % of its hits per point at a typical 60 % hit chance; a
        /// point of damage about 5 % of a hit), Armor Class (the party's attacks on it, 4 % a point), saves (only the
        /// party's spells against it, 2 % a point), and Strength / Dexterity through the modifiers they move. At most
        /// <see cref="DebuffCap"/>; 0 for a debuff that takes nothing away. The engine's flat "plain debuff" share (0.25)
        /// is for debuffs whose penalties cannot be read.
        /// </summary>
        public static float DebuffShare(float attack, float damage, float armor, float saves, float strength, float dexterity)
        {
            float a = Math.Max(0f, attack) + Math.Max(0f, strength) / 2f;
            float d = Math.Max(0f, damage) + Math.Max(0f, strength) / 2f;
            float ac = Math.Max(0f, armor) + Math.Max(0f, dexterity) / 2f;
            float share = 0.05f * a / 0.6f + 0.05f * d + 0.04f * ac + 0.02f * Math.Max(0f, saves);
            return Clamp(share, 0f, DebuffCap);
        }

        public const float DebuffCap = 0.6f;

        #endregion

        #region Spellcasting boosts (0.3.21)

        /// <summary>
        /// What a boost to a caster's spells adds per round, from <paramref name="spellsPerRound"/> (the worth of its casting
        /// per round): each point of save DC makes about 5 % more of the targets of its save spells fail (about 0.6 of
        /// what it casts); a caster level adds about 2 % (dice, durations) and, like spell penetration, 5 % against the
        /// <paramref name="resistantShare"/> of enemies with spell resistance. At most half of its casting.
        /// </summary>
        public static float SpellcastingGain(float spellsPerRound, float dc, float casterLevel, float penetration, float resistantShare)
        {
            if (spellsPerRound <= 0f) return 0f;
            float share = 0.05f * 0.6f * Math.Max(0f, dc) + 0.02f * Math.Max(0f, casterLevel) +
                          0.05f * Clamp(resistantShare, 0f, 1f) * (Math.Max(0f, casterLevel) + Math.Max(0f, penetration));
            return spellsPerRound * Clamp(share, 0f, 0.5f);
        }

        #endregion

        #region Poison (0.3.21)

        /// <summary>
        /// The expected number of times a poison (BuffPoisonStatDamage) deals its damage: once when it lands (OnActivate,
        /// whatever NoEffectOnFirstTick says), then in each of the next <paramref name="laterRounds"/> rounds while it
        /// has been saved against fewer than <paramref name="savesToEnd"/> times - a failed save (chance
        /// <paramref name="fail"/>) deals it again, a passed one counts towards the end.
        /// </summary>
        public static float PoisonDoses(float fail, int laterRounds, int savesToEnd)
        {
            float f = Clamp(fail, 0f, 1f);
            int saves = Math.Max(1, savesToEnd);
            var on = new float[saves];   // chance of 0 .. saves-1 passed saves so far (still poisoned)
            on[0] = 1f;
            float doses = 1f;
            for (int t = 0; t < laterRounds; t++)
            {
                var next = new float[saves];
                for (int k = 0; k < saves; k++)
                {
                    doses += on[k] * f;
                    next[k] += on[k] * f;
                    if (k + 1 < saves) next[k + 1] += on[k] * (1f - f);
                }
                on = next;
            }
            return doses;
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

        /// <summary>
        /// ★ kinetic-infusion-value: one round of an on-hit maneuver's existing control share. Delivery probability
        /// already contains the blast's hit/resistance checks; count it once, then the maneuver check once.
        /// Native callers supply the chance and control share, not a copied game formula or an infusion weight.
        /// </summary>
        public static float OnHitControlValue(float triggerChance, float checkChance, float targetWorth, float controlShare)
        {
            if (!(triggerChance > 0f) || !(checkChance > 0f) || !(targetWorth > 0f) || !(controlShare > 0f)) return 0f;
            return Math.Min(1f, triggerChance) * Math.Min(1f, checkChance) * targetWorth * controlShare;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
