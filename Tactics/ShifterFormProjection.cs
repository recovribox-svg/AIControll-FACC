// ★ v0.3.30: Shifter future-body scalar inputs; no actor state is changed.
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>A numerical projection, not an activated form, temporary unit or equipment change. Unknown
    /// additional form facts receive no positive credit. The normal planner re-reads all native facts after casting.</summary>
    internal sealed class ShifterFormProjection
    {
        internal readonly UnitEntityData Unit;
        internal readonly Polymorph Form;
        private readonly HashSet<BlueprintBuff> suppressed = new HashSet<BlueprintBuff>();
        private readonly ItemEntityArmor armor;
        private readonly ItemEntityShield shield;
        private readonly Dictionary<StatType, int> changes = new Dictionary<StatType, int>();
        internal int DerivedAttackLoss, DerivedDamageLoss, DerivedArmorLoss;
        internal float SaveLoss, HpLoss;
        private static readonly System.Reflection.FieldInfo SuppressedBuffs =
            HarmonyLib.AccessTools.Field(typeof(SuppressBuffs), "m_Buffs");

        internal static bool Supports(Polymorph form)
        {
            if (form == null || !form.AllowDamageTransfer || form.KeepSlots || form.MainHand != null || form.OffHand != null)
                return false;
            int primary = 0, secondary = 0;
            foreach (var weapon in form.AdditionalLimbs)
            {
                if (weapon == null || !weapon.IsNatural || weapon.IsUnarmed) return false;
                primary++;
            }
            foreach (var weapon in form.SecondaryAdditionalLimbs)
            {
                if (weapon == null || !weapon.IsNatural || weapon.IsUnarmed) return false;
                secondary++;
            }
            return primary > 0 && primary + secondary <= 8;
        }

        internal ShifterFormProjection(UnitEntityData unit, BlueprintBuff buff, Polymorph form)
        {
            Unit = unit; Form = form;
            armor = unit.Body.Armor?.MaybeArmor;
            shield = unit.Body.SecondaryHand?.MaybeShield;
            foreach (var component in buff.GetComponents<SuppressBuffs>())
            {
                // The exported Shifter forms suppress explicit buffs (notably Enlarge Person). Other suppression
                // contracts cannot be priced by this scalar projection; do not retain a vanished bonus silently.
                if (component.Descriptor.Value != SpellDescriptor.None || (component.Schools?.Length ?? 0) > 0)
                    throw new NotSupportedException("form suppresses a school or descriptor");
                if (SuppressedBuffs?.GetValue(component) is BlueprintBuffReference[] refs)
                    foreach (var reference in refs) if (reference?.Get() is BlueprintBuff b) suppressed.Add(b);
            }
        }

        private bool Lost(ModifiableValue.Modifier modifier)
        {
            if (modifier.Source?.Blueprint is BlueprintBuff buff && suppressed.Contains(buff)) return true;
            var item = modifier.ItemSource ?? (modifier.Source as ItemEnchantment)?.Owner;
            return item != null && (item == armor || item == shield || item is ItemEntityArmor a && a.Shield == shield && shield != null);
        }

        internal int LostBonus(StatType type)
        {
            int lost = 0;
            // Discard only positive lost modifiers. Keeping a removed penalty underestimates the new form.
            foreach (var modifier in Unit.Stats.GetStat(type).Modifiers)
                if (modifier.ModValue > 0 && Lost(modifier)) lost += modifier.ModValue;
            return lost;
        }

        private int Added(StatType type, int amount, ModifierDescriptor descriptor)
        {
            if (amount <= 0) return amount;
            if (ModifierDescriptorHelper.IsStackable(descriptor)) return amount;
            int existing = 0;
            foreach (var modifier in Unit.Stats.GetStat(type).Modifiers)
                if (!Lost(modifier) && modifier.ModDescriptor == descriptor) existing = Math.Max(existing, modifier.ModValue);
            return Math.Max(0, amount - existing);
        }

        internal int ScoreChange(StatType type)
        {
            if (changes.TryGetValue(type, out int change)) return change;
            int amount = type == StatType.Strength ? Form.StrengthBonus : type == StatType.Dexterity ? Form.DexterityBonus :
                type == StatType.Constitution ? Form.ConstitutionBonus : 0;
            change = -LostBonus(type);
            if (type == StatType.Strength || type == StatType.Dexterity || type == StatType.Constitution)
            {
                // Powerful Change's possible extra positive bonus is deliberately uncredited: it needs the applied
                // fact's runtime. Master Shapeshifter is an explicit native feature with an inspected +4 descriptor.
                change += Added(type, amount, ModifierDescriptor.Polymorph);
                if (Unit.State.Features.MasterShapeshifter)
                    change += Added(type, 4, ModifierDescriptor.MasterShapeshifter);
            }
            changes[type] = change;
            return change;
        }

        internal int ModifierChange(StatType type)
        {
            if (!(Unit.Stats.GetStat(type) is ModifiableValueAttributeStat stat)) return 0;
            int future = Math.Max(1, stat.ModifiedValue + ScoreChange(type));
            return (int)Math.Floor((future - 10) / 2d) - stat.Bonus;
        }

        internal int SizeChange => Form.Size.GetModifiers().AttackAndAC - Unit.State.Size.GetModifiers().AttackAndAC;
        internal int AttackChange(StatType type) => ModifierChange(type) + SizeChange - LostBonus(StatType.AdditionalAttackBonus) - DerivedAttackLoss;
        internal int ArmorChange => Added(StatType.AC, Form.NaturalArmor, ModifierDescriptor.NaturalArmorForm) + SizeChange -
            LostBonus(StatType.AC) + Math.Min(0, ModifierChange(StatType.Dexterity)) - DerivedArmorLoss; // no speculative uncapping/unarmored benefit
    }
}
