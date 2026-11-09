// ★ v0.3.30 competence: burn has the consequences the active burn-change feature applies. It is not inherently HP
// damage: the native handler can instead replace a burn-ranked saving-throw/skill penalty buff.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Designers.Mechanics.Facts.Restrictions;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Class.Kineticist.Properties;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static readonly FieldInfo BurnRankProperty = HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_CustomProperty");
        private static readonly FieldInfo BurnRankIntensified = HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_AffectedByIntensifiedMetamagic");
        private static readonly FieldInfo BurnStatRestrictions = HarmonyLib.AccessTools.Field(typeof(AddContextStatBonus), "Restrictions");

        /// <summary>Decision-local immutable description. No live context, buff mutation or native action is retained.</summary>
        internal sealed class BurnConsequenceModel
        {
            internal bool Modeled;
            internal string Unknown;
            internal int Accepted, ClassLevel, CharacterLevel;
            internal readonly List<BurnStatConsequence> Stats = new List<BurnStatConsequence>();
            /// <summary>★ v0.3.30 competence2: burn-ranked buffs that only give (Overflow fortification, Adaptation
            /// resistance). Read and set aside: they are not a cost, and the existing Overflow credit is separate.</summary>
            internal readonly List<BlueprintBuff> Benefits = new List<BlueprintBuff>();
            // Only requested burn alternatives, owned by one decision; no actor/global key or context survives it.
            internal readonly Dictionary<int, BurnDelta> Deltas = new Dictionary<int, BurnDelta>();
        }

        internal struct BurnDelta { internal int Nonlethal; internal float Saves; }

        internal sealed class BurnStatConsequence
        {
            internal StatType Stat;
            internal int Multiplier;
            internal ContextRankConfig Rank;
            internal KineticistBurnPropertyGetter Burn;
            internal bool FlooredComposite;
            internal int FeatureAdjustment;
            internal BlueprintBuff Buff;
            internal int? CurrentAmount;
        }

        /// <summary>
        /// Restrict recognition to the actual direct feature -> permanent replacement buff -> burn-property stat shape.
        /// Conditional actions, arbitrary properties/components, stacking and non-feature caster contexts remain unknown;
        /// a successfully read different consequence is never inferred from an archetype name, GUID or main stat.
        /// </summary>
        internal static BurnConsequenceModel ReadBurnConsequences(IEnumerable<BlueprintScriptableObject> activeFacts,
            int accepted, int classLevel, int characterLevel)
            => ReadBurnConsequencesWithOwnership(activeFacts, accepted, classLevel, characterLevel, null);

        internal static BurnConsequenceModel ReadBurnConsequencesWithOwnership(IEnumerable<BlueprintScriptableObject> activeFacts,
            int accepted, int classLevel, int characterLevel, Func<BlueprintFeatureBase, bool> ownsFeature)
        {
            var model = new BurnConsequenceModel { Accepted = accepted, ClassLevel = classLevel, CharacterLevel = characterLevel };
            try
            {
                if (RankApplyProgression == null || RankApplyMinMax == null || RankConfigBaseType == null ||
                    BurnRankProperty == null || BurnRankIntensified == null || BurnStatRestrictions == null)
                    return UnknownBurn(model, "native burn rank bindings unavailable");
                int triggers = 0;
                var seen = new HashSet<BlueprintBuff>();
                foreach (var source in activeFacts)
                foreach (var trigger in source.GetComponents<AddKineticistBurnValueChangedTrigger>())
                {
                    triggers++;
                    if (!(source is BlueprintFeature) || trigger.Action?.Actions == null || trigger.Action.Actions.Length == 0)
                        return UnknownBurn(model, $"burn trigger has no supported feature action graph: {source.name}");
                    foreach (var action in trigger.Action.Actions)
                    {
                        if (!(action is ContextActionApplyBuff apply) || !apply.Permanent || apply.SameDuration || apply.Buff == null ||
                            apply.Buff.Stacking != StackingType.Replace)
                            return UnknownBurn(model, $"burn trigger is not a direct permanent replacement buff: {source.name}");
                        var buff = apply.Buff;
                        if (!seen.Add(buff)) continue; // Reapplying the same replacement buff does not add another modifier.
                        // ★ v0.3.30 competence2 (PR #20 review): an actor's kit carries more burn-change triggers than
                        // its burn feature - from class level 6 Elemental Overflow's fortification/stat-choice buff, and
                        // the Adaptation talents' energy resistance. A buff with no stat modifier is a cost only if it is
                        // not one of the listed benefit shapes; it must not make the whole actor's burn price unknown.
                        if (!buff.ComponentsArray.Any(c => c is AddContextStatBonus))
                        {
                            if (!BurnBenefitOnly(buff)) return UnknownBurn(model, $"burn buff has an unsupported consequence component: {buff.name} from {source.name}");
                            model.Benefits.Add(buff);
                            continue;
                        }
                        var configs = buff.GetComponents<ContextRankConfig>().ToList();
                        int statCount = 0;
                        foreach (var component in buff.ComponentsArray)
                        {
                            if (component is ContextRankConfig) continue;
                            if (!(component is AddContextStatBonus stat) || stat.Value == null ||
                                stat.Value.ValueType != ContextValueType.Rank || stat.HasMinimal || stat.Descriptor != ModifierDescriptor.UntypedStackable ||
                                !SupportedBurnStat(stat.Stat))
                                return UnknownBurn(model, $"burn buff has an unsupported consequence component: {buff.name} from {source.name}");
                            var restrictions = BurnStatRestrictions.GetValue(stat) as RestrictionCalculator;
                            if (restrictions?.Property != null && (restrictions.Property.Getters == null || restrictions.Property.Getters.Length != 0))
                                return UnknownBurn(model, $"burn stat has a conditional restriction: {buff.name} from {source.name}");
                            var ranks = configs.Where(c => c.Type == stat.Value.ValueRank).ToList();
                            if (ranks.Count != 1 || !(RankConfigBaseType.GetValue(ranks[0]) is ContextRankBaseValueType baseType) ||
                                baseType != ContextRankBaseValueType.CustomProperty || (bool)BurnRankIntensified.GetValue(ranks[0]))
                                return UnknownBurn(model, $"burn stat rank is not an unmodified burn custom property: {buff.name} from {source.name}");
                            var property = (BurnRankProperty.GetValue(ranks[0]) as BlueprintUnitPropertyReference)?.Get();
                            var getters = property?.ComponentsArray;
                            KineticistBurnPropertyGetter burn = null;
                            bool composite = false;
                            int adjustment = 0;
                            if (property == null || property.BaseValue != 0 || property.OperationOnComponents != BlueprintUnitProperty.MathOperation.Sum ||
                                getters == null || getters.Length != 1 || !(getters[0] is PropertyValueGetter valueGetter) ||
                                !ModdedBurnProperties.TryRead(valueGetter, ownsFeature, out burn, out composite, out adjustment))
                                // ★ v0.3.30 competence3: name what was read - the competence2 logs said only "unsupported arithmetic"
                                // for Orvath, and the native Dark Elementalist kit reads fine offline (ProfileAudit BURN kits).
                                return UnknownBurn(model, "burn property has additional or unsupported arithmetic: " +
                                    $"{buff.name} ranks by {property?.name ?? "no property"} (base {property?.BaseValue}, " +
                                    $"{property?.OperationOnComponents}, getters " +
                                    $"{(getters == null ? "none" : string.Join("+", getters.Select(g => g?.GetType().Name ?? "null")))}" +
                                    $"{(getters != null && getters.Length == 1 && getters[0] is KineticistBurnPropertyGetter b && b.Settings != null ? ", with settings" : "")})" +
                                    $" from {source.name}");
                            model.Stats.Add(new BurnStatConsequence { Stat = stat.Stat, Multiplier = stat.Multiplier, Rank = ranks[0], Burn = burn,
                                Buff = buff, FlooredComposite = composite, FeatureAdjustment = adjustment });
                            statCount++;
                        }
                        if (statCount == 0) return UnknownBurn(model, $"burn buff contains no modeled stat consequence: {buff.name} from {source.name}");
                    }
                }
                if (triggers == 0) return UnknownBurn(model, "no supported active burn-change trigger");
                // Only benefit triggers (a part whose burn feature is not one of these shapes): the cost is not known.
                if (model.Stats.Count == 0) return UnknownBurn(model, "no supported burn consequence buff");
                model.Modeled = true;
            }
            catch (Exception ex) { return UnknownBurn(model, "burn consequence query failed: " + ex.GetType().Name); }
            return model;
        }

        private static BurnConsequenceModel UnknownBurn(BurnConsequenceModel model, string reason)
        {
            model.Modeled = false;
            model.Unknown = reason;
            model.Stats.Clear();
            model.Benefits.Clear();
            return model;
        }

        /// <summary>
        /// ★ v0.3.30 competence2: a burn-ranked buff that only gives - fortification, energy resistance, or fact actions
        /// that set shared values and apply (or take back) buffs that are not harmful by what they do. Fail-closed: any
        /// other component or action keeps the burn price unknown. Nothing is evaluated or applied here.
        /// </summary>
        private static bool BurnBenefitOnly(BlueprintBuff buff)
        {
            if (buff.ComponentsArray == null || AbilityProfile.IsHarmfulBuff(buff)) return false;
            foreach (var component in buff.ComponentsArray)
            {
                switch (component)
                {
                    case ContextRankConfig _:
                    case AddDamageResistanceEnergy _:
                        continue;
                    case AddFortification fortification when fortification.UseContextValue || fortification.Bonus >= 0:
                        continue;
                    case AddFactContextActions actions:
                        var applied = new HashSet<BlueprintBuff>();
                        if (BurnBenefitActions(actions.Activated?.Actions, applied, 0) &&
                            BurnBenefitActions(actions.Deactivated?.Actions, applied, 0) &&
                            BurnBenefitActions(actions.NewRound?.Actions, applied, 0) &&
                            BurnRemovesOnlyOwn(actions, applied))
                            continue;
                        return false;
                    default:
                        return false;
                }
            }
            return true;
        }

        private static bool BurnBenefitActions(Kingmaker.ElementsSystem.GameAction[] actions, HashSet<BlueprintBuff> applied, int depth)
        {
            if (actions == null) return true;
            if (depth > 4) return false;
            foreach (var action in actions)
            {
                switch (action)
                {
                    case ContextActionChangeSharedValue _:
                    case ContextActionRemoveBuff _:   // checked against what this graph applies (BurnRemovesOnlyOwn)
                        break;
                    case ContextActionApplyBuff apply when apply.Buff != null && !apply.ToCaster && !AbilityProfile.IsHarmfulBuff(apply.Buff):
                        applied.Add(apply.Buff);
                        break;
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional
                        when BurnBenefitActions(conditional.IfTrue?.Actions, applied, depth + 1) &&
                             BurnBenefitActions(conditional.IfFalse?.Actions, applied, depth + 1):
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        /// <summary>A removal only takes back a buff the same graph gives (Overflow swaps its level-6/11/16 stat buffs).</summary>
        private static bool BurnRemovesOnlyOwn(AddFactContextActions actions, HashSet<BlueprintBuff> applied)
        {
            bool Own(Kingmaker.ElementsSystem.GameAction[] list, int depth)
            {
                if (list == null) return true;
                if (depth > 4) return false;
                foreach (var action in list)
                {
                    if (action is ContextActionRemoveBuff remove && (remove.ToCaster || remove.Buff == null || !applied.Contains(remove.Buff)))
                        return false;
                    if (action is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional &&
                        (!Own(conditional.IfTrue?.Actions, depth + 1) || !Own(conditional.IfFalse?.Actions, depth + 1)))
                        return false;
                }
                return true;
            }
            return Own(actions.Activated?.Actions, 0) && Own(actions.Deactivated?.Actions, 0) && Own(actions.NewRound?.Actions, 0);
        }

        private static bool SupportedBurnStat(StatType stat)
        {
            switch (stat)
            {
                case StatType.DamageNonLethal:
                case StatType.SaveFortitude: case StatType.SaveReflex: case StatType.SaveWill:
                // These are inspected, but the existing combat model assigns no tactical utility to skill modifiers.
                case StatType.SkillAthletics: case StatType.SkillMobility: case StatType.SkillThievery:
                case StatType.SkillStealth: case StatType.SkillKnowledgeArcana: case StatType.SkillKnowledgeWorld:
                case StatType.SkillLoreNature: case StatType.SkillLoreReligion: case StatType.SkillPerception:
                case StatType.SkillPersuasion: case StatType.SkillUseMagicDevice: return true;
                default: return false;
            }
        }

        private static bool CostedBurnStat(StatType stat) => stat == StatType.DamageNonLethal || stat == StatType.SaveFortitude ||
            stat == StatType.SaveReflex || stat == StatType.SaveWill;

        /// <summary>Marginal stat changes, evaluating native pure rank progression/caps at both accepted-burn totals.</summary>
        internal static bool BurnConsequenceDelta(BurnConsequenceModel model, int addedBurn, out int nonlethal, out float weightedSaves)
        {
            nonlethal = 0;
            weightedSaves = 0f;
            if (model?.Modeled != true) return false;
            if (addedBurn <= 0) return true;
            if (model.Deltas.TryGetValue(addedBurn, out var cached))
            { nonlethal = cached.Nonlethal; weightedSaves = cached.Saves; return true; }
            try
            {
                foreach (var stat in model.Stats)
                {
                    if (!CostedBurnStat(stat.Stat)) continue; // Skill utility is explicitly outside the combat appraisal.
                    int scale = checked((stat.Burn.MultiplyOnClassLevel ? model.ClassLevel : 1) *
                                        (stat.Burn.MultyplyOnCharacterLevel ? model.CharacterLevel : 1));
                    int now = checked(model.Accepted * scale), next = checked((model.Accepted + Math.Max(0, addedBurn)) * scale);
                    if (stat.FlooredComposite)
                    {
                        now = ValueModel.TomeBurnValue(model.Accepted, scale, stat.FeatureAdjustment);
                        next = ValueModel.TomeBurnValue(checked(model.Accepted + addedBurn), scale, stat.FeatureAdjustment);
                    }
                    now = (int)RankApplyProgression.Invoke(stat.Rank, new object[] { now });
                    next = (int)RankApplyProgression.Invoke(stat.Rank, new object[] { next });
                    // Intensified-dependent configs were excluded. These pure caps do not consult a context.
                    now = (int)RankApplyMinMax.Invoke(stat.Rank, new object[] { now, false, null });
                    next = (int)RankApplyMinMax.Invoke(stat.Rank, new object[] { next, false, null });
                    int delta = checked(next * stat.Multiplier - (stat.CurrentAmount ?? now * stat.Multiplier));
                    if (stat.Stat == StatType.DamageNonLethal) nonlethal = checked(nonlethal + delta);
                    else if (stat.Stat == StatType.SaveFortitude || stat.Stat == StatType.SaveReflex || stat.Stat == StatType.SaveWill)
                        weightedSaves += delta / 3f; // Existing recipient appraisal: one save is a third of all saves.
                }
                model.Deltas[addedBurn] = new BurnDelta { Nonlethal = nonlethal, Saves = weightedSaves };
                return true;
            }
            catch { nonlethal = 0; weightedSaves = 0f; return false; }
        }

        private static BurnConsequenceModel BurnConsequencesFor(TacticalContext ctx)
        {
            if (ctx.BurnConsequences != null) return ctx.BurnConsequences;
            var unit = ctx.Unit;
            var part = unit.Get<UnitPartKineticist>();
            var facts = unit.Facts.List.Where(f => f != null && f.Active).Select(f => f.Blueprint).OfType<BlueprintScriptableObject>();
            var model = ReadBurnConsequencesWithOwnership(facts, part?.AcceptedBurn ?? 0, part?.ClassLevel ?? 0, Level(unit),
                feature => unit.Progression.Features.HasFact(feature));
            if (model.Modeled)
            {
                // The next burn replaces the buff with its new total. Compare with what is actually held now: a
                // missing/externally removed buff can make the next application larger than a steady-state rank delta.
                // Snapshot only its existing rank number; do not evaluate arbitrary values or retain the live context.
                try
                {
                    foreach (var stat in model.Stats)
                    {
                        if (!CostedBurnStat(stat.Stat)) continue;
                        var held = unit.Buffs.GetBuff(stat.Buff);
                        if (held == null || !held.Active) stat.CurrentAmount = 0;
                        else if (held.MaybeContext == null) return ctx.BurnConsequences = UnknownBurn(model, "held burn buff has no readable rank context");
                        else stat.CurrentAmount = checked(held.MaybeContext[stat.Rank.Type] * stat.Multiplier);
                    }
                }
                catch (Exception ex) { return ctx.BurnConsequences = UnknownBurn(model, "held burn buff query failed: " + ex.GetType().Name); }
            }
            return ctx.BurnConsequences = model;
        }
    }
}
