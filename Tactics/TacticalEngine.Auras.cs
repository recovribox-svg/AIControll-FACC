// ★ v0.3.21 (coverage gaps): buffs that carry an aura around their bearer (AddAreaEffect) - Archon's Aura, Aura of Greater
// Courage, the Cleric domain and Warpriest blessing greater powers, Celestial Aura of Heaven, Judgment Aura. Before, the
// aura was an unread component of its buff: the ability was valued by the old 0.5 guess (or 0 on a limited use), on the
// caster alone, whatever the aura gave and to whom.
//
//   - Who: the units around the bearer (the caster for a personal ability, otherwise its target) within the area's size,
//     on the sides the area reaches, through the conditions the area gives each buff under (TacticalEngine.Toggles.cs
//     ReadAreaGrants / GrantReaches: the same reading as performances), and a failed save when the area gives it on one.
//   - Allies: each one's buffs appraised as any buff (AppraiseToggleBuffs: its attacks, what comes at it, stacking), over
//     the rounds the carrier lasts. Effects with no readable part are not guessed here; an aura none of whose buffs can
//     be read keeps the old reading of its carrier.
//   - Enemies: a debuff (by its penalties, ValueModel.DebuffShare, with the flat 0.25 as floor when part is unread) or
//     control (by its conditions), times the chance it lands (spell resistance, the area's save) - as the control section.
//   - A harmful aura that also reaches allies costs what it would take from them.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly Dictionary<BlueprintAbilityAreaEffect, List<AreaGrant>> s_AuraGrants =
            new Dictionary<BlueprintAbilityAreaEffect, List<AreaGrant>>();

        private static List<AreaGrant> AuraGrants(BlueprintAbilityAreaEffect area)
        {
            if (area == null) return new List<AreaGrant>();
            if (s_AuraGrants.TryGetValue(area, out var cached)) return cached;
            var grants = new List<AreaGrant>();
            try { ReadAreaGrants(area, grants, followSaves: true); }
            catch (Exception ex) { Main.Verbose($"[Aura] {area.name}: {ex.Message}"); }
            s_AuraGrants[area] = grants;
            return grants;
        }

        private static bool HarmfulGrant(AreaGrant grant) => grant.Buff.Harmful || AbilityProfile.IsHarmfulBuff(grant.Buff);

        /// <summary>The aura gives something the AI can read: a buff with read parts, or a harmful one.</summary>
        private static bool AuraRead(AbilityProfile.AuraEffect aura) =>
            AuraGrants(aura.Area).Any(g => HarmfulGrant(g) || BuffPartsReader.Read(g.Buff).Any);

        /// <summary>
        /// <paramref name="buff"/> is only the carrier of an aura that is read (no part of its own is read): its worth is the
        /// aura's (<see cref="AuraValue"/>), not a guess on its bearer. Archon's Aura's carrier also has an attack trigger
        /// the reader does not value; guessed at 0.5 on the caster as well, the aura was counted twice.
        /// </summary>
        private static bool AuraOnlyCarrier(AbilityProfile profile, BlueprintBuff buff)
        {
            var aura = profile.Auras.FirstOrDefault(a => a.Carrier == buff);
            return aura != null && AuraRead(aura) && !BuffPartsReader.Read(buff).Any;
        }

        /// <summary>The share of <paramref name="unit"/>'s worth per round that <paramref name="buffs"/> (harmful) take.</summary>
        private static float HarmShare(TacticalContext ctx, AbilityData ability, UnitEntityData unit, List<BlueprintBuff> buffs)
        {
            MechanicsContext abilityCtx = null;
            try { abilityCtx = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(unit)); } catch { }
            return HarmShareIn(ctx, abilityCtx, unit, buffs);
        }

        /// <summary>The same with a given source context.</summary>
        private static float HarmShareIn(TacticalContext ctx, MechanicsContext abilityCtx, UnitEntityData unit, List<BlueprintBuff> buffs)
        {
            // Frozen out of the fight on both sides: Time Stop's buff stops its bearer and makes it untouchable.
            if (buffs.Any(b => b.ComponentsArray?.Any(c => c is Kingmaker.UnitLogic.FactLogic.Untargetable ||
                                                            c is Kingmaker.UnitLogic.FactLogic.CompleteDamageImmunity) ?? false))
                return 0f;
            var control = buffs.Select(AbilityProfile.BuffControl).DefaultIfEmpty(ControlStrength.None).Max();
            if (control == ControlStrength.Hard) return 1f;
            if (control == ControlStrength.Soft) return 0.4f;
            float share = DebuffShareIn(ctx, abilityCtx, unit, buffs, out bool partial);
            if (share < 0f) share = 0.25f;
            else if (partial) share = Math.Max(share, 0.25f);
            return control == ControlStrength.Minor ? Math.Max(share, 0.15f) : share;
        }

        /// <summary>The worth of the auras <paramref name="ability"/>'s buffs carry (damage-equivalent points), and a note.</summary>
        private static float AuraValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                       bool targetIsEnemy, out string note)
        {
            note = null;
            var unit = ctx.Unit;
            float total = 0f;
            int helped = 0, hindered = 0;
            foreach (var aura in profile.Auras)
            {
                try
                {
                    // The target bears it otherwise - an enemy too: Ghoul Touch's paralysed target sickens those around it.
                    // The area's sides are the caster's (its context's).
                    var bearer = aura.OnCaster ? unit : target;
                    if (bearer == null || bearer.Buffs.HasFact(aura.Carrier)) continue;
                    if (profile.BuffAppliesTo(aura.Carrier, ability, bearer) == EffectApplies.No) continue;
                    var grants = AuraGrants(aura.Area);
                    if (grants.Count == 0) continue;

                    var abilityCtx = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(bearer));
                    MechanicsContext auraCtx = null;
                    try
                    {
                        auraCtx = new MechanicsContext(unit, bearer.Descriptor, aura.Carrier, abilityCtx);
                        auraCtx.RecalculateRanks();
                    }
                    catch { auraCtx = abilityCtx; }
                    float duration = EffectiveRounds(ability, profile, aura.Carrier, abilityCtx);
                    float unknown = profile.HasLongBuff ? ctx.ExpectedRounds : Math.Min(ctx.ExpectedRounds, 3f);
                    float rounds = ValueModel.UsefulRounds(duration, ctx.ExpectedRounds, unknown);
                    float harmRounds = Math.Min(rounds, Math.Min(ctx.ExpectedRounds, 3f));
                    float radius = aura.Area.Size.Meters;
                    string key = $"aura:{CombatMemory.AbilityKey(ability)}:{aura.Carrier.AssetGuid}";

                    // What reaches a unit: (buff, weight) for the grants whose conditions hold for it.
                    List<(BlueprintBuff Buff, float Weight, SavingThrowType Save)> Reaching(UnitEntityData u, bool harmful) =>
                        grants.Where(g => HarmfulGrant(g) == harmful && !u.Buffs.HasFact(g.Buff))
                              .Select(g => (g.Buff, GrantReaches(g, auraCtx, u), g.Save))
                              .Where(r => r.Item2 > 0f).ToList();

                    if (aura.Area.CanTargetAllies)
                        foreach (var ally in UnitsAround(ctx.Allies, bearer.Position, radius).Take(6))
                        {
                            var useful = Reaching(ally, false)
                                .Where(r => r.Save == SavingThrowType.Unknown && BuffStackingAnalyzer.Analyze(r.Buff, ally).IsEffective).ToList();
                            if (useful.Count > 0 && auraCtx != null)
                            {
                                var appraisal = AppraiseToggleBuffs(ctx, auraCtx, key, useful.Select(r => r.Buff).Distinct().ToList(), ally, rounds);
                                if (appraisal.Modelled && appraisal.Value > 0f)
                                {
                                    total += appraisal.Value * useful.Max(r => r.Weight);
                                    helped++;
                                }
                            }
                            // A harmful aura that also reaches allies costs what it takes from them.
                            var hurt = Reaching(ally, true);
                            if (hurt.Count > 0)
                                total -= hurt.Max(r => r.Weight) * CombatMath.ControlWorth(ally) *
                                         HarmShare(ctx, ability, ally, hurt.Select(r => r.Buff).ToList()) * harmRounds;
                        }
                    if (aura.Area.CanTargetEnemies)
                        foreach (var enemy in UnitsAround(ctx.Enemies, bearer.Position, radius))
                        {
                            var hits = Reaching(enemy, true).Where(r => !CombatMath.BuffImmune(enemy, r.Buff, ability)).ToList();
                            if (hits.Count == 0) continue;
                            float p = CombatMath.SpellResistancePass(unit, enemy, ability) *
                                      hits.Max(r => r.Weight * CombatMath.SaveFailChance(ability, r.Save, enemy));
                            float worth = p * CombatMath.ControlWorth(enemy) * HarmShare(ctx, ability, enemy, hits.Select(r => r.Buff).ToList()) * harmRounds;
                            if (CombatMath.IsDisabled(enemy)) worth *= 0.4f;
                            if (worth <= 0f) continue;
                            total += worth;
                            hindered++;
                        }
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Aura] {ability.Name}: {ex.Message}");
                }
            }
            if (helped > 0 || hindered > 0)
                note = helped > 0 && hindered > 0 ? $"aura {helped} allies, {hindered} enemies"
                     : helped > 0 ? $"aura {helped} allies" : $"aura {hindered} enemies";
            return total;
        }

        /// <summary>For the offline audit and the coverage report: what the ability's auras give, or null when none is read.</summary>
        internal static string AuraReading(AbilityProfile profile)
        {
            if (profile == null || profile.Auras.Count == 0) return null;
            var read = profile.Auras.Where(AuraRead).ToList();
            if (read.Count == 0) return null;
            return string.Join("; ", read.Select(a =>
                $"{a.Area.Size.Meters:0.#} m: " + string.Join(", ", AuraGrants(a.Area).Select(g =>
                    g.Buff.name + (HarmfulGrant(g) ? " (harmful" + (g.Save != SavingThrowType.Unknown ? $", {g.Save} save)" : ")") : "")))));
        }
    }
}
