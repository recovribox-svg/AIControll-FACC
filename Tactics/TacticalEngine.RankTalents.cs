// ★ v0.3.28 test 6: wild talents whose cast adds one rank to a feature that another fact on the caster reads - Enveloping
// Winds (+5 % ranged miss chance a rank, read by EnvelopingWindsBuff's SetAttackerMissChance through a ContextRankConfig on
// the feature's rank) and Shroud of Water's upgrade (+1 AC a rank up to half the shroud again: the ShroudOfWater component).
// The cast's own buff is only an AddFacts of that feature, which nothing could read: such a cast was worth 0, or a guess.
// What it adds is read from the game's own components, for the rank it would add, so it is right at every level (the
// shroud's base grows with class level, the cap with it; the winds' class-level steps are in the same formula).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly FieldInfo RankConfigBaseType = HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_BaseValueType");
        private static readonly FieldInfo RankConfigFeature = HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_Feature");
        private static readonly MethodInfo RankApplyProgression = HarmonyLib.AccessTools.Method(typeof(ContextRankConfig), "ApplyProgression", new[] { typeof(int) });
        private static readonly MethodInfo RankApplyMinMax = HarmonyLib.AccessTools.Method(typeof(ContextRankConfig), "ApplyMinMax",
            new[] { typeof(int), typeof(bool), typeof(MechanicsContext) });
        private static readonly FieldInfo MissType = HarmonyLib.AccessTools.Field(typeof(SetAttackerMissChance), "m_Type");

        /// <summary>The rank config reads the rank of <paramref name="feature"/> (ContextRankBaseValueType.FeatureRank).</summary>
        private static bool ReadsRankOf(ContextRankConfig config, BlueprintFeature feature)
        {
            try
            {
                if (!(RankConfigBaseType?.GetValue(config) is ContextRankBaseValueType type) || type != ContextRankBaseValueType.FeatureRank) return false;
                return (RankConfigFeature?.GetValue(config) as BlueprintFeatureReference)?.Get() == feature;
            }
            catch { return false; }
        }

        /// <summary>
        /// The parts <paramref name="buffs"/> add through one more rank of a feature that a fact of <paramref name="ally"/>
        /// reads, as BuffParts the appraisal values like any other: the shroud's new total AC with its own descriptor (the
        /// stacking rule then counts only what it adds over the bonus it gives now or the armor worn), the winds' extra
        /// miss chance for the share of the incoming damage its attack types cover.
        /// </summary>
        internal static void AddRankIncrementParts(TacticalContext ctx, List<BlueprintBuff> buffs, UnitEntityData ally, List<BuffPart> parts)
        {
            if (buffs == null || ally == null) return;
            foreach (var buff in buffs)
            {
                if (buff?.ComponentsArray == null) continue;
                foreach (var add in buff.GetComponents<AddFacts>())
                {
                    foreach (var fact in add.Facts)
                    {
                        if (!(fact is BlueprintFeature feature) || feature.Ranks <= 1) continue;
                        int rank;
                        try { rank = ally.Descriptor.Progression.Features.GetRank(feature); } catch { continue; }
                        if (rank >= feature.Ranks) continue;   // the feature cannot take another rank
                        try { AddIncrements(ctx, buff, feature, rank, ally, parts); }
                        catch (Exception ex) { Main.Verbose($"[Buff] rank of {feature.name} on {ally.CharacterName}: {ex.Message}"); }
                    }
                }
            }
        }

        private static void AddIncrements(TacticalContext ctx, BlueprintBuff owner, BlueprintFeature feature, int rank,
                                          UnitEntityData ally, List<BuffPart> parts)
        {
            foreach (var fact in ally.Facts.List)
            {
                if (fact?.Blueprint?.ComponentsArray == null || !fact.Active) continue;
                var blueprint = fact.Blueprint as BlueprintScriptableObject;
                foreach (var component in fact.Blueprint.ComponentsArray)
                {
                    switch (component)
                    {
                        // ShroudOfWater.OnTurnOn: min(floor(base x 1.5), base + upgrade rank), on its stat with its descriptor.
                        case ShroudOfWater shroud when shroud.UpgradeFeature == feature && shroud.Stat == StatType.AC:
                        {
                            var context = fact.MaybeContext;
                            if (context == null) break;
                            int baseValue = shroud.BaseValue.Calculate(context);
                            int cap = (int)Math.Floor(baseValue * 1.5);
                            int now = Math.Min(cap, baseValue + rank), next = Math.Min(cap, baseValue + rank + 1);
                            if (next > now)
                                parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = next, Descriptor = shroud.Descriptor });
                            break;
                        }
                        case SetAttackerMissChance miss when blueprint != null:
                        {
                            var configs = blueprint.GetComponents<ContextRankConfig>().Where(c => ReadsRankOf(c, feature)).ToList();
                            if (configs.Count == 0) break;
                            var context = new MechanicsContext(ally, ally.Descriptor, blueprint);
                            context.Recalculate();
                            int now = miss.Value.Calculate(context);
                            foreach (var config in configs)
                            {
                                int value = (int)RankApplyProgression.Invoke(config, new object[] { rank + 1 });
                                context[config.Type] = (int)RankApplyMinMax.Invoke(config, new object[] { value, false, context });
                            }
                            context.RecalculateSharedValues();
                            int next = Math.Min(100, miss.Value.Calculate(context));
                            if (next <= now) break;
                            float share = AttackTypeShare(ctx, ally, MissType?.GetValue(miss) is SetAttackerMissChance.Type t ? t : SetAttackerMissChance.Type.All);
                            if (share > 0f)
                                parts.Add(new BuffPart { Kind = BuffPartKind.MissChance, Owner = owner, Fixed = next - now, Chance = share });
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The share of what comes at <paramref name="ally"/> that attacks of <paramref name="types"/> make: weapon attacks
        /// by the attacker's weapon in hand (ranged or melee), weighed by each enemy's part of the danger (IncomingParts, or
        /// the enemies' threat before they close in). Touch and ray attacks are not told apart from the caster's other
        /// damage, so they count for nothing here.
        /// </summary>
        private static float AttackTypeShare(TacticalContext ctx, UnitEntityData ally, SetAttackerMissChance.Type types)
        {
            bool ranged = (types & SetAttackerMissChance.Type.Ranged) != 0, melee = (types & SetAttackerMissChance.Type.Melee) != 0;
            if (!ranged && !melee) return 0f;
            var exposure = IncomingParts(ctx, ally, ally.Position);
            if (exposure.Count == 0)
                exposure = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e))
                                      .Select(e => new KeyValuePair<UnitEntityData, float>(e, CombatMath.Threat(e))).ToList();
            float all = 0f, covered = 0f;
            foreach (var part in exposure)
            {
                if (part.Key == null || !(part.Value > 0f)) continue;
                all += part.Value;
                var weapon = part.Key.Body?.PrimaryHand?.MaybeWeapon;
                bool isRanged = weapon?.Blueprint?.IsRanged ?? false;
                if (isRanged ? ranged : melee) covered += part.Value;
            }
            return all > 0f ? covered / all : 0f;
        }
    }
}
