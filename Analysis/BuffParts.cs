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
        /// <summary>★ v0.3.21: all damage the bearer deals is raised by this many percent (OutcomingDamageAndHealingModifier:
        /// Golden Dragon Might).</summary>
        DamagePercent,
        /// <summary>★ v0.3.21: hit points healed each round while hurt (AddEffectFastHealing).</summary>
        FastHealing,
        /// <summary>★ v0.3.21 (gaps): a hit of the bearer's puts <see cref="BuffPart.HitBuff"/> on its target
        /// (AddInitiatorAttackWithWeaponTrigger → ContextActionApplyBuff: Rune blessing, Angel sword, judgments).</summary>
        OnHitBuff,
        /// <summary>★ v0.3.21 (gaps): a hit of the bearer's kills its target (ContextActionKill, usually on a failed save:
        /// Master Slayer, True Judgment) - once.</summary>
        OnHitKill,
        /// <summary>★ v0.3.21 (gaps): the save DCs of the bearer's spells (IncreaseAllSpellsDC; one school or kind of spell:
        /// conditional - IncreaseSpellSchoolDC, IncreaseSpellDescriptorDC).</summary>
        SpellDC,
        /// <summary>★ v0.3.21 (gaps): the bearer's caster level (IncreaseCasterLevel; one school: IncreaseSpellSchoolCasterLevel).</summary>
        CasterLevel,
        /// <summary>★ v0.3.21 (gaps): its checks against spell resistance (SpellPenetrationBonus).</summary>
        SpellPenetration,
        /// <summary>★ v0.3.21: natural weapons for empty hands (EmptyHandWeaponOverride: claws, Feral Mutagen, bloodline
        /// claws): <see cref="BuffPart.Natural"/>; they take effect only with nothing in the hands (UnitBody.SetEmptyHandWeapon).</summary>
        NaturalWeapon,
        /// <summary>★ v0.3.21: sees what hides (AddCondition SeeInvisibility: Fixed 1; TrueSeeing, which also sees through
        /// Blur and Displacement: Fixed 2 - UnitPartConcealment.Calculate).</summary>
        Sight,
        /// <summary>★ v0.3.21: the weapon's critical threat range doubles (WeaponCriticalEdgeIncrease: keen) - not with
        /// Improved Critical, which sets the same flag (RuleCalculateWeaponStats.DoubleCriticalEdge).</summary>
        CritRangeDouble,
        // ★ v0.3.22: outlines remove the bearer's concealment; they are harmful only when something is hidden.
        RemovesConcealment
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
        /// <summary>★ v0.3.21 (log review): the caster's own fact this part comes with (AddFactsFromCaster: the game adds
        /// only the facts the caster has - a Skald's rage powers through Inspired Rage, a Judge's judgements): nothing when
        /// the caster lacks it.</summary>
        public Kingmaker.Blueprints.Facts.BlueprintUnitFact CasterFact;
        /// <summary>★ v0.3.21 (gaps): on-hit parts - the buff put on the target, the save against it, the share of the
        /// bearer's hits it takes effect on (a natural 20: 0.05), on a critical hit only, on its first attack or hit only.</summary>
        public BlueprintBuff HitBuff;
        public SavingThrowType HitSave = SavingThrowType.Unknown;
        public float Chance = 1f;
        public bool CritOnly, FirstOnly;
        /// <summary>★ v0.3.21: the empty-hand weapon a <see cref="BuffPartKind.NaturalWeapon"/> part gives.</summary>
        public Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon Natural;

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
            (Descriptor != ModifierDescriptor.UntypedStackable ? $" {Descriptor}" : "") + (Conditional ? " (conditional)" : "") +
            (CasterFact != null ? " (caster's)" : "");
    }

    public sealed class BuffPartList
    {
        public readonly List<BuffPart> Parts = new List<BuffPart>();
        /// <summary>Component type names that carry an effect this reader does not value.</summary>
        public readonly List<string> Unread = new List<string>();
        /// <summary>★ v0.3.21 (log review): unread components of a caster's fact (see <see cref="BuffPart.CasterFact"/>).</summary>
        public readonly List<KeyValuePair<Kingmaker.Blueprints.Facts.BlueprintUnitFact, string>> CasterUnread =
            new List<KeyValuePair<Kingmaker.Blueprints.Facts.BlueprintUnitFact, string>>();
        public bool Any => Parts.Count > 0;

        /// <summary>★ v0.3.21 (log review): what is not read of this buff as <paramref name="caster"/> gives it.</summary>
        public IEnumerable<string> UnreadFor(Kingmaker.EntitySystem.Entities.UnitEntityData caster) =>
            Unread.Concat(CasterUnread.Where(u => caster?.Descriptor != null && Kingmaker.UnitLogic.UnitHelper.HasFact(caster.Descriptor, u.Key)).Select(u => u.Value));
        /// <summary>★ v0.3.21: the kinds of effect an immunity part already covers (each counted once).</summary>
        public Kingmaker.Blueprints.Classes.Spells.SpellDescriptor ImmuneTo;

        /// <summary>★ v0.3.21: an immunity to what <paramref name="against"/> names that is not counted yet.</summary>
        public void AddImmunity(BlueprintBuff owner, Kingmaker.Blueprints.Classes.Spells.SpellDescriptor against, int saves, bool conditional)
        {
            var fresh = against & ~ImmuneTo;
            if (fresh == Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None) return;
            ImmuneTo |= fresh;
            Parts.Add(new BuffPart { Kind = BuffPartKind.ConditionalSaves, Owner = owner, Fixed = saves, Against = fresh, Conditional = conditional });
        }
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
                            if (fact != null && !IsAbility(fact)) ReadInto(owner, fact.ComponentsArray, list, depth + 1);
                        break;
                    // ★ v0.3.21: a Tactician shares a teamwork feat for a few rounds (Vanguard / Forester: AddFactsFromCaster;
                    // Cavalier: AddFeatureIfHasFact, "if it does not have it yet") - read as the feat's own parts.
                    // ★ v0.3.21 (log review): the game adds only the facts the caster has (AddFactsFromCaster.OnActivate);
                    // each part keeps its fact and counts for a caster that has it. Camellia's Inspired Rage carried "hits
                    // hinder" and three unread components from rage powers she does not have (0.3.21 logs). The facts of a
                    // selection too (a Bladebound's black blade feats).
                    case Kingmaker.UnitLogic.FactLogic.AddFactsFromCaster fromCaster:
                    {
                        IEnumerable<Kingmaker.Blueprints.Facts.BlueprintUnitFact> granted = !fromCaster.FeatureFromSelection
                            ? (IEnumerable<Kingmaker.Blueprints.Facts.BlueprintUnitFact>)fromCaster.Facts
                            : fromCaster.Selection != null
                                ? fromCaster.Selection.AllFeatures.Cast<Kingmaker.Blueprints.Facts.BlueprintUnitFact>()
                                : Enumerable.Empty<Kingmaker.Blueprints.Facts.BlueprintUnitFact>();
                        foreach (var fact in granted)
                            if (fact != null) ReadGranted(owner, fact, list, depth, fromCaster: true);
                        break;
                    }
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
                    // ★ v0.3.21: Edict of Impenetrable Fortress / Invulnerability (Aeon): immune to physical damage
                    // (AddPhysicalImmunity: every physical form, RuleCalculateDamage) - read as damage reduction no weapon
                    // passes.
                    case Kingmaker.UnitLogic.FactLogic.AddPhysicalImmunity _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.PhysicalDR, Owner = owner, Fixed = 999 });
                        break;
                    // ★ v0.3.21: Golden Dragon Might: +N % to the damage the bearer deals (ModifierBonus of RuleDealDamage).
                    case OutcomingDamageAndHealingModifier more when more.Type != OutcomingDamageAndHealingModifier.ModifyingType.OnlyHeal && more.ModifierPercents != null:
                    {
                        int weapon = OutgoingWeaponType(more);
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.DamagePercent, Owner = owner, Value = more.ModifierPercents,
                            MeleeOnly = weapon == 1, RangedOnly = weapon == 2, Conditional = OutgoingLimited(more)
                        });
                        break;
                    }
                    // ★ v0.3.21: a second buff added while a first one is on the bearer (Thousand Bites, Dragon Pride:
                    // BuffExtraEffects.CheckAndReapply): its parts at half.
                    case BuffExtraEffects extra when extra.ExtraEffectBuff != null:
                    {
                        int before = list.Parts.Count;
                        ReadInto(extra.ExtraEffectBuff, extra.ExtraEffectBuff.ComponentsArray, list, depth + 1);
                        for (int i = before; i < list.Parts.Count; i++) list.Parts[i].Conditional = true;
                        break;
                    }
                    // ★ v0.3.21 (verification audit): immunities - counted like a large save bonus against effects of that
                    // kind, for the share of the enemies that can use one (EnemyThreat.ShareUsing).
                    case BuffDescriptorImmunity immune when immune.Descriptor.Value != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None:
                        list.AddImmunity(owner, immune.Descriptor.Value, ImmunitySaves, immune.CheckFact);
                        break;
                    case SpellImmunityToSpellDescriptor spellImmune when spellImmune.Descriptor.Value != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None:
                        list.AddImmunity(owner, spellImmune.Descriptor.Value, ImmunitySaves, false);
                        break;
                    // ★ v0.3.21 (gaps): teamwork feats with numbers - Allied Spellcaster (+2 against spell resistance with an
                    // ally who has it nearby), Shield Wall (+1 shield AC beside one), an attack bonus against some targets
                    // (AttackBonusAgainstFactOwner: Volley Fire): conditional.
                    case AlliedSpellcaster _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.SpellPenetration, Owner = owner, Fixed = 2, Conditional = true });
                        break;
                    case ShieldWall _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = 1, Descriptor = ModifierDescriptor.UntypedStackable, Conditional = true });
                        break;
                    case AttackBonusAgainstFactOwner against:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Attack, Owner = owner, Fixed = against.AttackBonus, Value = against.Bonus,
                                                      Descriptor = against.Descriptor, Conditional = true });
                        break;
                    // ★ v0.3.21 (gaps): what the bearer's hits do (a debuff, extra damage, a kill on a failed save).
                    case AddInitiatorAttackWithWeaponTrigger hit when ReadOnHit(owner, hit, list):
                        break;
                    // ★ v0.3.21 (gaps): spellcasting boosts - valued for a recipient that casts (TacticalEngine.Buffs).
                    case Kingmaker.Designers.Mechanics.Facts.IncreaseAllSpellsDC dc when dc.Value != null:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.SpellDC, Owner = owner, Value = dc.Value, Descriptor = dc.Descriptor });
                        break;
                    case Kingmaker.Designers.Mechanics.Facts.IncreaseSpellSchoolDC schoolDc:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.SpellDC, Owner = owner, Fixed = schoolDc.BonusDC, Descriptor = schoolDc.Descriptor, Conditional = true });
                        break;
                    case Kingmaker.Designers.Mechanics.Facts.IncreaseSpellDescriptorDC descriptorDc:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.SpellDC, Owner = owner, Fixed = descriptorDc.BonusDC, Descriptor = descriptorDc.ModifierDescriptor, Conditional = true });
                        break;
                    case Kingmaker.Designers.Mechanics.Facts.IncreaseCasterLevel cl when cl.Value != null:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.CasterLevel, Owner = owner, Value = cl.Value, Descriptor = cl.Descriptor });
                        break;
                    case Kingmaker.Designers.Mechanics.Facts.IncreaseSpellSchoolCasterLevel schoolCl:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.CasterLevel, Owner = owner, Fixed = schoolCl.BonusLevel, Descriptor = schoolCl.Descriptor, Conditional = true });
                        break;
                    case Kingmaker.Designers.Mechanics.Facts.SpellPenetrationBonus pen when pen.Value != null:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.SpellPenetration, Owner = owner, Value = pen.Value, Descriptor = pen.Descriptor, Conditional = pen.CheckFact });
                        break;
                    case Kingmaker.Designers.Mechanics.Buffs.EmptyHandWeaponOverride claws when claws.Weapon != null && !claws.IsMonkUnarmedStrike:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.NaturalWeapon, Owner = owner, Natural = claws.Weapon });
                        break;
                    case Kingmaker.UnitLogic.FactLogic.AddCondition sees when sees.Condition == Kingmaker.UnitLogic.UnitCondition.SeeInvisibility ||
                                                                              sees.Condition == Kingmaker.UnitLogic.UnitCondition.TrueSeeing:
                        list.Parts.Add(new BuffPart
                        {
                            Kind = BuffPartKind.Sight, Owner = owner,
                            Fixed = sees.Condition == Kingmaker.UnitLogic.UnitCondition.TrueSeeing ? 2 : 1
                        });
                        break;
                    case AddConditionImmunity conditionImmune when ConditionDescriptor(conditionImmune.Condition) != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None:
                        list.AddImmunity(owner, ConditionDescriptor(conditionImmune.Condition), ImmunitySaves, false);
                        break;
                    case AddEnergyDamageImmunity energyImmune:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.EnergyResist, Owner = owner, Fixed = EnergyImmunityResist });
                        break;
                    case AddEnergyImmunity energyImmune2:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.EnergyResist, Owner = owner, Fixed = EnergyImmunityResist });
                        break;
                    // ★ v0.3.21 (verification audit): a weapon enchantment the buff keeps on the weapon in hand (Divine Weapon
                    // Bond, Arcane Pool, Sacred Weapon: BuffEnchantWornItem / BuffEnchantAnyWeapon) - the enchantment's parts.
                    case BuffEnchantWornItem worn when worn.Enchantment != null && (worn.AllWeapons || worn.Slot == Kingmaker.UI.GenericSlot.EquipSlotBase.SlotType.PrimaryHand):
                        foreach (var part in ReadEnchantment(worn.Enchantment).Parts) list.Parts.Add(part.ForWeapons(w => true, owner));
                        break;
                    case BuffEnchantAnyWeapon any when any.Enchantment != null && any.Slot == Kingmaker.UI.GenericSlot.EquipSlotBase.SlotType.PrimaryHand:
                        foreach (var part in ReadEnchantment(any.Enchantment).Parts) list.Parts.Add(part.ForWeapons(w => true, owner));
                        break;
                    case Kingmaker.UnitLogic.Buffs.Components.AddEffectFastHealing fast:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.FastHealing, Owner = owner, Fixed = fast.Heal, Value = fast.Bonus });
                        break;
                    case BackToBack _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = 2, Conditional = true });
                        break;
                    case ShakeItOff _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.Saves, Owner = owner, Fixed = 1, Conditional = true });
                        break;
                    // ★ v0.3.22: conditional penalties are filtered against the party's actual delivery options.
                    case ACBonusAgainstWeaponSubcategory ac:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = ac.ArmorClassBonus,
                            Descriptor = ac.Descriptor, Weapon = w => w != null && w.Category.HasSubCategory(ac.SubCategory) });
                        break;
                    case ACBonusAgainstSpellsWithDescriptor ac:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.ArmorClass, Owner = owner, Fixed = ac.ArmorClassBonus,
                            Descriptor = ac.Descriptor, Against = ac.SpellDescriptor.Value });
                        break;
                    case BuffSkillStealthBonus _:
                        break; // ★ v0.3.22: combat scoring has no stealth routine; this is not an unread combat bonus
                    case AddConditionImmunity outline when outline.Condition == Kingmaker.UnitLogic.UnitCondition.Invisible &&
                                                             owner.GetComponent<DoNotBenefitFromConcealment>() != null:
                        break; // the outline's concealment denial below already covers this part
                    case DoNotBenefitFromConcealment _:
                        list.Parts.Add(new BuffPart { Kind = BuffPartKind.RemovesConcealment, Owner = owner });
                        break;
                    case AddFactContextActions actions:
                        // Haste: HasteBuff only switches on HasteBuffEffect (Spells/Level3/HasteBuff).
                        foreach (var child in BuffEffectAnalyzer.ChildBuffs(actions))
                            ReadInto(child, child.ComponentsArray, list, depth + 1);
                        // ★ v0.3.21 (gaps): a stat change made when the buff starts (BuffActionAddStatBonus: Ray of
                        // Enfeeblement's Strength penalty, a shared value of 1d6 + 1 per 2 levels, x-1).
                        ReadActivatedStats(owner, actions.Activated?.Actions, list, false, 0);
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

        /// <summary>
        /// ★ v0.3.21 (gaps): an on-hit trigger's effects as parts; true when it was read (or only spends the buff: its
        /// removal). Triggers on a miss, before the attack, on the attacker, on attacks of opportunity or on a kill are not
        /// read; an action other than a buff, damage, a kill, a save or a removal marks the trigger as partly unread.
        /// </summary>
        private static bool ReadOnHit(BlueprintBuff owner, AddInitiatorAttackWithWeaponTrigger t, BuffPartList list)
        {
            try
            {
                var found = new List<BuffPart>();
                bool unknown = false;
                WalkOnHit(owner, t.Action?.Actions, found, false, SavingThrowType.Unknown, 0, ref unknown);
                if (found.Count == 0) return !unknown;   // only removals (a buff spent by the attack): nothing to value
                if (t.OnMiss || t.TriggerBeforeAttack || t.ActionsOnInitiator || t.OnAttackOfOpportunity || t.ReduceHPToZero ||
                    t.DamageMoreTargetMaxHP || !t.OnlyHit)
                    return false;
                bool conditional = t.OnlySneakAttack || t.CheckWeaponGroup || t.CheckPhysicalDamageForm || t.CheckDistance ||
                                   t.OnCharge || t.DuelistWeapon || t.AllNaturalAndUnarmed || t.OnlyOnFullAttack;
                foreach (var part in found)
                {
                    var p = part;
                    p.Chance = t.OnlyNatural20 ? 0.05f : 1f;
                    p.CritOnly = t.CriticalHit;
                    p.FirstOnly = t.OnlyOnFirstAttack || t.OnlyOnFirstHit;
                    p.Conditional |= conditional;
                    if (t.CheckWeaponBlueprint && t.WeaponBlueprint != null)
                    {
                        var only = t.WeaponBlueprint;
                        p = p.ForWeapons(w => w == only, owner);
                    }
                    else if (t.CheckWeaponCategory)
                    {
                        var category = t.Category;
                        p = p.ForWeapons(w => w.Category == category, owner);
                    }
                    if (t.CheckWeaponRangeType)
                    {
                        if (t.RangeType == Kingmaker.Enums.WeaponRangeType.Ranged) p.RangedOnly = true;
                        else if (t.RangeType == Kingmaker.Enums.WeaponRangeType.Melee) p.MeleeOnly = true;
                        else p.Conditional = true;
                    }
                    list.Parts.Add(p);
                }
                if (unknown) list.Unread.Add(nameof(AddInitiatorAttackWithWeaponTrigger));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WalkOnHit(BlueprintBuff owner, Kingmaker.ElementsSystem.GameAction[] actions, List<BuffPart> found, bool conditional,
                                      SavingThrowType save, int depth, ref bool unknown)
        {
            if (actions == null || depth > 4) return;
            foreach (var action in actions)
            {
                switch (action)
                {
                    // ★ v0.3.21 (review): a buff that kills its bearer as it starts is a kill (True Judgment).
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff killing when killing.Buff != null && !killing.ToCaster &&
                                                                                            CompanionAI_Pathfinder.Tactics.AbilityProfile.KillsOnStart(killing.Buff):
                        found.Add(new BuffPart { Kind = BuffPartKind.OnHitKill, Owner = owner, HitSave = save, Conditional = conditional });
                        break;
                    // A debuff on the target; anything else it puts there (a mark of the bearer's) stays unread.
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff apply when apply.Buff != null && !apply.ToCaster &&
                                                                                          CompanionAI_Pathfinder.Tactics.AbilityProfile.IsHarmfulBuff(apply.Buff):
                        found.Add(new BuffPart { Kind = BuffPartKind.OnHitBuff, Owner = owner, HitBuff = apply.Buff, HitSave = save, Conditional = conditional });
                        break;
                    // ★ v0.3.21 (review): an empty buff on the target is a cooldown marker (True Judgment's), no effect.
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff marker when marker.Buff != null &&
                                                                                           (marker.Buff.ComponentsArray?.All(x => x == null) ?? true):
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDealDamage deal when deal.Value != null && !deal.IsAoE:
                        found.Add(new BuffPart { Kind = BuffPartKind.Damage, Owner = owner, Dice = deal.Value, Energy = true, Conditional = conditional || save != SavingThrowType.Unknown });
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionKill _:
                        found.Add(new BuffPart { Kind = BuffPartKind.OnHitKill, Owner = owner, HitSave = save, Conditional = conditional });
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSavingThrow saving:
                        WalkOnHit(owner, saving.Actions?.Actions, found, conditional, saving.Type, depth + 1, ref unknown);
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionConditionalSaved saved:
                        WalkOnHit(owner, saved.Failed?.Actions, found, conditional, save, depth + 1, ref unknown);
                        break;
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional branch:
                        WalkOnHit(owner, branch.IfTrue?.Actions, found, true, save, depth + 1, ref unknown);
                        WalkOnHit(owner, branch.IfFalse?.Actions, found, true, save, depth + 1, ref unknown);
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionRemoveSelf _:
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionRemoveBuff _:
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionChangeSharedValue _:
                        break;
                    default:
                        unknown = true;
                        break;
                }
            }
        }

        /// <summary>
        /// ★ v0.3.21 (gaps): stat changes among a buff's start actions - directly, behind a save (the failed branch: the full
        /// change; a passed save halves it for Ray of Enfeeblement, so this is its upper end) or a Conditional (marked
        /// conditional).
        /// </summary>
        private static void ReadActivatedStats(BlueprintBuff owner, Kingmaker.ElementsSystem.GameAction[] actions, BuffPartList list,
                                               bool conditional, int depth)
        {
            if (actions == null || depth > 4) return;
            foreach (var action in actions)
            {
                switch (action)
                {
                    case Kingmaker.UnitLogic.Buffs.Actions.BuffActionAddStatBonus stat when stat.Value != null:
                    {
                        int before = list.Parts.Count;
                        AddStat(list, owner, stat.Stat, stat.Descriptor, 0, stat.Value, 1, false);
                        for (int i = before; i < list.Parts.Count; i++) list.Parts[i].Conditional |= conditional;
                        break;
                    }
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSavingThrow saving:
                        ReadActivatedStats(owner, saving.Actions?.Actions, list, conditional, depth + 1);
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionConditionalSaved saved:
                        ReadActivatedStats(owner, saved.Failed?.Actions, list, conditional, depth + 1);
                        break;
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional branch:
                        ReadActivatedStats(owner, branch.IfTrue?.Actions, list, true, depth + 1);
                        ReadActivatedStats(owner, branch.IfFalse?.Actions, list, true, depth + 1);
                        break;
                }
            }
        }

        /// <summary>★ v0.3.21: the parts of a feature a buff grants, marked with it (see <see cref="BuffPart.Granted"/>).</summary>
        /// <param name="fromCaster">★ log review: a fact of the caster's (see <see cref="BuffPart.CasterFact"/>).</param>
        private static void ReadGranted(BlueprintBuff owner, Kingmaker.Blueprints.Facts.BlueprintUnitFact fact, BuffPartList list, int depth,
                                        bool conditional = false, bool fromCaster = false)
        {
            // ★ v0.3.21 (log review): a granted ability or toggle is something to use, not an effect on the bearer.
            if (IsAbility(fact)) return;
            int before = list.Parts.Count, unreadBefore = list.Unread.Count;
            ReadInto(owner, fact.ComponentsArray, list, depth + 1);
            for (int i = before; i < list.Parts.Count; i++)
            {
                list.Parts[i].Granted = list.Parts[i].Granted ?? fact;
                if (conditional) list.Parts[i].Conditional = true;
                if (fromCaster) list.Parts[i].CasterFact = fact;
            }
            if (!fromCaster || list.Unread.Count == unreadBefore) return;
            for (int i = unreadBefore; i < list.Unread.Count; i++)
                list.CasterUnread.Add(new KeyValuePair<Kingmaker.Blueprints.Facts.BlueprintUnitFact, string>(fact, list.Unread[i]));
            list.Unread.RemoveRange(unreadBefore, list.Unread.Count - unreadBefore);
        }

        /// <summary>★ v0.3.21 (log review): an ability or a toggle (its components run when used, not on the bearer).</summary>
        private static bool IsAbility(Kingmaker.Blueprints.Facts.BlueprintUnitFact fact) =>
            fact is Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility ||
            fact is Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility;

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

        /// <summary>★ v0.3.21: an immunity reads as this many points of save bonus against its kind of effect.</summary>
        private const int ImmunitySaves = 10;
        /// <summary>★ v0.3.21: an energy immunity reads as resistance this high (no single hit gets through).</summary>
        private const int EnergyImmunityResist = 30;

        /// <summary>★ v0.3.21: the kind of effect that imposes a condition (for immunity to it).</summary>
        private static Kingmaker.Blueprints.Classes.Spells.SpellDescriptor ConditionDescriptor(Kingmaker.UnitLogic.UnitCondition condition)
        {
            switch (condition)
            {
                case Kingmaker.UnitLogic.UnitCondition.Paralyzed: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Paralysis;
                case Kingmaker.UnitLogic.UnitCondition.Stunned: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Stun;
                case Kingmaker.UnitLogic.UnitCondition.Confusion: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Confusion;
                case Kingmaker.UnitLogic.UnitCondition.Frightened:
                case Kingmaker.UnitLogic.UnitCondition.Cowering: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Fear | Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Frightened;
                case Kingmaker.UnitLogic.UnitCondition.Shaken: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Shaken;
                case Kingmaker.UnitLogic.UnitCondition.Nauseated: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Nauseated;
                case Kingmaker.UnitLogic.UnitCondition.Sickened: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Sickened;
                case Kingmaker.UnitLogic.UnitCondition.Blindness: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Blindness;
                case Kingmaker.UnitLogic.UnitCondition.Dazed: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Daze;
                case Kingmaker.UnitLogic.UnitCondition.Sleeping: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Sleep;
                case Kingmaker.UnitLogic.UnitCondition.Entangled:
                case Kingmaker.UnitLogic.UnitCondition.CantMove:
                case Kingmaker.UnitLogic.UnitCondition.Slowed: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.MovementImpairing;
                case Kingmaker.UnitLogic.UnitCondition.Petrified: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Petrified;
                case Kingmaker.UnitLogic.UnitCondition.Fatigued: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Fatigue;
                case Kingmaker.UnitLogic.UnitCondition.Exhausted: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Exhausted;
                case Kingmaker.UnitLogic.UnitCondition.Staggered: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Staggered;
                default: return Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None;
            }
        }

        private static readonly System.Reflection.FieldInfo OutgoingWeapon =
            HarmonyLib.AccessTools.Field(typeof(OutcomingDamageAndHealingModifier), "m_DamageWeaponType");
        private static readonly System.Reflection.FieldInfo OutgoingFaction =
            HarmonyLib.AccessTools.Field(typeof(OutcomingDamageAndHealingModifier), "m_SourceToTargetFactionType");
        private static readonly System.Reflection.FieldInfo OutgoingAoe =
            HarmonyLib.AccessTools.Field(typeof(OutcomingDamageAndHealingModifier), "m_AoeEntry");

        /// <summary>★ v0.3.21: 0 any weapon, 1 melee, 2 ranged (its private WeaponType).</summary>
        private static int OutgoingWeaponType(OutcomingDamageAndHealingModifier c)
        {
            try { return OutgoingWeapon == null ? 0 : Convert.ToInt32(OutgoingWeapon.GetValue(c)); }
            catch { return 0; }
        }

        /// <summary>★ v0.3.21: only against allies, or only for (or never for) area damage: counted at half.</summary>
        private static bool OutgoingLimited(OutcomingDamageAndHealingModifier c)
        {
            try
            {
                int faction = OutgoingFaction == null ? 0 : Convert.ToInt32(OutgoingFaction.GetValue(c));
                int aoe = OutgoingAoe == null ? 0 : Convert.ToInt32(OutgoingAoe.GetValue(c));
                return faction == 2 || aoe != 0;
            }
            catch { return true; }
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
            string name = c.GetType().Name;
            // ★ v0.3.21 (gaps): a granted feat's prerequisites, tags and recommendations (teamwork feats a Tactician shares)
            // are metadata, not effects.
            if (name.StartsWith("Prerequisite") || name.StartsWith("Recommendation")) return true;
            switch (name)
            {
                case "FeatureTagsComponent":
                // ★ v0.3.21 (review): the DC of the buff's own effects by class level (Master Slayer) - read through the
                // save, not an effect of its own.
                case "ContextCalculateAbilityParamsBasedOnClass":
                // ★ v0.3.21 (gaps): known teamwork effects with nothing the appraisal models - CMD and CMB (Coordinated
                // Defense / Maneuvers), concentration (Shielded Caster), an extra attack of opportunity (Seize the Moment).
                case "CoordinatedDefense":
                case "CoordinatedManeuvers":
                case "ShieldedCaster":
                case "SiezeTheMoment":
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
                // ★ v0.3.21: visual only (the model's bones, where a spell visibly leaves from)
                case "ReplaceSourceBone":
                case "ReplaceCastSource":
                // ★ v0.3.21 (gaps): the aura a buff carries is read by its area (AbilityProfile.Auras, toggles' grants)
                case "AddAreaEffect":
                    return true;
                default:
                    return false;
            }
        }
    }
}
