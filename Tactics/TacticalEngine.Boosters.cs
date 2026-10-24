// ★ v0.3.19 (test9): boosts for the next spell - Arcane Reservoir's "Increase Caster Level" and "DC"
// (ArcanistArcaneReservoirCL/DCAbility: a free action, 1 reservoir point, a 2-round buff with AddCasterLevelForSpellbook +1
// or IncreaseSpellSpellbookDC +1 that removes itself after the next spellbook spell, AddAbilityUseTrigger AfterCast ->
// ContextActionRemoveSelf). Read as a generic "casting" buff (0.4), Orvath spent seven reservoir points in the 0.3.19-test8
// logs, mostly right before Entangle, Grease or Sleep, where one caster level changes almost nothing.
//
// Such a boost is worth what it adds to the spell actually cast next: the main action chosen with it, as own-attack marks
// are coupled with the attack (CoupleOwnAttackMarks). A DC boost raises the chance a save spell lands; a caster-level boost
// adds the damage dice a caster-level rank gives (ContextRankConfig on caster level, up to its cap). Anything else - no
// spell next, a spell without a save or caster-level dice - gains nothing and the point stays in the reservoir.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>What a next-spell boost adds, and to which spells (no spellbooks and no spell: any).</summary>
        internal sealed class SpellBoost
        {
            public int CasterLevel;
            public int DC;
            public readonly List<BlueprintSpellbook> Spellbooks = new List<BlueprintSpellbook>();
            public readonly List<BlueprintAbility> Spells = new List<BlueprintAbility>();
            /// <summary>★ v0.3.21: metamagic the next suitable spell gets for free (AutoMetamagic: Phantasmal Mage, Emergency
            /// Help, Healing Blessing, Life Spirit).</summary>
            public readonly List<AutoMetamagic> Metamagic = new List<AutoMetamagic>();
            public bool Any => CasterLevel > 0 || DC > 0 || Metamagic.Count > 0;

            public bool AppliesTo(AbilityData spell)
            {
                // ★ v0.3.21: a metamagic boost goes where the game's own rule puts it (AutoMetamagic.ShouldApplyTo).
                if (CasterLevel == 0 && DC == 0)
                    return spell != null && Metamagic.Any(m => AutoMetamagic.ShouldApplyTo(m, spell.Blueprint, spell));
                if (spell?.Spellbook == null) return false;
                if (Spells.Count > 0 && !Spells.Contains(spell.Blueprint) && !Spells.Contains(spell.Blueprint.Parent)) return false;
                return Spellbooks.Count == 0 || Spellbooks.Contains(spell.Spellbook.Blueprint);
            }
        }

        /// <summary>The boost of an ability whose every buff is spent by the next cast; null when it is not one.</summary>
        internal static SpellBoost SpellBoostOf(AbilityProfile profile, AbilityData ability, UnitEntityData caster)
        {
            // ★ v0.3.21: or a buff whose free metamagic ends with the spell it changes (AutoMetamagic.Once).
            if (profile == null) return null;
            if (!profile.BuffsSpentByCast &&
                !(profile.CasterBuffs.Count > 0 && profile.CasterBuffs.All(b => b != null && Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<AutoMetamagic>(b).Any(m => m.Once))))
                return null;
            var boost = new SpellBoost();
            try
            {
                foreach (var buff in profile.CasterBuffs.Concat(profile.TargetBuffs).Distinct())
                {
                    if (buff?.ComponentsArray == null) continue;
                    // ★ v0.3.19 (test12): only the branch this caster gets - Arcane Reservoir DC gives +1, or +2 with
                    // Potent Magic (a Conditional on the caster's fact); counting both read +3 where the game gave +1.
                    if (ability != null && caster != null &&
                        profile.BuffAppliesTo(buff, ability, caster) == EffectApplies.No) continue;
                    foreach (var c in buff.ComponentsArray)
                    {
                        switch (c)
                        {
                            case AddCasterLevelForSpellbook cl:
                                boost.CasterLevel += Math.Max(0, cl.Bonus);
                                boost.Spellbooks.AddRange(cl.Spellbooks.Where(b => b != null));
                                break;
                            case AddCasterLevelForAbility cl when cl.Spell != null:
                                boost.CasterLevel += Math.Max(0, cl.Bonus);
                                boost.Spells.Add(cl.Spell);
                                break;
                            case IncreaseCasterLevel cl:
                                boost.CasterLevel += Math.Max(0, SimpleValue(cl.Value));
                                break;
                            case IncreaseSpellSpellbookDC dc:
                                boost.DC += Math.Max(0, dc.BonusDC);
                                boost.Spellbooks.AddRange(dc.Spellbooks.Where(b => b != null));
                                break;
                            case IncreaseSpellDC dc when dc.Spell != null:
                                boost.DC += Math.Max(0, dc.UseContextBonus ? SimpleValue(dc.Value) : dc.BonusDC);
                                boost.Spells.Add(dc.Spell);
                                break;
                            case IncreaseAllSpellsDC dc:
                                boost.DC += Math.Max(0, SimpleValue(dc.Value));
                                break;
                            case AutoMetamagic metamagic:
                                boost.Metamagic.Add(metamagic);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] spell boost of {profile.Blueprint?.name}: {ex.Message}");
                return null;
            }
            return boost.Any ? boost : null;
        }

        private static int SimpleValue(Kingmaker.UnitLogic.Mechanics.ContextValue value) =>
            value == null ? 0 : value.IsValueSimple ? value.Value : 1;

        /// <summary>
        /// Next-spell boosts keep only what they add to the best main action, when that is a spell they apply to; dropped
        /// otherwise. Free actions, so this is the whole decision about them.
        /// </summary>
        private static List<Candidate> CoupleSpellBoosts(TacticalContext ctx, List<Candidate> usable)
        {
            SpellBoost BoostOf(Candidate c) =>
                c.Kind == CandidateKind.Ability && c.Ability != null ? SpellBoostOf(AbilityProfile.Get(c.Ability), c.Ability, ctx.Unit) : null;
            if (!usable.Any(c => BoostOf(c) != null)) return usable;

            var main = usable.FirstOrDefault(c => c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift && BoostOf(c) == null);
            var result = new List<Candidate>(usable.Count);
            foreach (var c in usable)
            {
                var boost = BoostOf(c);
                if (boost == null) { result.Add(c); continue; }
                float gain = BoostGain(ctx, boost, main, out string note);
                // ★ v0.3.19 (release): only a boost that is worth something reaches the normal log (two lines a round
                // for each of Orvath's reservoir abilities, nearly all "0.0 - no spell to boost next").
                if (gain <= 0f)
                {
                    // ★ v0.3.20: "no spell to boost next" (372 verbose lines in one real-time log, the best action being an
                    // attack) says nothing; only why a boost does not help the spell that comes next.
                    if (note != NoSpellToBoost) Main.Verbose($"[Tactics] {ctx.Unit.CharacterName}: {c.Ability.Name} = 0 ({note})");
                    continue;
                }
                ReportOnce($"boost|{ctx.Unit.UniqueId}|{CombatMemory.Round}|{c.Key}|{main?.Key}",
                           $"[Tactics] {ctx.Unit.CharacterName}: {c.Ability.Name} = {gain:F1} ({note})");
                // ★ v0.3.19 (test12): the "[Buff]" line explains this value, not the buff formula it replaced.
                s_LastAppraisal[AppraisalKey(ctx.Unit, c.Ability, c.Target ?? ctx.Unit)] = $"{gain:0.#}: next-spell boost, {note}";
                c.Value = gain;
                c.Label += $" [for {main.Ability.Name}]";
                result.Add(c);
            }
            return result.OrderByDescending(c => c.Value).ToList();
        }

        private const string NoSpellToBoost = "no spell to boost next";

        /// <summary>What <paramref name="boost"/> adds to <paramref name="main"/>, in its own value's points.</summary>
        private static float BoostGain(TacticalContext ctx, SpellBoost boost, Candidate main, out string note)
        {
            note = NoSpellToBoost;
            if (main == null || main.Kind != CandidateKind.Ability || main.Ability == null || main.Ability.SourceItem != null ||
                main.Ability.Blueprint.Type != AbilityType.Spell || main.Value <= 0f)
                return 0f;
            if (!boost.AppliesTo(main.Ability)) { note = $"does not apply to {main.Ability.Name}"; return 0f; }
            var profile = AbilityProfile.Get(main.Ability);
            if (profile == null) return 0f;

            float share = 0f;
            var parts = new List<string>();
            // DC: the chance the save is failed rises by 5 % a point (up to the natural-1 cap); the value grows with it.
            if (boost.DC > 0 && profile.Save != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown &&
                main.Target != null && ctx.Unit.IsEnemy(main.Target))
            {
                float p = CombatMath.EffectChance(ctx.Unit, main.Target, main.Ability, profile, damage: false);
                if (p > 0f && p < 0.95f)
                {
                    float dc = Math.Min(0.05f * boost.DC, 0.95f - p) / Math.Max(0.1f, p);
                    // Half on a save: the save only decides half of the damage.
                    if (profile.HalfOnSave && profile.ControlStrength == ControlStrength.None) dc *= 0.5f;
                    share += dc;
                    parts.Add($"DC +{boost.DC}: lands {p:P0} -> {Math.Min(0.95f, p + 0.05f * boost.DC):P0}");
                }
            }
            // Caster level: more damage dice where a caster-level rank counts them, up to its cap.
            if (boost.CasterLevel > 0 && profile.DealsDamage)
            {
                int level = Math.Max(1, main.Ability.Spellbook?.CasterLevel ?? 1);
                float dice = CasterLevelDiceGain(main.Ability.Blueprint, level, boost.CasterLevel);
                if (dice > 0f)
                {
                    share += 0.7f * dice;   // the damage part of a damage spell's value
                    parts.Add($"caster level +{boost.CasterLevel}: +{dice:P0} damage dice");
                }
            }
            // ★ v0.3.21: free metamagic on the spell cast next, where the game applies it and the spell does not have it.
            share += MetamagicGain(ctx, boost, main, profile, parts);
            note = parts.Count > 0 ? $"for {main.Ability.Name}: {string.Join(", ", parts)}" : $"adds nothing to {main.Ability.Name}";
            return main.Value * share;
        }

        /// <summary>
        /// ★ v0.3.21: the share of <paramref name="main"/>'s value that free metamagic adds: Empower half again of its
        /// damage or healing, Maximize the dice at their best (about +60 % of a dice roll), both together half again of
        /// the maximum (the game's rule for the pair); Persistent a second save, so a target resists only by passing both.
        /// Reach, Extend, Quicken, Selective and the rest change no number the value is made of: not counted.
        /// </summary>
        private static float MetamagicGain(TacticalContext ctx, SpellBoost boost, Candidate main, AbilityProfile profile, List<string> parts)
        {
            Kingmaker.UnitLogic.Abilities.Metamagic flags = 0;
            foreach (var m in boost.Metamagic)
                if (AutoMetamagic.ShouldApplyTo(m, main.Ability.Blueprint, main.Ability)) flags |= m.Metamagic;
            if (flags == 0) return 0f;
            bool Has(Kingmaker.UnitLogic.Abilities.Metamagic f) => (flags & f) != 0 && !main.Ability.HasMetamagic(f);
            float share = 0f;
            bool numbers = profile.DealsDamage || profile.Heals.Count > 0;
            bool empower = Has(Kingmaker.UnitLogic.Abilities.Metamagic.Empower), maximize = Has(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize);
            if (numbers && (empower || maximize))
            {
                float more = empower && maximize ? 1.4f : maximize ? 0.6f : 0.5f;
                share += 0.7f * more;   // the damage or healing part of the value
                parts.Add($"{(empower && maximize ? "empowered and maximized" : maximize ? "maximized" : "empowered")}: +{more:P0} dice");
            }
            if (Has(Kingmaker.UnitLogic.Abilities.Metamagic.Persistent) && profile.Save != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown &&
                main.Target != null && ctx.Unit.IsEnemy(main.Target))
            {
                float fail = CombatMath.SaveFailChance(main.Ability, profile, main.Target);
                if (fail > 0f && fail < 1f)
                {
                    // Resisting needs both saves: the effect lands at 1 - (1 - fail)^2 instead of fail.
                    float gain = 1f - fail;
                    if (profile.HalfOnSave && profile.ControlStrength == ControlStrength.None) gain *= 0.5f;
                    share += gain;
                    parts.Add($"persistent: lands {fail:P0} -> {1f - (1f - fail) * (1f - fail):P0}");
                }
            }
            return share;
        }

        private static readonly System.Reflection.FieldInfo RankBase =
            HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_BaseValueType");
        private static readonly System.Reflection.FieldInfo RankUseMax =
            HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_UseMax");
        private static readonly System.Reflection.FieldInfo RankMax =
            HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_Max");
        private static readonly System.Reflection.FieldInfo RankProgression =
            HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_Progression");

        /// <summary>
        /// The share of extra rank a caster-level-based ContextRankConfig of <paramref name="spell"/> gets from
        /// <paramref name="bonus"/> more caster levels (AsIs: one die a level, up to its cap; a stepped progression about
        /// half that). 0 when none of its ranks count caster levels or the cap is reached.
        /// </summary>
        private static float CasterLevelDiceGain(BlueprintAbility spell, int casterLevel, int bonus)
        {
            float best = 0f;
            try
            {
                foreach (var config in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<ContextRankConfig>(spell))
                {
                    if (RankBase?.GetValue(config)?.ToString() != "CasterLevel") continue;
                    bool useMax = RankUseMax?.GetValue(config) is bool b && b;
                    int cap = useMax && RankMax?.GetValue(config) is int m ? m : int.MaxValue;
                    float now = Math.Min(casterLevel, cap), then = Math.Min(casterLevel + bonus, cap);
                    if (then <= now) continue;
                    float gain = (then - now) / Math.Max(1f, now);
                    if (RankProgression?.GetValue(config)?.ToString() != "AsIs") gain *= 0.5f;
                    best = Math.Max(best, gain);
                }
            }
            catch
            {
            }
            return best;
        }
    }
}
