// ★ C07: native Magus contracts. Metadata is weakly cached; no context, actor or command survives a decision.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Root;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Parts;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class MagusMechanics
    {
        internal enum Mode { None, Combat, Strike }
        private static readonly FieldInfo ModeField = AccessTools.Field(typeof(SetMagusFeatureActive), "m_Feature");
        private static readonly FieldInfo GripField = AccessTools.Field(typeof(HandsEquipmentSet), "m_GripType");
        private sealed class ModeInfo { internal Mode Value; }
        private static readonly ConditionalWeakTable<BlueprintActivatableAbility, ModeInfo> Modes = new ConditionalWeakTable<BlueprintActivatableAbility, ModeInfo>();
        internal static Mode Kind(BlueprintActivatableAbility blueprint) => blueprint == null ? Mode.None : Modes.GetValue(blueprint, ReadMode).Value;
        private static ModeInfo ReadMode(BlueprintActivatableAbility bp)
        {
            var result = new ModeInfo();
            if (ModeField == null || bp.Buff?.ComponentsArray == null || bp.Buff.ComponentsArray.Length != 1 ||
                !(bp.Buff.ComponentsArray[0] is SetMagusFeatureActive feature) || feature.GetType() != typeof(SetMagusFeatureActive) ||
                bp.ActivateWithUnitCommand || bp.IsTargeted || bp.HiddenInUI || bp.Group != ActivatableAbilityGroup.None ||
                bp.GetComponent<ActivatableAbilityResourceLogic>() != null) return result;
            // Private native enum names, not localized ability names or class/GUID guesses.
            string value = ModeField.GetValue(feature)?.ToString();
            result.Value = value == "SpellCombat" ? Mode.Combat : value == "Spellstrike" ? Mode.Strike : Mode.None;
            return result;
        }

        internal static AbilityData Held(UnitEntityData unit) => unit?.Get<UnitPartTouch>()?.Ability?.Data;
        internal static bool PlainTouch(AbilityData ability, AbilityProfile profile) => ability != null && profile != null &&
            profile.Fusion == null && !profile.IsAoE && profile.MeleeAttack == null && !profile.IsWeaponAttack && !profile.Summons &&
            SpellDelivery.IsPhysicalTouch(ability) && SpellDelivery.IsTouchAttack(ability);

        // Only read-only scalar state is previewed. The native feature setter is just an unlocked boolean; native grip
        // preference reset is one field. Never toggle an ability, apply a buff or raise equipment events while pricing.
        internal sealed class Preview : IDisposable
        {
            private readonly UnitPartMagus part;
            private readonly bool combat, strike;
            private readonly HandsEquipmentSet hands;
            private readonly object grip;
            private bool disposed;
            internal Preview(UnitPartMagus part, bool combat, bool strike, HandsEquipmentSet hands)
            {
                this.part = part; this.combat = part.SpellCombat.Active; this.strike = part.Spellstrike.Active;
                this.hands = hands;
                if (hands != null && GripField != null) grip = GripField.GetValue(hands);
                try
                {
                    if (combat && !this.combat && hands != null && hands.GripType == GripType.TwoHanded)
                    {
                        if (GripField == null) throw new InvalidOperationException("native grip binding unavailable");
                        GripField.SetValue(hands, Enum.ToObject(GripField.FieldType, 0));
                    }
                    part.SpellCombat.Active = combat;
                    part.Spellstrike.Active = strike;
                    TacticalEngine.MagusStateChanged();
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    part.SpellCombat.Active = combat;
                    part.Spellstrike.Active = strike;
                    if (grip != null) GripField.SetValue(hands, grip);
                }
                finally { TacticalEngine.MagusStateChanged(); }
            }
        }

        private sealed class Guard { internal BlueprintUnitFact Fact; internal bool Present; }
        private sealed class Penalty { internal BlueprintBuff Buff; internal AttackTypeAttackBonus Part; internal Guard[] Guards; }
        private sealed class CombatInfo { internal bool Known; internal readonly List<Penalty> Parts = new List<Penalty>(); }
        private static readonly ConditionalWeakTable<BlueprintBuff, CombatInfo> Combat = new ConditionalWeakTable<BlueprintBuff, CombatInfo>();
        private static CombatInfo ReadCombat(BlueprintBuff root)
        {
            var result = new CombatInfo(); var path = new HashSet<BlueprintBuff>(); int budget = 32;
            var applied = new HashSet<BlueprintBuff>(); var removed = new List<ContextActionRemoveBuff>();
            bool Buff(BlueprintBuff buff, List<Guard> guards, int depth)
            {
                if (buff?.ComponentsArray == null || buff.Stacking != StackingType.Replace || depth > 4 || --budget < 0 || !path.Add(buff)) return false;
                try
                {
                    foreach (var c in buff.ComponentsArray)
                    {
                        if (c is AttackTypeAttackBonus bonus && c.GetType() == typeof(AttackTypeAttackBonus) &&
                            bonus.Value?.ValueType == ContextValueType.Simple && !bonus.CheckFact &&
                            bonus.Descriptor == ModifierDescriptor.UntypedStackable)
                            result.Parts.Add(new Penalty { Buff = buff, Part = bonus, Guards = guards.ToArray() });
                        else if (c is AddFactContextActions facts && (facts.NewRound?.Actions?.Length ?? 0) == 0 &&
                                 Actions(facts.Activated?.Actions, guards, depth) &&
                                 (facts.Deactivated?.Actions == null || facts.Deactivated.Actions.All(a => a is ContextActionRemoveBuff)))
                        { if (facts.Deactivated?.Actions != null) removed.AddRange(facts.Deactivated.Actions.Cast<ContextActionRemoveBuff>()); }
                        else return false;
                    }
                    return true;
                }
                finally { path.Remove(buff); }
            }
            bool Actions(GameAction[] actions, List<Guard> guards, int depth)
            {
                if (actions == null) return true;
                foreach (var a in actions)
                {
                    if (--budget < 0) return false;
                    if (a is ContextActionApplyBuff apply && apply.Permanent && apply.AsChild && !apply.ToCaster && !apply.SameDuration)
                    { applied.Add(apply.Buff); if (!Buff(apply.Buff, guards, depth + 1)) return false; }
                    else if (a is Conditional conditional && conditional.ConditionsChecker?.Conditions?.Length == 1 &&
                             conditional.ConditionsChecker.Conditions[0] is ContextConditionHasFact fact &&
                             fact.GetType() == typeof(ContextConditionHasFact) && fact.Fact != null)
                    {
                        var yes = new List<Guard>(guards) { new Guard { Fact = fact.Fact, Present = !fact.Not } };
                        var no = new List<Guard>(guards) { new Guard { Fact = fact.Fact, Present = fact.Not } };
                        if (!Actions(conditional.IfTrue?.Actions, yes, depth + 1) || !Actions(conditional.IfFalse?.Actions, no, depth + 1)) return false;
                    }
                    else return false;
                }
                return true;
            }
            result.Known = Buff(root, new List<Guard>(), 0) && result.Parts.Count > 0 &&
                removed.All(r => r.Buff != null && !r.ToCaster && applied.Contains(r.Buff));
            return result;
        }
        internal static bool CombatPenalty(UnitEntityData unit, BlueprintBuff root, ItemEntityWeapon weapon, out int penalty)
        {
            penalty = 0;
            if (root == null || weapon?.Blueprint == null) return false;
            var info = Combat.GetValue(root, ReadCombat);
            if (!info.Known) return false;
            foreach (var part in info.Parts)
            {
                if (part.Guards.Any(g => unit.Descriptor.HasFact(g.Fact) != g.Present)) continue;
                // A native already-held child contributes through the real calculation rule, once.
                var held = unit.Buffs.GetBuff(part.Buff);
                if (held?.Active == true) continue;
                if (part.Part.Type.IsSuitableWeapon(weapon.Blueprint) != part.Part.AllTypesExcept)
                    penalty += part.Part.AttackBonus * part.Part.Value.Value;
            }
            return true;
        }
        internal static bool StrikeExtra(BlueprintBuff root, out int extra)
        {
            extra = 0;
            if (root?.Stacking != StackingType.Replace || root.ComponentsArray?.Length != 1 || !(root.ComponentsArray[0] is BuffExtraAttack part) ||
                part.GetType() != typeof(BuffExtraAttack) || part.Haste || part.Penalized || part.Number < 0 || part.Number > 4) return false;
            extra = part.Number;
            return true;
        }
    }
}
