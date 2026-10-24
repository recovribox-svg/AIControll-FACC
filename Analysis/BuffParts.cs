// ★ v0.3.19 (test11): what a buff does, part by part, with the size of each part - read from its components the way the
// game applies them, so base-game and mod buffs built from the same components are read alike.
//
// BuffEffectAnalyzer gives one 0..1 "quality" per buff (the best component's class, size ignored): Guidance's +1 attack was
// worth what a +4 is, Light of the Angels' 1 temporary hit point (Mythic/Angel/AngelMinorAbilityEffectBuff) what fifty are.
// The parts here keep the bonus type (stacking) and the value as the game stores it (a number, a rank-based ContextValue,
// dice): the tactical engine evaluates them with the caster's real context (TacticalEngine.Buffs) and values each part
// for the recipient. Static per buff (cached); nothing here depends on a unit.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Analysis
{
    public enum BuffPartKind
    {
        /// <summary>Attack rolls (AdditionalAttackBonus, weapon attack bonuses).</summary>
        Attack,
        /// <summary>Weapon damage per hit (AdditionalDamage, weapon damage bonuses).</summary>
        Damage,
        ArmorClass,
        /// <summary>All three saves; <see cref="BuffPart.SaveShare"/> is 1/3 for one save.</summary>
        Saves,
        Strength,
        Dexterity,
        Constitution,
        /// <summary>Intelligence, Wisdom or Charisma (spell DCs and casting).</summary>
        MentalStat,
        TempHp,
        /// <summary>Concealment, miss chance in percent (Blur 20, Displacement 50).</summary>
        Concealment,
        /// <summary>A miss chance attackers roll (SetAttackerMissChance), percent.</summary>
        MissChance,
        MirrorImages,
        /// <summary>Damage reduction against physical damage, per hit (with a pool when it has one).</summary>
        PhysicalDR,
        /// <summary>Resistance to one energy type, per hit.</summary>
        EnergyResist,
        /// <summary>Extra attacks in a full attack (Haste, Divine Power).</summary>
        ExtraAttack,
        /// <summary>The body grows (ChangeUnitSize): -1 attack and AC from size, bigger weapon dice.</summary>
        SizeUp,
        /// <summary>No spells while it lasts (Rage).</summary>
        ForbidsSpells,
        /// <summary>AC or saves against one alignment only (Protection from Evil).</summary>
        ConditionalAC,
        ConditionalSaves,
        /// <summary>★ v0.3.21: invisible (BuffInvisibility): total concealment and +2 attack against enemies that cannot see
        /// it, who also lose their Dexterity to AC against it; <see cref="BuffPart.EndsOnAttack"/> for the kind an attack ends.</summary>
        Invisibility,
        /// <summary>★ v0.3.21: the weapon's damage dice grow this many size steps (IncreaseDiceSizeOnAttack: Lead Blades).</summary>
        WeaponDiceUp,
        /// <summary>★ v0.3.21: an enhancement bonus on the weapon itself (WeaponEnhancementBonus): attack and damage, above
        /// what the weapon already has unless it stacks.</summary>
        WeaponEnhancement,
        /// <summary>★ v0.3.21: the weapon's critical threat range doubles (WeaponCriticalEdgeIncrease: keen) - not with
        /// Improved Critical, which sets the same flag (RuleCalculateWeaponStats.DoubleCriticalEdge).</summary>
        CritRangeDouble
    }

    public sealed class BuffPart
    {
        public BuffPartKind Kind;
        /// <summary>The buff whose context evaluates the values (a child buff's own ranks).</summary>
        public BlueprintBuff Owner;
        public StatType Stat;
        public ModifierDescriptor Descriptor = ModifierDescriptor.UntypedStackable;
        public int Fixed;
        public ContextValue Value;
        public int Multiplier = 1;
        public ContextDiceValue Dice;
        public DiceFormula Formula;
        public bool FormulaBonusByCasterLevel;
        public ContextValue Pool;
        public int Cap;
        /// <summary>Only for ranged / only for melee attacks; neither when both flags are false.</summary>
        public bool RangedOnly, MeleeOnly;
        /// <summary>Applies only sometimes (a weapon kind, an alignment): counted at half.</summary>
        public bool Conditional;
        /// <summary>★ v0.3.20 (test5): a save bonus only against effects of these descriptors (Remove Fear: +4 against fear):
        /// counted for the share of enemies that can use one (EnemyThreat.ShareUsing), not at half.</summary>
        public Kingmaker.Blueprints.Classes.Spells.SpellDescriptor Against;
        /// <summary>★ v0.3.20 (test5): a bonus only against creatures of this alignment (Protection from Evil): counted for
        /// the share of enemies of it, not at half.</summary>
        public AlignmentComponent? AgainstAlignment;
        public bool ScaleByBaseAttack;
        /// <summary>★ v0.3.19 (class audit B2): the amount is per character level of the recipient (TemporaryHitPointsPerLevel).</summary>
        public bool PerRecipientLevel;
        public float SaveShare = 1f;
        /// <summary>★ v0.3.21: only with weapons this accepts (natural, a category); judged on the recipient's weapon in hand
        /// instead of counted at half. Null: any weapon.</summary>
        public Func<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, bool> Weapon;
        /// <summary>★ v0.3.21: energy damage (not reduced like weapon damage against swarms).</summary>
        public bool Energy;
        /// <summary>★ v0.3.21: invisibility that ends with the bearer's first offensive action (Invisibility, Vanish).</summary>
        public bool EndsOnAttack;
        /// <summary>★ v0.3.21: the feature this part comes with (a Tactician's teamwork feat): nothing for a recipient that
        /// has it already.</summary>
        public Kingmaker.Blueprints.Facts.BlueprintUnitFact Granted;

        /// <summary>★ v0.3.21: a copy for some weapons only.</summary>
        public BuffPart ForWeapons(Func<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, bool> weapon, BlueprintBuff owner)
        {
            var copy = (BuffPart)MemberwiseClone();
            var own = Weapon;
            copy.Weapon = own == null ? weapon : w => own(w) && weapon(w);
            copy.Owner = Owner ?? owner;
            return copy;
        }

        public override string ToString() =>
            $"{Kind}{(Kind == BuffPartKind.Saves && SaveShare < 1f ? $"({Stat})" : "")} " +
            (Value != null && Value.ValueType != ContextValueType.Simple ? $"{Value.ValueType}" : $"{Fixed + (Value?.Value ?? 0) * Multiplier}") +
            (Descriptor != ModifierDescriptor.UntypedStackable ? $" {Descriptor}" : "") + (Conditional ? " (conditional)" : "");
    }

    public sealed class BuffPartList
    {
        public readonly List<BuffPart> Parts = new List<BuffPart>();
        /// <summary>Component type names that carry an effect this reader does not value.</summary>
        public readonly List<string> Unread = new List<string>();
        public bool Any => Parts.Count > 0;
    }

    public static class BuffPartsReader
    {
        private static readonly Dictionary<string, BuffPartList> Cache = new Dictionary<string, BuffPartList>();

        private static readonly Dictionary<string, BuffPartList> EnchantmentCache = new Dictionary<string, BuffPartList>();

        public static void ClearCache()
        {
            Cache.Clear();
            EnchantmentCache.Clear();
        }

        /// <summary>
        /// ★ v0.3.21: what a weapon enchantment does to the weapon it is on, as buff parts for its wielder: enhancement
        /// (WeaponEnhancementBonus), keen (WeaponCriticalEdgeIncrease), energy dice on each hit (WeaponEnergyDamageDice),
        /// dice against an alignment (WeaponDamageAgainstAlignment: Holy, Unholy, Axiomatic, Anarchic), bane
        /// (WeaponConditionalDamageDice / WeaponConditionalEnhancementBonus against a creature type: at half).
        /// </summary>
        public static BuffPartList ReadEnchantment(Kingmaker.Blueprints.Items.Ecnchantments.BlueprintItemEnchantment enchantment)
        {
            if (enchantment == null) return new BuffPartList();
            string key = enchantment.AssetGuid.ToString();
            if (EnchantmentCache.TryGetValue(key, out var cached)) return cached;
            var list = new BuffPartList();
            try
            {
                foreach (var c in enchantment.ComponentsArray ?? new BlueprintComponent[0])
                {
                    switch (c)
                    {
                        case null:
                            break;
                        case WeaponEnhancementBonus e when e.EnhancementBonus > 0:
                            list.Parts.Add(new BuffPart
                            {
                                Kind = BuffPartKind.WeaponEnhancement, Fixed = e.EnhancementBonus,
                                Descriptor = e.Stack ? ModifierDescriptor.UntypedStackable : ModifierDescriptor.Enhancement
                            });
                            break;
                        case Kingmaker.Designers.Mechanics.WeaponEnchants.WeaponCriticalEdgeIncrease _:
                            list.Parts.Add(new BuffPart { Kind = BuffPartKind.CritRangeDouble });
                            break;
                        case WeaponEnergyDamageDice energy when energy.EnergyDamageDice.Rolls > 0:
                            list.Parts.Add(new BuffPart { Kind = BuffPartKind.Damage, Formula = energy.EnergyDamageDice, Energy = true });
                            break;
                        case WeaponDamageAgainstAlignment aligned when aligned.Value != null:
                            list.Parts.Add(new BuffPart
                            {
                                Kind = BuffPartKind.Damage, Dice = aligned.Value, Energy = true, AgainstAlignment = aligned.EnemyAlignment
                            });
                            break;
                        case WeaponConditionalDamageDice bane when bane.Damage?.Dice.Rolls > 0 || bane.Damage?.Bonus > 0:
                            list.Parts.Add(new BuffPart
                            {
                                Kind = BuffPartKind.Damage, Formula = bane.Damage.Dice, Fixed = bane.Damage.Bonus, Conditional = true,
                                Energy = bane.Damage.TypeDescription?.Type != Kingmaker.RuleSystem.Rules.Damage.DamageType.Physical
                            });
                            break;
                        case WeaponConditionalEnhancementBonus baneEnhancement when baneEnhancement.EnhancementBonus > 0:
                            list.Parts.Add(new BuffPart
                            {
                                Kind = BuffPartKind.WeaponEnhancement, Fixed = baneEnhancement.EnhancementBonus, Conditional = true,
                                Descriptor = ModifierDescriptor.Enhancement
                            });
                            break;
                        default:
                            if (!IsBookkeeping(c)) list.Unread.Add(c.GetType().Name);
                            break;
                    }
                }
            }
            catch (Exception ex) { Main.Verbose($"[BuffParts] enchantment {enchantment.name}: {ex.Message}"); }
            EnchantmentCache[key] = list;
            return list;
        }

        public static BuffPartList Read(BlueprintBuff buff)
        {
            if (buff == null) return new BuffPartList();
            string key = buff.AssetGuid.ToString();
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var list = new BuffPartList();
            try { ReadInto(buff, buff.ComponentsArray, list, 0); }
            catch (Exception ex) { Main.Verbose($"[BuffParts] {buff.name}: {ex.Message}"); }
            Cache[key] = list;
            return list;
        }

        private static void ReadInto(BlueprintBuff owner, BlueprintComponent[] components, BuffPartList list, int depth)
        {
            if (components == null || depth > 2) return;
            foreach (var c in components)
            {
                if (c == null) continue;
                switch (c)
                {
                    case AddStatBonus s:
                        AddStat(list, owner, s.Stat, s.Descriptor, fixedValue: s.Value, value: null, multiplier: 1, byBab: s.ScaleByBasicAttackBonus);
                        break;
                    case AddContextStatBonus cs:
                        AddStat(list, owner, cs.Stat, cs.Descriptor, 0, cs.Value, cs.Multiplier, false);
                        break;
                    case AddGenericStatBonus g:
                        AddStat(list, owner, g.Stat, g.Descriptor, g.Value, null, 1, false);
                        break;
                    case AddStatBonusAbilityValue a:
                        AddStat(list, owner, a.Stat, a.Descriptor, 0, a.Value, 1, false);
                        break;
                    case BuffAllSavesBonus saves:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Saves, Owner = owner, Descriptor = saves.Descriptor, Fixed = saves.Value });
                        break;
                    case TemporaryHitPointsFromAbilityValue thp:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.TempHp, Owner = owner, Descriptor = thp.Descriptor, Value = thp.Value });
                        break;
                    case TemporaryHitPointsRandom rnd:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.TempHp, Owner = owner, Descriptor = rnd.Descriptor, Formula = rnd.Dice, Value = rnd.Bonus,
                            FormulaBonusByCasterLevel = rnd.ScaleBonusByCasterLevel
                        });
                        break;
                    case AddConcealment con:
                        if (con.Concealment == Concealment.None) break;
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Concealment, Owner = owner, Fixed = con.Concealment == Concealment.Total ? 50 : 20,
                            Conditional = con.CheckWeaponRangeType || con.CheckDistance
                        });
                        break;
                    case SetAttackerMissChance miss:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.MissChance, Owner = owner, Value = miss.Value, Conditional = true });
                        break;
                    case AddMirrorImage images:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.MirrorImages, Owner = owner, Dice = images.Count, Cap = images.MaxCount });
                        break;
                    case BuffExtraAttack extra:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ExtraAttack, Owner = owner, Fixed = Math.Max(1, extra.Number) });
                        break;
                    case WeaponParametersAttackBonus wa:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Attack, Owner = owner, Descriptor = wa.Descriptor,
                            Fixed = wa.UseContextIstead ? 0 : wa.AttackBonus, Value = wa.UseContextIstead ? wa.ContextAttackBonus : null,
                            RangedOnly = wa.Ranged, MeleeOnly = !wa.Ranged,
                            Conditional = wa.OnlyFinessable || wa.OnlyTwoHanded || wa.SpecificWeaponBlueprint,
                            ScaleByBaseAttack = wa.ScaleByBasicAttackBonus
                        });
                        break;
                    case WeaponParametersDamageBonus wd:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Damage, Owner = owner,
                            Fixed = wd.UseContextIstead ? 0 : wd.DamageBonus, Value = wd.UseContextIstead ? wd.ContextDamageBonus : null,
                            RangedOnly = wd.Ranged, MeleeOnly = !wd.Ranged,
                            Conditional = wd.OnlyFinessable || wd.OnlyTwoHanded || wd.DualWielding || wd.OnlyToOffHandBonus,
                            ScaleByBaseAttack = wd.ScaleByBasicAttackBonus
                        });
                        break;
                    case AdditionalDiceOnAttack dice:
                        // Extra damage dice on weapon hits (Geniekind forms, elemental weapon buffs). Only some attacks
                        // (the first, a charge, sneak attacks, one weapon kind, an extra condition): half.
                        if (dice.AttackType == AdditionalDiceOnAttack.WeaponOptions.OnlyNonWeaponAttacks || dice.Value == null) break;
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Damage, Owner = owner, Dice = dice.Value,
                            MeleeOnly = dice.CheckWeaponRangeType && dice.RangeType == Kingmaker.Enums.WeaponRangeType.Melee,
                            RangedOnly = dice.CheckWeaponRangeType && dice.RangeType == Kingmaker.Enums.WeaponRangeType.Ranged,
                            Conditional = dice.OnlyOnFirstAttack || dice.OnlyOnFirstHit || dice.OnlyOnFullAttack || dice.OnlySneakAttack ||
                                          dice.OnCharge || dice.OnAttackOfOpportunity || dice.CriticalHit || dice.CheckWeaponCategory ||
                                          dice.CheckWeaponGroup || (dice.InitiatorConditions?.Conditions?.Length ?? 0) > 0 ||
                                          (dice.TargetConditions?.Conditions?.Length ?? 0) > 0
                        });
                        break;
                    case AddDamageResistancePhysical dr:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.PhysicalDR, Owner = owner, Value = dr.Value, Pool = dr.UsePool ? dr.Pool : null });
                        break;
                    // ★ v0.3.21: Protection from Energy absorbs every point of its energy type until its pool is spent
                    // (ProtectionFromEnergy.CalculateValue returns the pool left): the pool is both the amount and the cap.
                    case ProtectionFromEnergy pe:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.EnergyResist, Owner = owner, Value = pe.UsePool ? pe.Pool : pe.Value, Pool = pe.UsePool ? pe.Pool : null });
                        break;
                    case AddDamageResistanceEnergy er:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.EnergyResist, Owner = owner, Value = er.Value, Pool = er.UsePool ? er.Pool : null });
                        break;
                    case Kingmaker.Designers.Mechanics.Buffs.ChangeUnitSize size:
                        if (size.SizeDelta > 0 || size.Size > Size.Medium)
                            list.Parts.Add(new BuffPart { Kind = BuffPartKind.SizeUp, Owner = owner });
                        break;
                    case ForbidSpellCasting _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ForbidsSpells, Owner = owner });
                        break;
                    case ArmorClassBonusAgainstAlignment aca:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ConditionalAC, Owner = owner, Descriptor = aca.Descriptor, Fixed = aca.Value, Value = aca.Bonus, Conditional = true, AgainstAlignment = aca.alignment });
                        break;
                    case SavingThrowBonusAgainstAlignment sva:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ConditionalSaves, Owner = owner, Descriptor = sva.Descriptor, Fixed = sva.Value, Value = sva.Bonus, Conditional = true, AgainstAlignment = sva.Alignment });
                        break;
                    // ★ v0.3.19 (class audit B2): components the class blueprints use for their main bonuses - Rage,
                    // Bloodrage and Inspired Rage give their attack and damage this way (melee only), stances and
                    // judgments too; Bit of Luck, Fortune, Lion's Call reroll the d20.
                    case AttackTypeAttackBonus ata:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Attack, Owner = owner, Descriptor = ata.Descriptor, Value = ata.Value, Multiplier = ata.AttackBonus,
                            MeleeOnly = !ata.AllTypesExcept && IsMeleeType(ata.Type) || ata.AllTypesExcept && IsRangedType(ata.Type),
                            RangedOnly = !ata.AllTypesExcept && IsRangedType(ata.Type) || ata.AllTypesExcept && IsMeleeType(ata.Type),
                            Conditional = ata.CheckFact
                        });
                        break;
                    case WeaponAttackTypeDamageBonus wtd:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Damage, Owner = owner, Descriptor = wtd.Descriptor, Value = wtd.Value, Multiplier = wtd.AttackBonus,
                            MeleeOnly = IsMeleeType(wtd.Type), RangedOnly = IsRangedType(wtd.Type)
                        });
                        break;
                    case ACBonusAgainstAttacks acb:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.ArmorClass, Owner = owner, Descriptor = acb.Descriptor, Fixed = acb.ArmorClassBonus, Value = acb.Value,
                            Conditional = acb.AgainstMeleeOnly || acb.AgainstRangedOnly || acb.OnlySneakAttack || acb.OnlyAttacksOfOpportunity ||
                                          acb.IsTouch || acb.CheckArmorCategory || acb.NoShield || acb.OneHanded
                        });
                        break;
                    case TemporaryHitPointsPerLevel tpl:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.TempHp, Owner = owner, Descriptor = tpl.Descriptor, Value = tpl.Value,
                            Multiplier = tpl.HitPointsPerLevel, PerRecipientLevel = true
                        });
                        break;
                    case ModifyD20 d20 when d20.TakeBest && d20.RollsAmount >= 1 && !d20.Replace:
                    {
                        // Two d20s, the better kept: about +3 on a roll that needs 6-15 (ValueModel works in 5 % steps).
                        bool partial = d20.RerollOnlyIfFailed || d20.RerollOnlyIfSuccess || d20.WithChance || d20.AgainstAlignment ||
                                       d20.SpecificDescriptor || d20.DispellOnRerollFinished || d20.DispellOn20;
                        if ((d20.Rule & RuleType.AttackRoll) != 0)
                            list.Parts.Add(new BuffPart { Kind = BuffPartKind.Attack, Owner = owner, Fixed = 3, Conditional = partial, Descriptor = ModifierDescriptor.UntypedStackable });
                        if ((d20.Rule & RuleType.SavingThrow) != 0)
                            list.Parts.Add(new BuffPart { Kind = BuffPartKind.Saves, Owner = owner, Fixed = 3, Conditional = partial, Descriptor = ModifierDescriptor.UntypedStackable });
                        break;
                    }
                    case SavingThrowBonusAgainstDescriptor svd:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ConditionalSaves, Owner = owner, Descriptor = svd.ModifierDescriptor, Fixed = svd.Value, Value = svd.Bonus, Conditional = true, Against = svd.SpellDescriptor.Value });
                        break;
                    case SavingThrowContextBonusAgainstDescriptor svc:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ConditionalSaves, Owner = owner, Descriptor = svc.ModifierDescriptor, Value = svc.Value, Conditional = true, Against = svc.SpellDescriptor.Value });
                        break;
                    // ★ v0.3.21: Greater Invisibility (stays after attacks), Invisibility and Vanish (end at the first
                    // offensive action against an enemy: BuffInvisibility.HandleUnitMakeOffensiveAction).
                    case BuffInvisibility invisible:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Invisibility, Owner = owner, Fixed = 50,
                            EndsOnAttack = !invisible.NotDispellAfterOffensiveAction && !invisible.DispelWithAChance,
                            Conditional = !invisible.NotDispellAfterOffensiveAction && invisible.DispelWithAChance
                        });
                        break;
                    // ★ v0.3.21: energy dice on each weapon hit (AdditionalDamageOnHit.OnEventAboutToTrigger: Scaled Fist
                    // Draconic Fury), with the weapons it accepts.
                    case AdditionalDamageOnHit onHit when onHit.EnergyDamageDice.Rolls > 0:
                    {
                        var specific = onHit.SpecificWeapon ? onHit.Weapon : null;
                        bool natural = onHit.OnlyNaturalAndUnarmed, melee = onHit.OnlyMelee, isSpecific = onHit.SpecificWeapon;
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Damage, Owner = owner, Formula = onHit.EnergyDamageDice, Energy = true, MeleeOnly = melee,
                            Weapon = w => (!natural || w.Type.IsNatural || w.Type.IsUnarmed) && (!melee || w.IsMelee) && (!isSpecific || w == specific)
                        });
                        break;
                    }
                    // ★ v0.3.21: Magic Fang and Greater Magic Fang (EquipmentWeaponTypeEnhancement.CheckWeapon): an
                    // enhancement bonus to attack and damage with natural and unarmed attacks, or one weapon category.
                    case Kingmaker.Designers.Mechanics.EquipmentEnchants.EquipmentWeaponTypeEnhancement fang when fang.Enhancement > 0:
                    {
                        bool all = fang.AllNaturalAndUnarmed;
                        var category = fang.Category;
                        Func<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, bool> accepts = w => all && (w.IsNatural || w.IsUnarmed) || w.Category == category;
                        var descriptor = fang.IsStackable ? ModifierDescriptor.UntypedStackable : ModifierDescriptor.Enhancement;
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Attack, Owner = owner, Descriptor = descriptor, Fixed = fang.Enhancement, Weapon = accepts });
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Damage, Owner = owner, Descriptor = descriptor, Fixed = fang.Enhancement, Weapon = accepts });
                        break;
                    }
                    // ★ v0.3.21: Lead Blades (IncreaseDiceSizeOnAttack.CheckCondition): the dice of weapons of these
                    // categories as if the weapon were bigger.
                    case IncreaseDiceSizeOnAttack dice:
                    {
                        var categories = dice.CheckWeaponCategories ? dice.Categories : null;
                        var subCategories = dice.CheckWeaponSubCategories ? dice.SubCategories : null;
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.WeaponDiceUp, Owner = owner, Fixed = dice.UseContextBonus ? 0 : dice.AdditionalSize,
                            Value = dice.UseContextBonus ? dice.Value : null,
                            Weapon = w => (categories == null || categories.Contains(w.Category)) &&
                                          (subCategories == null || subCategories.Any(sc => w.Category.HasSubCategory(sc)))
                        });
                        break;
                    }
                    case AddFacts facts:
                        foreach (var fact in facts.Facts)
                            if (fact != null) ReadInto(owner, fact.ComponentsArray, list, depth + 1);
                        break;
                    // ★ v0.3.21: a Tactician shares a teamwork feat for a few rounds (Vanguard / Forester: AddFactsFromCaster;
                    // Cavalier: AddFeatureIfHasFact, "if it does not have it yet") - read as the feat's own parts.
                    case Kingmaker.UnitLogic.FactLogic.AddFactsFromCaster fromCaster when !fromCaster.FeatureFromSelection:
                        foreach (var fact in fromCaster.Facts)
                            if (fact != null) ReadGranted(owner, fact, list, depth);
                        break;
                    case AddFeatureIfHasFact ifHas when ifHas.Feature != null:
                        // Not: added when the bearer lacks the checked fact (the feat itself, for the Cavalier's buffs);
                        // otherwise only for bearers that have it (the feat's own follow-ups): at half.
                        ReadGranted(owner, ifHas.Feature, list, depth, conditional: !ifHas.Not);
                        break;
                    // ★ v0.3.21: teamwork feats (the game's components). Each needs a partner with the same feat next to the
                    // enemy or the bearer - counted at half.
                    case PreciseStrike precise when precise.Damage != null:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Damage, Owner = owner, Formula = precise.Damage.Dice, Fixed = precise.Damage.Bonus,
                            MeleeOnly = true, Conditional = true
                        });
                        break;
                    case OutflankAttackBonus outflank when outflank.AttackBonus > 0:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Attack, Owner = owner, Fixed = outflank.AttackBonus, Descriptor = outflank.Descriptor,
                            MeleeOnly = true, Conditional = true
                        });
                        break;
                    case BackToBack _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = 2, Conditional = true });
                        break;
                    case ShakeItOff _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Saves, Owner = owner, Fixed = 1, Conditional = true });
                        break;
                    case AddFactContextActions actions:
                        // Haste: HasteBuff only switches on HasteBuffEffect (Spells/Level3/HasteBuff).
                        foreach (var child in BuffEffectAnalyzer.ChildBuffs(actions))
                            ReadInto(child, child.ComponentsArray, list, depth + 1);
                        break;
                    // ★ v0.3.21: Protective Luck (witch / shaman hex): whoever attacks the bearer gets misfortune before the
                    // roll (AddTargetBeforeAttackRollTrigger.ActionsOnAttacker -> a buff whose ModifyD20 rolls the attack
                    // twice and keeps the worse): about +3 AC against every attacker (ValueModel works in 5 % steps).
                    case AddTargetBeforeAttackRollTrigger before when MisfortuneOnAttacker(before):
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = 3, Descriptor = ModifierDescriptor.UntypedStackable,
                            Conditional = before.OnlyMelee || before.NotReach || before.CheckDescriptor
                        });
                        break;
                    case AddInitiatorAttackRollTrigger _:
                    case AddInitiatorSkillRollTrigger _:
                    case AddInitiatorSavingThrowTrigger _:
                    case AddInitiatorPartySkillRollTrigger _:
                        // Guidance's triggers only remove the buff; others do something on the roll.
                        if (!CompanionAI_Pathfinder.Tactics.AbilityProfile.IsSpentByRoll(owner)) list.Unread.Add(c.GetType().Name);
                        break;
                    default:
                        if (!IsBookkeeping(c)) list.Unread.Add(c.GetType().Name);
                        break;
                }
            }
        }

        /// <summary>★ v0.3.21: the parts of a feature a buff grants, marked with it (see <see cref="BuffPart.Granted"/>).</summary>
        private static void ReadGranted(BlueprintBuff owner, Kingmaker.Blueprints.Facts.BlueprintUnitFact fact, BuffPartList list, int depth,
                                        bool conditional = false)
        {
            int before = list.Parts.Count;
            ReadInto(owner, fact.ComponentsArray, list, depth + 1);
            for (int i = before; i < list.Parts.Count; i++)
            {
                list.Parts[i].Granted = list.Parts[i].Granted ?? fact;
                if (conditional) list.Parts[i].Conditional = true;
            }
        }

        private static void AddStat(BuffPartList list, BlueprintBuff owner, StatType stat, ModifierDescriptor descriptor, int fixedValue,
                                    ContextValue value, int multiplier, bool byBab)
        {
            BuffPartKind kind;
            float share = 1f;
            switch (stat)
            {
                case StatType.AdditionalAttackBonus: kind = BuffPartKind.Attack; break;
                case StatType.AdditionalDamage: kind = BuffPartKind.Damage; break;
                case StatType.AC: kind = BuffPartKind.ArmorClass; break;
                case StatType.SaveFortitude:
                case StatType.SaveReflex:
                case StatType.SaveWill: kind = BuffPartKind.Saves; share = 1f / 3f; break;
                case StatType.Strength: kind = BuffPartKind.Strength; break;
                case StatType.Dexterity: kind = BuffPartKind.Dexterity; break;
                case StatType.Constitution: kind = BuffPartKind.Constitution; break;
                case StatType.Intelligence:
                case StatType.Wisdom:
                case StatType.Charisma: kind = BuffPartKind.MentalStat; break;
                case StatType.TemporaryHitPoints: kind = BuffPartKind.TempHp; break;
                default:
                    // Skills, speed, initiative, CMB / CMD: no combat value here (known, not unread).
                    return;
            }
            list.Parts.Add(new BuffPart
            {
                Kind = kind, Owner = owner, Stat = stat, Descriptor = descriptor, Fixed = fixedValue, Value = value,
                Multiplier = multiplier, ScaleByBaseAttack = byBab, SaveShare = share
            });
        }

        /// <summary>★ v0.3.21: the trigger gives the attacker a buff that makes it roll its attack twice and keep the worse.</summary>
        private static bool MisfortuneOnAttacker(AddTargetBeforeAttackRollTrigger trigger)
        {
            var actions = trigger.ActionsOnAttacker?.Actions;
            if (actions == null) return false;
            foreach (var apply in actions.OfType<Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff>())
            {
                if (apply.Buff == null) continue;
                foreach (var d20 in apply.Buff.GetComponents<ModifyD20>())
                    if ((d20.Rule & RuleType.AttackRoll) != 0 && !d20.TakeBest && !d20.Replace && d20.RollsAmount >= 1 &&
                        !d20.WithChance && !d20.RerollOnlyIfFailed && !d20.RerollOnlyIfSuccess)
                        return true;
            }
            return false;
        }

        private static bool IsRangedType(WeaponRangeType type) => type == WeaponRangeType.Ranged || type == WeaponRangeType.RangedNormal;

        private static bool IsMeleeType(WeaponRangeType type) => type == WeaponRangeType.Melee || type == WeaponRangeType.MeleeNormal;

        /// <summary>Components with no combat effect of their own (descriptors, ranks, bookkeeping, self-removal, visuals).</summary>
        private static bool IsBookkeeping(BlueprintComponent c)
        {
            switch (c.GetType().Name)
            {
                case "SpellDescriptorComponent":
                case "ContextRankConfig":
                case "ContextCalculateSharedValue":
                case "RemoveBuffOnAttack":
                case "RemoveWhenCombatEnded":
                case "NotDispelable":
                case "SetBuffOnsetDelay":
                case "SummonedUnitBuff":
                case "RemoveBuffIfCasterIsMissing":
                case "UniqueBuff":
                case "SpellComponent":
                case "AddSpellSchool":
                case "FormationACBonus":
                case "BuffAllSkillsBonus":
                case "BuffMovementSpeed":
                case "ChangeImpatience":
                case "ReplaceAsksList":
                case "RecalculateOnStatChange":
                case "IsPositiveEffect":
                case "ReplaceAbilityParamsWithContext":
                case "ContextSetAbilityParams":
                case "AbilityScoreCheckBonus":
                case "SavesFixerRecalculate":
                    return true;
                default:
                    return false;
            }
        }
    }
}
