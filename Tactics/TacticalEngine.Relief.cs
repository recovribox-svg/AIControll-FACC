// ★ coverage5 (author request; docs/RELIEF_SPELLS.md): relief for an ally an effect hinders - confused, frightened, held,
// shaken, dominated - and the spells that end it (a dispel, a removal) or switch it off (a suppression).
// Evidence: a user of the stable 0.3.52 reports "none of the magic-using characters ever cast Dispel Magic". The cause: a
// dispel aimed at anyone was worth the flat 2-point "dispel" fallback, once a fight, whatever it took off; Break
// Enchantment only counted what was on its caster; Remove Fear and Unbreakable Heart were valued only by their save bonus
// (their SuppressBuffs unread).
// Native contracts (supplied assembly and exports):
//  - ContextActionDispelMagic tries the target's buffs by caster level, highest first, up to its count; RuleDispelMagic has
//    no protection for a friendly buff, so a dispel on an ally can take off the party's own buffs (Haste, Heroism) first;
//  - Break Enchantment: every Enchantment and Transmutation effect up to 5th level on each ally within 30 ft, no count;
//  - Remove Fear's buff suppresses Shaken and Frightened; Unbreakable Heart's suppresses Fear, Shaken, Frightened,
//    Confusion, Compulsion and negative emotion (and so Dominate). A new suppressor re-checks the buffs already on its
//    bearer (SuppressBuffs.OnActivate -> UnitPartBuffSuppress.ApplyChanges), by the buff context's descriptor and school;
//  - Confusion: a status condition with no round save, for its rank in rounds.
// The worth of ending an effect is what it takes from the ally (its control or penalty share, OwnDebuffShare) times the
// ally's output per round, for the rounds it would still last (its time left, its round re-save, its suppression), within
// three rounds; at most the ally's whole output. Valuation only: no buff, context or rule is run.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>The rounds relief is counted for: as a cleanse's, the fight's expected rounds, at most three.</summary>
        private static float ReliefHorizon(TacticalContext ctx) => Math.Min(Math.Max(1f, ctx.ExpectedRounds), 3f);

        /// <summary>An ally's output for a round (its worth's base), half for a summon.</summary>
        private static float AllyPerRound(UnitEntityData ally) =>
            ValueModel.PerRound(Level(ally)) * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);

        /// <summary>An effect working against its bearer: given by one of its enemies, or flagged harmful.</summary>
        internal static bool IsHarmfulOn(UnitEntityData bearer, Buff buff)
        {
            try
            {
                var giver = buff?.MaybeContext?.MaybeCaster;
                return giver != null && giver.IsEnemy(bearer) || (buff?.Blueprint?.Harmful ?? false);
            }
            catch { return false; }
        }

        /// <summary>
        /// What <paramref name="buff"/> costs <paramref name="ally"/>: its control or penalty share of the ally's output a round,
        /// for the rounds it would still act within the horizon (and at most <paramref name="capRounds"/>, a suppressor's time).
        /// </summary>
        internal static float HarmWorth(TacticalContext ctx, UnitEntityData ally, Buff buff, float horizon, float capRounds = float.PositiveInfinity)
        {
            try
            {
                if (ally?.Descriptor?.State == null || !ally.Descriptor.State.IsConscious || !IsHarmfulOn(ally, buff)) return 0f;
                float share = OwnDebuffShare(ctx, buff, ally);
                if (!(share > 0f)) return 0f;
                float rounds = buff.IsPermanent ? horizon : Math.Min(horizon, (float)buff.TimeLeft.TotalSeconds / 6f);
                if (buff.IsSuppressed) rounds = SuppressionRules.ActiveRounds(rounds, BuffSuppression.SuppressedSeconds(buff) / 6f, horizon);
                rounds = Math.Min(rounds, capRounds);
                if (!(rounds > 0f)) return 0f;
                return Math.Min(1f, share) * AllyPerRound(ally) * ValueModel.HeldRounds(rounds, RoundSavePass(buff, ally));
            }
            catch { return 0f; }
        }

        /// <summary>Removing harmful effects by name or descriptor (Remove Paralysis, Optimistic Smile...): what they cost the ally.</summary>
        private static float RemovalRelief(TacticalContext ctx, AbilityProfile profile, UnitEntityData ally, float horizon)
        {
            if (!profile.RemovesAnything || ally?.Buffs == null) return 0f;
            float total = 0f;
            foreach (var buff in ally.Buffs.Enumerable)
                if (RemovesByNameOrDescriptor(profile, buff)) total += HarmWorth(ctx, ally, buff, horizon);
            return Math.Min(total, AllyPerRound(ally) * horizon);
        }

        private static readonly Dictionary<BlueprintBuff, bool> s_Suppressors = new Dictionary<BlueprintBuff, bool>();

        /// <summary>The ability puts a buff on its recipient that suppresses other effects (cached per buff).</summary>
        internal static bool SuppressesOnRecipient(AbilityProfile profile)
        {
            if (profile?.TargetBuffs == null || profile.TargetBuffs.Count == 0) return false;
            foreach (var buff in profile.TargetBuffs)
            {
                if (buff == null) continue;
                if (!s_Suppressors.TryGetValue(buff, out bool suppresses))
                {
                    try { suppresses = buff.GetComponent<SuppressBuffs>() != null; } catch { suppresses = false; }
                    if (s_Suppressors.Count > 2048) s_Suppressors.Clear();
                    s_Suppressors[buff] = suppresses;
                }
                if (suppresses) return true;
            }
            return false;
        }

        /// <summary>Whether a SuppressBuffs component switches off <paramref name="buff"/> (UnitPartBuffSuppress.IsSuppressed).</summary>
        private static bool SuppressorHits(SuppressBuffs component, Buff buff)
        {
            if (component == null || buff?.Blueprint == null) return false;
            if (component.ExcludedBuffs.Contains(buff.Blueprint)) return false;
            if (component.Buffs.Contains(buff.Blueprint)) return true;
            var context = buff.MaybeContext;
            if (context == null) return false;
            if (component.Descriptor.Value != SpellDescriptor.None && (component.Descriptor.Value & context.SpellDescriptor) != SpellDescriptor.None)
                return true;
            return component.Schools != null && Array.IndexOf(component.Schools, context.SpellSchool) >= 0;
        }

        /// <summary>
        /// The buffs <paramref name="ability"/> puts on <paramref name="recipient"/> that suppress effects already on it (Remove
        /// Fear, Unbreakable Heart): the harm they switch off, for as long as the suppressor lasts; on an ally under an enemy's
        /// control, a suppressed hold frees it for that time (FreedWorth).
        /// </summary>
        private static float SuppressionRelief(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData recipient,
                                               float horizon, out bool frees)
        {
            frees = false;
            if (recipient?.Buffs == null || profile.TargetBuffs.Count == 0) return 0f;
            bool turned = ctx.TurnedAllies.Contains(recipient);
            float total = 0f;
            MechanicsContext durationContext = null;
            foreach (var occurrence in profile.BuffApplications)
            {
                var applied = occurrence.Buff;
                if (applied == null || !profile.TargetBuffs.Contains(applied)) continue;
                var suppressors = applied.GetComponents<SuppressBuffs>();
                bool any = false;
                foreach (var _ in suppressors) { any = true; break; }
                if (!any) continue;
                float lasts = horizon;
                try
                {
                    if (TryFusionApplicationRounds(ability, occurrence, recipient, ref durationContext, out float rounds)) lasts = rounds;
                }
                catch { }
                float part = 0f;
                foreach (var buff in recipient.Buffs.Enumerable)
                {
                    if (buff == null || buff.IsSuppressed) continue;   // already off: nothing to gain
                    bool hit = false;
                    foreach (var component in suppressors) if (SuppressorHits(component, buff)) { hit = true; break; }
                    if (!hit) continue;
                    if (turned && HoldsUnderControl(buff)) { part += FreedWorth(ctx, recipient, buff, lasts); frees = true; }
                    else if (!turned) part += HarmWorth(ctx, recipient, buff, horizon, lasts);
                }
                total = Math.Max(total, part);   // one occurrence of the same buff stands for the cast
            }
            return turned ? total : Math.Min(total, AllyPerRound(recipient) * horizon);
        }

        /// <summary>
        /// ★ coverage5: an ordinary dispel (Dispel Magic, Greater Dispel Magic, Break Enchantment) on every unit it reaches, in the
        /// game's own order and count (DispelValue): an enemy's buffs taken off are its loss; on an ally, a harmful effect taken
        /// off is relief and a buff the party gave it is a loss. Allies under an enemy's control are FreeingValue's. False when
        /// no dispel action of the ability can be read (the old fallback then stays).
        /// </summary>
        private static bool TryDispelValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                           List<UnitEntityData> affectedEnemies, List<UnitEntityData> affectedAllies,
                                           out float value, out string note)
        {
            value = 0f;
            note = null;
            if (profile.DispelActions.Count == 0) return false;
            bool readable = false;
            foreach (var dispel in profile.DispelActions)
                if (DispelUnsupported(dispel) == null || IsAutomaticDispel(dispel)) { readable = true; break; }
            if (!readable) return false;
            float horizon = ReliefHorizon(ctx);
            var units = new List<UnitEntityData>();
            if (target != null) units.Add(target);
            foreach (var u in affectedEnemies) if (!units.Contains(u)) units.Add(u);
            foreach (var u in affectedAllies) if (!units.Contains(u)) units.Add(u);
            float gain = 0f, loss = 0f;
            int helped = 0, hurt = 0;
            foreach (var u in units)
            {
                if (u == null || ctx.TurnedAllies.Contains(u)) continue;
                MechanicsContext source = null;
                try { source = CombatMath.CreateContext(ability, new TargetWrapper(u)); } catch { }
                float best = float.NegativeInfinity;
                foreach (var dispel in profile.DispelActions)
                {
                    float worth = DispelValue(ctx, dispel, source, u, horizon, true, true, out _);
                    if (worth > best) best = worth;
                }
                if (float.IsNegativeInfinity(best) || best == 0f) continue;
                if (ctx.Unit.IsEnemy(u))
                {
                    float reach;
                    try { reach = CombatMath.EffectChance(ctx.Unit, u, ability, profile, damage: false); } catch { reach = 0f; }
                    best *= reach;
                }
                if (best > 0f) { gain += best; helped++; } else { loss -= best; hurt++; }
            }
            value = gain - loss;
            note = loss > 0f ? $"dispel +{gain:0.#} ({helped}) -{loss:0.#} ({hurt} lose own buffs)" : $"dispel +{gain:0.#} ({helped})";
            return true;
        }
    }
}
