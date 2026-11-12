// ★ v0.3.30: native natural-weapon/form/Fury rule-result projections.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.WeaponEnchants;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.FactLogic;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        private sealed class ShifterStats : RuleCalculateWeaponStats
        {
            private readonly bool? secondary;
            private readonly bool transfer;
            private readonly ShifterFormProjection form;
            private readonly bool soleWeapon;
            private readonly List<EntityFact> transfers;
            internal ShifterStats(UnitEntityData unit, ItemEntityWeapon weapon, bool? secondary, bool transfer,
                List<EntityFact> transfers, ShifterFormProjection form = null, bool soleWeapon = false) : base(unit, weapon)
            { this.secondary = secondary; this.transfer = transfer; this.transfers = transfers; this.form = form; this.soleWeapon = soleWeapon; }

            public override void OnTrigger(RulebookEventContext context)
            {
                // Change this read-only rule, after AboutToTrigger. No buff data/actor/body state is changed.
                if (secondary.HasValue) IsSecondaryOverride = secondary;
                if (transfers.Count > 0)
                {
                    WeaponDamageDice.Modifications?.RemoveAll(m => IsTransfer(m.Source));
                    var critical = AdditionalCriticalMultiplier.Modifiers.Where(m => !IsTransfer(m.Fact)).ToArray();
                    AdditionalCriticalMultiplier.Clear();
                    foreach (var modifier in critical) AdditionalCriticalMultiplier.Add(modifier);
                }
                if (transfer) TransferShifterDice(this, transfers);
                if (form != null)
                {
                    // Native polymorph replaces size absolutely. UseSizeAsBaseForDamage also changes the dice base:
                    // the exported Large griffon's claw is already its Large dice, not a Medium claw enlarged again.
                    IncreaseWeaponSize(ShifterDiceShift(form.Form, Weapon.Size));
                    // RuleCalculateWeaponSizeBonus skips an unmodified blueprint dice override on a polymorphed
                    // real actor (the game's DefaultUnit inspection dummy is the exception). Our actor is still human.
                    if (SkipFormScaling(Weapon.Blueprint, WeaponDamageDice.HasModifications)) DoNotScaleDamage = true;
                    OverrideDamageBonus = secondary == true ? 0.5f : soleWeapon ? 1.5f : 1f;
                }
                base.OnTrigger(context);
                if (form != null && DamageDescription.Count > 0)
                {
                    int delta = -form.LostBonus(StatType.AdditionalDamage) - form.DerivedDamageLoss;
                    if (DamageBonusStat.HasValue && Initiator.Stats.GetStat(DamageBonusStat.Value) is ModifiableValueAttributeStat stat)
                    {
                        int future = stat.Bonus + form.ModifierChange(DamageBonusStat.Value);
                        float multiplier = DamageBonusStatMultiplier + AdditionalDamageBonusStatMultiplier;
                        delta += ShifterRules.AttributeDamage(future, multiplier) - ShifterRules.AttributeDamage(stat.Bonus, multiplier);
                    }
                    DamageDescription[0].AddModifier(new Modifier(delta, ModifierDescriptor.UntypedStackable));
                }
            }
        }

        private static bool IsTransfer(EntityFact fact) => fact?.Blueprint?.GetComponent<PolymorphDamageTransfer>() != null;

        private static int ShifterDiceShift(Polymorph form, Size weaponSize) =>
            (int)(form.UseSizeAsBaseForDamage ? Size.Medium : form.Size) - (int)weaponSize;

        private static bool SkipFormScaling(BlueprintItemWeapon weapon, bool modified) => weapon.IsDamageDiceOverridden && !modified;

        private static bool? FurySecondary(BlueprintItemWeapon weapon, bool selected) =>
            weapon.IsUnarmed ? (bool?)null : !selected;

        private static void TransferShifterDice(RuleCalculateWeaponStats stats, List<EntityFact> transfers)
        {
            foreach (var fact in transfers)
            {
                var source = fact.Blueprint.GetComponent<EmptyHandWeaponOverride>()?.Weapon;
                if (source == null) continue;
                if (source.Damage.IsBetterThan(stats.WeaponDamageDice.ModifiedValue)) stats.WeaponDamageDice.Modify(source.Damage, fact);
                foreach (var enchantment in source.Enchantments)
                {
                    var critical = enchantment.GetComponent<WeaponCriticalMultiplierIncrease>();
                    if (critical == null) continue;
                    stats.AdditionalCriticalMultiplier.Add(new Modifier(critical.AdditionalMultiplier, fact));
                    break;
                }
            }
        }

        private static float ShifterHit(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon,
            int penalty, bool? secondary, bool transfer, List<EntityFact> transfers, ShifterFormProjection form = null, bool sole = false)
        {
            var stats = Rulebook.Trigger<RuleCalculateWeaponStats>(new ShifterStats(unit, weapon, secondary, transfer, transfers, form, sole));
            var attack = Rulebook.Trigger(new RuleCalculateAttackBonus(unit, target, stats, penalty) { IgnoreRangedPenalty = true });
            int ab = attack.Result + (form?.AttackChange(attack.AttackBonusStat) ?? 0);
            int ac = Rulebook.Trigger(new RuleCalculateAC(unit, target, weapon.Blueprint.AttackType)).Result;
            // Projected variants must neither consume nor overwrite a real weapon's ordinary hit memo.
            var hit = ReadHitOnTarget(unit, target, weapon, stats, cache: false);
            if (!hit.Read) throw new InvalidOperationException("projected natural hit unavailable");
            float p = HitProbability(ab, ac) * LandsPastDefences(unit, target);
            float threat = hit.CritImmune ? 0f : stats.CriticalRange / 20f;
            return p * hit.Damage * ValueModel.CritFactor(threat, p, stats.CriticalMultiplier);
        }

        internal static float? ShifterFormDamage(ShifterFormProjection form, UnitEntityData target, List<EntityFact> transfers)
        {
            if (form.Unit?.View == null || target?.View == null) return null;
            try
            {
                float total = 0f;
                int count = form.Form.AdditionalLimbs.Count() + form.Form.SecondaryAdditionalLimbs.Count();
                bool sole = count == 1 && !form.Unit.Body.AdditionalLimbs.Any(l => l?.MaybeWeapon?.Blueprint?.KeepInPolymorph == true);
                bool first = true;
                foreach (var group in form.Form.AdditionalLimbs.GroupBy(b => b))
                {
                    var blueprint = group.Key;
                    var weapon = Made(blueprint);
                    bool transfer = form.Form.AllowDamageTransfer;
                    total += group.Count() * ShifterHit(form.Unit, target, weapon, 0, false, transfer, transfers, form, sole);
                    if (first && form.Unit.State.Features.IterativeNaturalAttacks)
                    {
                        int extra = ShifterRules.Iteratives(form.Unit.Stats.BaseAttackBonus,
                            form.Unit.Get<Kingmaker.UnitLogic.Parts.UnitPartCompanion>() != null);
                        extra = Math.Min(8, extra); // only a lower-bound forecast for extreme modded BAB; execution is native
                        for (int n = 1; n <= extra; n++)
                            total += ShifterHit(form.Unit, target, weapon, form.Unit.State.Features.FlawlessAttacks ? 0 : -5 * n, false, transfer, transfers, form, sole);
                    }
                    first = false;
                }
                foreach (var group in form.Form.SecondaryAdditionalLimbs.GroupBy(b => b))
                {
                    var blueprint = group.Key;
                    bool secondary = !blueprint.AlwaysPrimary;
                    bool transfer = form.Unit.State.Features.MythicDemonNaturalAttacksNoSecondary;
                    total += group.Count() * ShifterHit(form.Unit, target, Made(blueprint), 0, secondary, transfer, transfers, form, sole);
                }
                // Haste, retained limbs, sneak, Pounce, Rend, form-only feats and other added facts are deliberately
                // uncredited before transformation. The real native routine includes them on the next decision.
                return total;
            }
            catch (Exception ex) { Main.Verbose($"[Shifter] form estimate: {ex.Message}"); return null; }
        }

        private sealed class FuryCount : RuleCalculateAttacksCount
        {
            private readonly ItemEntityWeapon selected;
            private readonly ShiftersFury.HandType hand;
            internal FuryCount(UnitEntityData unit, ShiftersFuryItemData data) : base(unit)
            { selected = data.Weapon; hand = data.Hand; Configure(); }
            private void Configure()
            {
                WeaponFilter = NaturalOnly;
                ForcePrimaryAttackWeapon = hand == ShiftersFury.HandType.Limb ? null :
                    new WeaponData { Weapon = selected, IsPrimaryHand = hand == ShiftersFury.HandType.PrimaryHand };
            }
            public override void OnTrigger(RulebookEventContext context) { Configure(); base.OnTrigger(context); }
        }

        private sealed class FuryLimbs : RuleCalculateAttacksCountLimbs
        {
            private readonly ItemEntityWeapon selected;
            internal FuryLimbs(UnitEntityData unit, ShiftersFuryItemData data) : base(unit)
            { selected = data.Hand == ShiftersFury.HandType.Limb ? data.Weapon : null; Configure(); }
            private void Configure() { WeaponFilter = NaturalOnly; ForcePrimaryAttackWeapon = selected; }
            public override void OnTrigger(RulebookEventContext context) { Configure(); base.OnTrigger(context); }
        }

        private static bool NaturalOnly(BlueprintItemWeapon weapon) => weapon?.IsNatural == true;

        internal static float? ShifterFuryDamage(UnitEntityData unit, UnitEntityData target, ShiftersFuryItemData data, List<EntityFact> transfers)
        {
            if (unit?.View == null || target?.View == null || data?.Weapon == null) return null;
            try
            {
                // Only called with Fury off. Native subscribers still see their normal base event type.
                var count = Rulebook.Trigger<RuleCalculateAttacksCount>(new FuryCount(unit, data));
                var limbs = Rulebook.Trigger<RuleCalculateAttacksCountLimbs>(new FuryLimbs(unit, data));
                float total = 0f;
                int attacks = 0;
                foreach (var attack in UnitAttack.EnumerateAttacks(count)) Add(attack.Hand.MaybeWeapon, attack.AttackBonusPenalty);
                foreach (var attack in UnitAttack.EnumerateLimbsAttacks(limbs)) Add(attack.Hand.MaybeWeapon, attack.AttackBonusPenalty);
                return total;
                void Add(ItemEntityWeapon weapon, int penalty)
                {
                    if (weapon == null) return;
                    if (++attacks > 32) throw new NotSupportedException("Fury routine exceeds forecast budget");
                    // Native Fury explicitly overrides even an AlwaysPrimary natural weapon. Unarmed strikes are
                    // excluded from its stats override (although its natural-only count filter can retain them).
                    bool? secondary = FurySecondary(weapon.Blueprint, weapon == data.Weapon);
                    bool transfer = unit.Body.IsPolymorphed && unit.GetActivePolymorph().Component?.AllowDamageTransfer == true &&
                        (weapon == data.Weapon || unit.State.Features.MythicDemonNaturalAttacksNoSecondary);
                    total += ShifterHit(unit, target, weapon, penalty, secondary, transfer, transfers);
                }
            }
            catch (Exception ex) { Main.Verbose($"[Shifter] Fury estimate: {ex.Message}"); return null; }
        }
    }
}
