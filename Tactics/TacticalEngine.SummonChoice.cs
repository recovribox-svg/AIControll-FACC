using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    // ★ v0.3.70 F3 (author request): which summon to conjure. A summon spell was worth its spell level alone, so every variant
    // of one spell scored the same (Summon Monster VIII's Monadic Deva and Frost Giant were tied) and nothing of the fight
    // entered the choice. Now each spawn action is priced by what the game will put on the field, read from the ability's own
    // context: how many creatures (its count dice, with the game's own count handlers such as Superior Summoning), for how
    // many rounds (its duration, at most the fight's expected rounds), each by its CR against the enemies' and by what that
    // kind has actually dealt per round it fought this session; and how many of those bodies have an enemy to fight beside the
    // party's summons already in the field.
    public static partial class TacticalEngine
    {
        private static readonly SummonWorthMemory s_SummonWorth = new SummonWorthMemory();
        private static readonly HashSet<string> s_SummonRoundsNoted = new HashSet<string>(StringComparer.Ordinal);
        private static int s_SummonRoundsFight = -1;

        /// <summary>A round a party summon stood in the fight, once per creature and round.</summary>
        private static void NoteSummonRounds(TacticalContext ctx)
        {
            if (s_SummonRoundsFight != CombatMemory.Fight) { s_SummonRoundsFight = CombatMemory.Fight; s_SummonRoundsNoted.Clear(); }
            if (s_SummonRoundsNoted.Count >= 2048) return;
            foreach (var ally in ctx.Allies)
            {
                if (ally == null || !IsSummon(ally) || !ally.Descriptor.State.IsConscious) continue;
                if (!s_SummonRoundsNoted.Add($"{ally.UniqueId}|{ctx.Round}")) continue;
                s_SummonWorth.NoteRound(ally.Blueprint?.AssetGuid.ToString());
            }
        }

        /// <summary>A party summon's hit on an enemy (DamageObserver): its kind's damage this session.</summary>
        internal static void NoteSummonDamage(UnitEntityData attacker, int damage)
        {
            try
            {
                if (attacker == null || damage <= 0 || !IsSummon(attacker)) return;
                s_SummonWorth.NoteDamage(attacker.Blueprint?.AssetGuid.ToString(), damage);
            }
            catch { }
        }

        /// <summary>
        /// What <paramref name="ability"/>'s creatures are worth in this fight, before the crowd and easy-fight restraints;
        /// null when it has no readable spawn action (the spell-level fallback then applies).
        /// </summary>
        private static float? SummonWorth(TacticalContext ctx, AbilityData ability, AbilityProfile profile, out string note)
        {
            note = null;
            if (profile.SpawnActions.Count == 0) return null;
            MechanicsContext context;
            try { context = CombatMath.CreateContext(ability, new TargetWrapper(ctx.Unit.Position)); }
            catch { return null; }
            if (context == null) return null;
            var awake = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).ToList();
            float enemyLevel = awake.Count > 0 ? (float)awake.Average(e => Level(e)) : 1f;
            int fighting = ctx.Allies.Count(a => a != null && IsSummon(a) && a.Descriptor.State.IsConscious);
            float horizon = ValueModel.Horizon(ctx.ExpectedRounds, 4f);
            float total = 0f, bodies = 0f;
            var parts = new List<string>(profile.SpawnActions.Count);
            foreach (var spawn in profile.SpawnActions)
            {
                var kind = spawn.Blueprint;
                if (kind == null) continue;
                float count = SummonCount(context, spawn);
                float rounds = horizon;
                try
                {
                    float lasts = spawn.DurationValue.Calculate(context).Value;
                    if (lasts > 0f) rounds = Math.Min(rounds, lasts);
                }
                catch { }
                float prior = ValueModel.SummonPrior(kind.CR, enemyLevel);
                bool seen = s_SummonWorth.TryPerRound(kind.AssetGuid.ToString(), out float measured, out int roundsSeen);
                float perRound = ValueModel.SummonPerRound(prior, seen ? measured : float.NaN, seen ? roundsSeen : 0);
                total += count * perRound * rounds;
                bodies += count;
                parts.Add($"{count:0.#}x {kind.name} CR{kind.CR} {perRound:0.#}/rd" + (seen ? $" (dealt {measured:0.#}/rd over {roundsSeen} rd)" : "") +
                          $" for {rounds:0.#} rd");
            }
            if (bodies <= 0f) return null;
            float share = ValueModel.SummonBodyShare(bodies, fighting, awake.Count);
            note = string.Join(", ", parts) + (share < 0.995f ? $"; {share:P0} of them have an enemy to fight" : "");
            return total * share;
        }

        /// <summary>
        /// The creatures one spawn action brings: the average of its count dice under the ability's context, then the
        /// game's own count handlers (ICalculateSummonUnitsCount: Superior Summoning's extra creature) on that average.
        /// </summary>
        private static float SummonCount(MechanicsContext context, ContextActionSpawnMonster spawn)
        {
            float average = 1f;
            try
            {
                var dice = spawn.CountValue;
                if (dice != null)
                {
                    float rolls = dice.DiceCountValue?.Calculate(context) ?? 0;
                    float bonus = dice.BonusValue?.Calculate(context) ?? 0;
                    int sides = Kingmaker.RuleSystem.DiceTypeExtension.Sides(dice.DiceType);
                    average = Math.Max(1f, rolls * (sides + 1) / 2f * (sides > 0 ? 1f : 0f) + bonus);
                    int rounded = (int)Math.Round(average);
                    int count = rounded;
                    EventBus.RaiseEvent(delegate (ICalculateSummonUnitsCount h) { h.HandleCalculateSummonUnitsCount(context, dice, ref count); });
                    average += count - rounded;
                }
            }
            catch { }
            return Math.Max(1f, average);
        }
    }
}
