// ★ v0.3.0: What an ability actually does, read from its blueprint (cached per blueprint).
//
// The previous classifier walked only a hard-coded set of action containers and returned "no buff" for anything it
// did not recognise, which made unrecognised buffs look "never applied" (=> re-cast every turn) and let utility
// abilities such as Appraise Foe score as attacks. This walker recurses through ANY field of type ActionList /
// GameAction[] / GameAction and into referenced abilities (sticky-touch deliveries, ContextActionCastSpell, ...), and
// keeps "no detectable effect" as an explicit result instead of a silent default.
//
// ★ v0.3.1: effects behind a Conditional (Cure/Inflict Wounds: "undead → heal / else damage", Lay On Hands) carry the
// branch they sit in, and AppliesTo() evaluates that branch against a concrete target. v0.3.0 counted both branches
// for everyone, so Inflict Light Wounds was valued as a heal on living Camellia and as damage on an undead Skeletal
// Champion (where it heals). Also records what an ability removes (cleanses) and buffs that only last for the next
// attack (RemoveBuffOnAttack / ≤ 1 round, e.g. Perfect Strike).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <remarks>★ v0.3.18 (test10): Minor - a small penalty, not control (Shaken, Sickened, Dazzled, Fatigued: -1 / -2 to
    /// attacks, saves or stats).</remarks>
    public enum ControlStrength { None = 0, Minor = 1, Soft = 2, Hard = 3 }

    /// <summary>Result of evaluating an effect's Conditional branch for one target.</summary>
    public enum EffectApplies { Unconditional, Yes, No, Unknown }

    /// <summary>Whom a nested action runs on: the ability's target, its caster, or someone else (party, pet, random units).</summary>
    public enum EffectSubject { Target, Caster, Other, Summoned }

    /// <summary>One enclosing Conditional: the effect runs when the checker evaluates to <see cref="Branch"/>.</summary>
    public sealed class EffectGuard
    {
        public ConditionsChecker Checker;
        public bool Branch;
        /// <summary>The game evaluates this checker against the caster (Conditional inside ContextActionOnContextCaster).</summary>
        public bool OnCaster;
    }

    public sealed class AbilityProfile
    {
        public BlueprintAbility Blueprint { get; }

        /// <remarks>★ v0.3.18 (AA-1): besides ContextActionDealDamage, a kill (ContextActionKill: the target's HP) and any
        /// other action the game can preview (IDealDamageProvider: ContextActionDealWeaponDamage, Tabletop Tweaks'
        /// ContextActionDealDamageTTT, Mystical Mayhem's corrupting ray) - the same preview as the game's attack tooltip.</remarks>
        public readonly List<GameAction> Damage = new List<GameAction>();
        /// <summary>Heals of the ability's target(s). ★ v0.3.18: also actions the game previews (IHealDamageProvider).</summary>
        public readonly List<GameAction> Heals = new List<GameAction>();
        /// <summary>★ v0.3.1: heals run on the caster (ContextActionOnContextCaster, life drain) or on other units
        /// (party members, pet, random units around) - not on the ability's target.</summary>
        public readonly List<ContextActionHealTarget> CasterHeals = new List<ContextActionHealTarget>();
        /// <summary>Buffs applied to the ability's target(s).</summary>
        public readonly List<BlueprintBuff> TargetBuffs = new List<BlueprintBuff>();
        /// <summary>Buffs applied to the caster (ToCaster / ContextActionOnContextCaster).</summary>
        public readonly List<BlueprintBuff> CasterBuffs = new List<BlueprintBuff>();

        public bool Summons { get; private set; }
        /// <summary>★ v0.3.18 (test11): the creatures it summons (ContextActionSpawnMonster.Blueprint).</summary>
        public readonly List<Kingmaker.Blueprints.BlueprintUnit> SummonedUnits = new List<Kingmaker.Blueprints.BlueprintUnit>();
        public bool Dispels { get; private set; }
        // ★ v0.3.22: preserve the native dispel settings for safe, scoped probability queries.
        internal readonly List<ContextActionDispelMagic> DispelActions = new List<ContextActionDispelMagic>();
        public bool RemovesBuffs { get; private set; }
        public bool Enchants { get; private set; }

        /// <summary>
        /// ★ v0.3.21: a weapon enchantment it puts on the target's (or caster's) weapon in hand - EnhanceWeapon (Magic
        /// Weapon, Greater Magic Weapon, Keen Edge, Magic Fang's action form) or ContextActionEnchantWornItem on a hand slot
        /// (oils, Holy Sword, domain powers, Black Blade) - valued by its enchantment's components (BuffPartsReader.ReadEnchantment).
        /// </summary>
        public sealed class WeaponEnchantEffect
        {
            public Kingmaker.Blueprints.Items.Ecnchantments.BlueprintItemEnchantment[] Enchantments;
            /// <summary>Greater: the enchantment at EnchantLevel (1-based, capped at the list) - EnhanceWeapon.RunAction.</summary>
            public bool Greater;
            public ContextValue EnchantLevel;
            /// <summary>Which of the recipient's weapons: natural and unarmed (Magic Fang), or the one in its main hand.</summary>
            public bool Natural;
            /// <summary>The off-hand version: the main hand's weapon only when it is two-handed (the off hand's attacks are
            /// not valued on their own).</summary>
            public bool OffHand;
            public bool OnCaster;
            public ContextDurationValue Duration;
            public bool Permanent;
        }

        public readonly List<WeaponEnchantEffect> WeaponEnchants = new List<WeaponEnchantEffect>();

        /// <summary>★ v0.3.8: the item enchantments it applies (ContextActionEnchantWornItem) and on whom.</summary>
        private readonly List<KeyValuePair<ContextActionEnchantWornItem, bool>> m_Enchantments =
            new List<KeyValuePair<ContextActionEnchantWornItem, bool>>();

        /// <summary>
        /// ★ v0.3.8: every item enchantment this ability applies is already on the item (the game's own action skips or
        /// merely refreshes it), or there is no item to enchant (ContextActionEnchantWornItem does nothing then). Null when the enchantments are unknown (another mod's action, a buff component).
        /// Camellia recast Spirit Weapon Enchantment at the start of every fight while it was still active.
        /// </summary>
        public bool? EnchantmentsPresent(UnitEntityData caster, UnitEntityData target)
        {
            if (m_Enchantments.Count == 0) return null;
            try
            {
                foreach (var entry in m_Enchantments)
                {
                    var owner = entry.Value ? caster : target;
                    var bp = entry.Key.Enchantment;
                    if (owner?.Body == null || bp == null) return null;
                    var slot = Kingmaker.UI.GenericSlot.EquipSlotBase.ExtractSlot(entry.Key.Slot, owner.Body);
                    if (slot == null || !slot.HasItem) continue;          // nothing to enchant: the cast would do nothing
                    if (!slot.Item.HasEnchantment(bp)) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] {Blueprint.name} enchantment check: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ★ v0.3.8: the enchantment is already in place: its item enchantments are on the item, or (unknown
        /// enchantments) every buff that carries it is already on its recipient.
        /// </summary>
        public bool EnchantmentActive(UnitEntityData caster, UnitEntityData target)
        {
            var present = EnchantmentsPresent(caster, target);
            if (present.HasValue) return present.Value;
            if (TargetBuffs.Count == 0 && CasterBuffs.Count == 0) return false;
            return TargetBuffs.All(b => target != null && target.Buffs.HasFact(b)) &&
                   CasterBuffs.All(b => caster != null && caster.Buffs.HasFact(b));
        }

        /// <summary>★ v0.3.3: lowers the caster's kineticist burn (ContextActionHealBurn, e.g. Dark Elementalist Soul Power).</summary>
        public bool HealsBurn { get; private set; }

        /// <summary>
        /// ★ v0.3.3: applies and removes the same buff depending on whether it is already there - an on/off switch
        /// (Isekai's "Shift Form: Overlord / Mortal Guise"). Never cast outside combat by the permanent-buff routine.
        /// </summary>
        public bool IsToggle => RemovedBuffs.Any(b => TargetBuffs.Contains(b) || CasterBuffs.Contains(b));

        /// <summary>★ v0.3.5: buffs applied permanently or for hours/days (worth keeping up outside combat).</summary>
        private readonly HashSet<BlueprintBuff> m_PersistentBuffs = new HashSet<BlueprintBuff>();
        public bool IsPersistent(BlueprintBuff buff) => buff != null && m_PersistentBuffs.Contains(buff);

        /// <summary>Buffs applied for a day or more ("treated today" markers such as Treat Affliction's).</summary>
        private readonly HashSet<BlueprintBuff> m_DayLongBuffs = new HashSet<BlueprintBuff>();
        public bool IsDayLong(BlueprintBuff buff) => buff != null && m_DayLongBuffs.Contains(buff);

        /// <summary>Descriptors of buffs the ability removes from its target (ContextActionRemoveBuffsByDescriptor).</summary>
        public SpellDescriptor RemovedDescriptors { get; private set; }
        /// <summary>Specific buffs the ability removes from its target (ContextActionRemoveBuff without ToCaster).</summary>
        public readonly List<BlueprintBuff> RemovedBuffs = new List<BlueprintBuff>();

        /// <summary>Removes buffs/conditions from its target and neither damages nor heals: its worth is what it removes.</summary>
        public bool RemovesFromTarget => RemovesAnything && Damage.Count == 0 && Heals.Count == 0;

        /// <summary>Removes buffs/conditions from its target (possibly alongside a heal, e.g. Lay On Hands mercies).</summary>
        public bool RemovesAnything => RemovedDescriptors != SpellDescriptor.None || RemovedBuffs.Count > 0;

        /// <summary>
        /// Removes whole classes of conditions by descriptor (fear, poison, disease...). The buffs such an ability applies
        /// (protection, cooldown markers) only matter together with the cleanse. Removing one specific buff, by contrast,
        /// is often a side effect of a real buff (e.g. a haste effect removing slow) and does not make it a cleanse.
        /// </summary>
        public bool IsCleanse => RemovedDescriptors != SpellDescriptor.None && Damage.Count == 0 && Heals.Count == 0;

        /// <summary>Every buff it applies lasts at most one round: it only helps what happens this turn (and this round).</summary>
        public bool BuffsLastOneRound { get; private set; }
        /// <summary>Every buff it applies is spent by its owner's next attack roll (RemoveBuffOnAttack, e.g. Perfect Strike).</summary>
        public bool BuffsSpentByAttack { get; private set; }
        /// <summary>
        /// ★ v0.3.19 (test9): every buff it applies removes itself on its owner's next d20 roll of any kind - an attack
        /// roll, save or skill check (AddInitiator...RollTrigger / SavingThrowTrigger running ContextActionRemoveSelf):
        /// Guidance. One roll's worth; valued as a one-minute buff, Camellia recast it on Orvath in rounds 2, 3 and 4.
        /// </summary>
        public bool BuffsSpentByRoll { get; private set; }
        /// <summary>
        /// ★ v0.3.19 (test9): every buff it applies removes itself when its owner next casts (AddAbilityUseTrigger running
        /// ContextActionRemoveSelf): Arcane Reservoir's +1 caster level or DC for the next spell.
        /// </summary>
        public bool BuffsSpentByCast { get; private set; }
        /// <summary>One round or one attack's (or roll's) worth of effect.</summary>
        public bool IsOneShot => BuffsLastOneRound || BuffsSpentByAttack || BuffsSpentByRoll;

        public SavingThrowType Save { get; private set; } = SavingThrowType.Unknown;
        /// <summary>★ v0.3.18 (AA-4): Demoralize / Dazzling Display - an Intimidate check, not a save, puts its buff on the
        /// target (CombatMath.IntimidateOdds); the buff's duration comes from the check's result.</summary>
        public Demoralize Intimidation { get; private set; }

        /// <summary>★ v0.3.18 (AA-4): the chain delivery: after its first target, the units nearest that target within its
        /// radius, up to its target count (AbilityDeliverChain.SelectNextTargetByMinDistance), of <see cref="ChainTargets"/>.</summary>
        public AbilityDeliverChain Chain { get; private set; }
        // ★ v0.3.22: conditional ricochets (including Chain Arrows) use native count and next-target selection.
        internal readonly List<AbilityDeliverRicochet> Ricochets = new List<AbilityDeliverRicochet>();
        public Kingmaker.UnitLogic.Abilities.Components.TargetType ChainTargets { get; private set; }

        /// <summary>★ v0.3.18 (AA-4): a combat maneuver the ability makes against its target.</summary>
        public sealed class ManeuverEffect
        {
            public Kingmaker.RuleSystem.Rules.CombatManeuver Type;
            /// <summary>The action (its branch conditions); ContextActionCombatManeuver also carries the stat replacement.</summary>
            public GameAction Action;
        }

        /// <summary>★ v0.3.18 (AA-4): the combat maneuvers it makes (ContextActionCombatManeuver, its custom version,
        /// ContextActionDisarm): trip, disarm, sunder, dirty tricks, bull rush (Hydraulic Push)...</summary>
        public readonly List<ManeuverEffect> Maneuvers = new List<ManeuverEffect>();
        public bool HalfOnSave { get; private set; }
        public bool NeedsAttackRoll { get; private set; }
        /// <summary>
        /// ★ v0.3.18: delivered by a projectile with an attack roll (rays, kinetic blasts): a ranged touch attack roll, which
        /// provokes attacks of opportunity from enemies engaging the caster even when the spell was cast defensively.
        /// </summary>
        public bool RangedAttackRoll { get; private set; }
        public bool IsCharge { get; private set; }
        /// <summary>★ v0.3.18 (AA-4): the game's custom weapon attacks - Vital Strike (one attack with extra weapon dice)
        /// and Cleave (attacks along the enemies in reach until one misses).</summary>
        public AbilityCustomVitalStrike VitalStrike { get; private set; }
        public AbilityCustomCleave Cleave { get; private set; }
        /// <summary>★ v0.3.21: Devastating Blow (AbilityCustomMeleeAttack, its only type): one attack with the melee hand at
        /// -5 that always threatens a critical hit.</summary>
        public bool DevastatingBlow { get; private set; }
        /// <summary>★ v0.3.21: a melee attack the ability makes on its target (ContextActionMeleeAttack: Piledriver, Death
        /// Attack, Blade from Shadows, Disarming Strike), with its branch conditions.</summary>
        public ContextActionMeleeAttack MeleeAttack { get; private set; }
        /// <summary>★ v0.3.21: the melee attack is all it does (valued as the weapon attack it is); with other effects it is
        /// one more part of the ability's value.</summary>
        public bool OnlyMeleeAttack => MeleeAttack != null && Damage.Count == 0 && Heals.Count == 0 && TargetBuffs.Count == 0 &&
                                       Maneuvers.Count == 0 && !Summons && !Dispels && !RemovesBuffs;
        public bool IsWeaponAttack => VitalStrike != null || Cleave != null || DevastatingBlow || OnlyMeleeAttack;

        public float AoERadius { get; private set; }
        public Kingmaker.UnitLogic.Abilities.Components.TargetType AoETargets { get; private set; } =
            Kingmaker.UnitLogic.Abilities.Components.TargetType.Any;
        public bool IsAoE => AoERadius > 0.01f;
        /// <summary>
        /// ★ v0.3.18: the game picks the units in the area one by one (AbilityTargetsAround), each with its own spell
        /// resistance check. Without it (spawned areas: Grease, Entangle, Web) the effect is applied once, to the aimed
        /// target: a creature aimed at that resists or is immune stops the whole area.
        /// </summary>
        public bool SelectsTargets { get; private set; }

        public CCType Control { get; private set; }

        /// <summary>★ v0.3.21 (gaps): a buff the ability applies that carries an aura (AddAreaEffect) around its bearer.</summary>
        public sealed class AuraEffect
        {
            public BlueprintBuff Carrier;
            public BlueprintAbilityAreaEffect Area;
            /// <summary>The bearer is the caster (a personal ability, or applied to the caster); otherwise the target.</summary>
            public bool OnCaster;
        }

        /// <summary>★ v0.3.21 (gaps): the auras its buffs carry.</summary>
        public readonly List<AuraEffect> Auras = new List<AuraEffect>();

        /// <summary>★ v0.3.21: the pit its area digs (AreaEffectPit), if any.</summary>
        public Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AreaEffectPit Pit { get; private set; }

        /// <summary>★ v0.3.21: <paramref name="unit"/> can fall into this ability's pit (not already in one, not larger than
        /// the pit takes, not immune by the pit's own lists - UnitInPitController.HandleEvade).</summary>
        public bool PitHolds(UnitEntityData unit)
        {
            if (Pit == null || unit == null) return false;
            try
            {
                if (unit.Get<Kingmaker.UnitLogic.Parts.UnitPartInPit>() != null) return false;
                if (unit.Descriptor.State.Size > Pit.MaxUnitSize) return false;
                foreach (var f in Pit.EvadingImmunityFacts.Concat(Pit.EffectsImmunityFacts))
                    if (f?.Get() != null && unit.Descriptor.HasFact(f.Get())) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
        public ControlStrength ControlStrength { get; private set; }

        /// <summary>
        /// ★ v0.3.21: flagged harmful to enemies and able to reach one. The witch and shaman hexes aimed at allies (Ward,
        /// Fortune, Protective Luck, Fury, Battle Ward, Draconic Resilience) carry EffectOnEnemy and EffectOnAlly "Harmful"
        /// in the game's blueprints: read as harmful, they were only offered against enemies, which they cannot target,
        /// and were never cast.
        /// </summary>
        public bool HarmfulToEnemies => Blueprint.EffectOnEnemy == AbilityEffectOnUnit.Harmful && !AimsAtFriendsOnly(Blueprint) && !OnlyOnCaster &&
                                        !(IsAoE && AoETargets == Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally && !DealsDamage);
        // ★ v0.3.21 (verification audit): ...nor an area that reaches only allies (Holy Aura, Unholy Aura, Shield of Law,
        // Cloak of Chaos are flagged harmful to enemies: their buffs were never valued, only the caster's own).

        /// <summary>
        /// ★ v0.3.21 (verification audit): a personal ability that reaches nobody but its caster - no area, no damage, no
        /// maneuver or weapon attack, no spawned area. The same mislabel as the ally hexes: Wizard Abjuration's Energy
        /// Resistance, Transmutation's Physical Enhancement, Protective Ward, the Arcanist's Energy Shield, Frightful Aspect,
        /// Firebelly, the Battle Spirit are flagged harmful to enemies; read as attacks on a personal ability, they were never
        /// offered (TacticalEngine.EvaluateAbility puts a harmful personal ability only on the caster when it is an area).
        /// </summary>
        public bool OnlyOnCaster => Blueprint.Range == AbilityRange.Personal && !IsAoE && !DealsDamage && Maneuvers.Count == 0 &&
                                    !IsWeaponAttack && !m_VisitedAreas.Any();

        /// <summary>★ v0.3.21: the ability can only be aimed at a friend or the caster and reaches nobody else (no point, no
        /// area around the target, not personal): its intent flags say nothing about enemies.</summary>
        public static bool AimsAtFriendsOnly(BlueprintAbility bp)
        {
            if (bp == null || bp.CanTargetEnemies || bp.CanTargetPoint || bp.Range == AbilityRange.Personal) return false;
            if (!bp.CanTargetFriends && !bp.CanTargetSelf) return false;
            return bp.GetComponent<AbilityTargetsAround>() == null && bp.GetComponent<AbilityDeliverChain>() == null;
        }
        /// <summary>★ v0.3.21 (verification audit): flagged helpful to allies and able to reach one. The Assassin's Death Attack
        /// is flagged helpful to allies and can only target an enemy: read as helpful only, it was never offered.</summary>
        public bool HelpfulToAllies => Blueprint.EffectOnAlly == AbilityEffectOnUnit.Helpful && !AimsAtEnemiesOnly(Blueprint);

        /// <summary>★ v0.3.21: the ability can only be aimed at an enemy and reaches nobody else.</summary>
        public static bool AimsAtEnemiesOnly(BlueprintAbility bp)
        {
            if (bp == null || !bp.CanTargetEnemies || bp.CanTargetFriends || bp.CanTargetSelf || bp.CanTargetPoint ||
                bp.Range == AbilityRange.Personal) return false;
            return bp.GetComponent<AbilityTargetsAround>() == null && bp.GetComponent<AbilityDeliverChain>() == null;
        }

        /// <summary>★ v0.3.12: a buff it applies to its target is a debuff or a controlling condition.</summary>
        public bool AppliesHarmfulBuffs { get; private set; }

        /// <summary>★ v0.3.12: harmful by intent flag or by what it applies (damage is checked separately).</summary>
        public bool HarmfulEffects => HarmfulToEnemies || AppliesHarmfulBuffs;

        /// <summary>
        /// ★ v0.3.14: damages, controls or debuffs <paramref name="target"/>: what the turn-based engine calls offensive
        /// (its "dmg" / "cc" / "debuff" reasons), from the effects instead of a score. Real time's Progressive scope came
        /// from its own classifier bucket (AbilityAttack / Debuff), so the same spell could be gated in one mode and not
        /// the other. Per-target branches count (Cure Light Wounds is offensive on an undead enemy only).
        /// </summary>
        public bool IsOffensiveOn(AbilityData ability, UnitEntityData caster, UnitEntityData target)
        {
            if (ability == null || caster == null || target == null || !caster.IsEnemy(target)) return false;
            if (Damage.Any(d => AppliesTo(d, ability, target) != EffectApplies.No)) return true;
            if (Maneuvers.Any(m => AppliesTo(m.Action, ability, target) != EffectApplies.No)) return true;   // ★ v0.3.18 (AA-4)
            if (IsWeaponAttack) return true;
            if (!HarmfulEffects && ControlStrength == ControlStrength.None) return false;
            return TargetBuffs.Any(b => BuffAppliesTo(b, ability, target) != EffectApplies.No);
        }

        /// <summary>Longest buff duration category we saw (rounds = short, minutes or longer = long).</summary>
        public bool HasLongBuff { get; private set; }

        public bool HasDetectableEffect =>
            Damage.Count > 0 || Heals.Count > 0 || CasterHeals.Count > 0 || TargetBuffs.Count > 0 || CasterBuffs.Count > 0 ||
            Summons || Dispels || RemovesBuffs || Enchants || Maneuvers.Count > 0 || IsWeaponAttack || Extensions.Count > 0;

        /// <summary>★ v0.3.19 (class audit B2): buffs it lengthens on its targets, and by how much (Cackle, Chant).</summary>
        public readonly List<KeyValuePair<BlueprintBuff, ContextDurationValue>> Extensions = new List<KeyValuePair<BlueprintBuff, ContextDurationValue>>();

        public bool DealsDamage => Damage.Count > 0;

        private readonly Dictionary<object, EffectGuard[]> m_Guards = new Dictionary<object, EffectGuard[]>();
        /// <summary>
        /// The branches each target buff is applied in. ★ v0.3.17 (test6): every branch is kept - the buff applies when
        /// any of them does. A buff found in two branches used to lose all its conditions ("unconditional"): Sleep's buff
        /// is applied in more than one branch, so its Hit Dice limit was never checked and Orvath kept casting it at a
        /// Cultist Champion (level 5) over the limit.
        /// </summary>
        private readonly Dictionary<BlueprintBuff, List<EffectGuard[]>> m_BuffGuards = new Dictionary<BlueprintBuff, List<EffectGuard[]>>();

        private void AddBuffGuards(BlueprintBuff buff, EffectGuard[] guards)
        {
            if (!m_BuffGuards.TryGetValue(buff, out var branches)) m_BuffGuards[buff] = branches = new List<EffectGuard[]>();
            branches.Add(guards ?? NoGuards);
        }
        private static readonly EffectGuard[] NoGuards = new EffectGuard[0];
        private int m_ShortBuffs, m_SpentByAttackBuffs, m_SpentByRollBuffs, m_SpentByCastBuffs, m_AllBuffs;
        private float m_AreaRadius;
        private bool m_AreaHitsAllies, m_AreaHitsEnemies;
        /// <summary>★ v0.3.7: walking a spawned area's actions (its buffs last while the area does, not for minutes).</summary>
        private bool m_InArea;
        private readonly HashSet<BlueprintAbilityAreaEffect> m_VisitedAreas = new HashSet<BlueprintAbilityAreaEffect>();

        /// <summary>★ v0.3.13: the areas this ability spawns (Entangle, Grease, Web...).</summary>
        public IEnumerable<BlueprintAbilityAreaEffect> SpawnedAreas => m_VisitedAreas;
        internal readonly HashSet<BlueprintAbilityAreaEffect> PeriodicAreas = new HashSet<BlueprintAbilityAreaEffect>();
        internal readonly Dictionary<BlueprintAbilityAreaEffect, ContextDurationValue> AreaDurations =
            new Dictionary<BlueprintAbilityAreaEffect, ContextDurationValue>();

        /// <summary>
        /// ★ v0.3.13: <paramref name="unit"/> stands in a still-active area of this ability that one of
        /// <paramref name="caster"/>'s side put down. Orvath cast Entangle twice in one turn on the same enemy (quickened,
        /// then again with his standard action): a second copy of the same area adds nothing the first does not do.
        /// </summary>
        public bool InOwnSideArea(UnitEntityData caster, UnitEntityData unit)
        {
            if (m_VisitedAreas.Count == 0 || caster == null || unit == null) return false;
            try
            {
                var areas = Kingmaker.Game.Instance?.State?.AreaEffects;
                if (areas == null) return false;
                foreach (var area in areas)
                {
                    if (area == null || area.IsEnded || !m_VisitedAreas.Contains(area.Blueprint)) continue;
                    var owner = area.Context?.MaybeCaster;
                    if (owner == null || owner.IsEnemy(caster)) continue;
                    if (area.InGameUnitsInside.Contains(unit)) return true;
                    // A fresh area may not have registered the units inside it yet: also by distance.
                    if (AoERadius > 0f && Vector3.Distance(area.Position, unit.Position) <= AoERadius + unit.Corpulence) return true;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] {Blueprint.name} area check: {ex.Message}");
            }
            return false;
        }

        /// <summary>Removal actions (ContextActionRemoveBuff / ...ByDescriptor) on the target, with their branch.</summary>
        private readonly List<GameAction> m_Removals = new List<GameAction>();

        private AbilityProfile(BlueprintAbility blueprint)
        {
            Blueprint = blueprint;
        }

        /// <summary>★ v0.3.22: actions on one infusion damage trigger, through the normal action reader.
        /// Delivery and the chance of the blast reaching a recipient are priced by the caller, separately from
        /// this trigger's own save. In particular an infusion's Reflex save must not halve the blast's damage.</summary>
        public bool IsKineticEffect { get; private set; }

        private static readonly Dictionary<Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger,
            Dictionary<BlueprintAbility, AbilityProfile>> InfusionProfiles =
            new Dictionary<Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger,
                Dictionary<BlueprintAbility, AbilityProfile>>();

        public static AbilityProfile ForKineticInfusion(BlueprintAbility blast,
            Kingmaker.UnitLogic.Mechanics.Components.AddKineticistInfusionDamageTrigger trigger)
        {
            if (blast == null || trigger == null) return null;
            if (!InfusionProfiles.TryGetValue(trigger, out var profiles))
                InfusionProfiles[trigger] = profiles = new Dictionary<BlueprintAbility, AbilityProfile>();
            if (profiles.TryGetValue(blast, out var cached)) return cached;
            var profile = new AbilityProfile(blast) { IsKineticEffect = true };
            var delivery = Get(blast);
            profile.AoERadius = delivery.AoERadius;
            profile.AoETargets = delivery.AoETargets;
            profile.Chain = delivery.Chain;
            profile.Ricochets.AddRange(delivery.Ricochets);
            profile.ChainTargets = delivery.ChainTargets;
            profile.m_AreaCondition = delivery.m_AreaCondition;
            foreach (var area in delivery.m_VisitedAreas) profile.m_VisitedAreas.Add(area);
            foreach (var area in delivery.PeriodicAreas) profile.PeriodicAreas.Add(area);
            foreach (var duration in delivery.AreaDurations) profile.AreaDurations[duration.Key] = duration.Value;
            profile.Walk(trigger.Actions?.Actions, new HashSet<BlueprintAbility>(), 0, EffectSubject.Target, 0,
                         new EffectGuard[0]);
            profile.BuffsLastOneRound = profile.m_AllBuffs > 0 && profile.m_ShortBuffs == profile.m_AllBuffs;
            profile.RefreshControl();
            profiles[blast] = profile;
            return profile;
        }

        // ★ v0.3.22: a delivered buff can deal damage on later rounds. Its round action is read by Walk too,
        // with its own save; it must not change the delivery blast's save or damage.
        internal sealed class PeriodicEffect
        {
            internal BlueprintBuff Buff;
            internal AbilityProfile Tick;
            internal bool SaveEnds;
        }
        internal readonly List<PeriodicEffect> PeriodicEffects = new List<PeriodicEffect>();
        private readonly HashSet<BlueprintBuff> m_ActivatedBuffs = new HashSet<BlueprintBuff>();
        private bool? m_SavedBranch;
        internal bool BuffOnBothSaveOutcomes => IsKineticEffect && TargetBuffs.Count > 0 && Damage.Count == 0 && Maneuvers.Count == 0 &&
            TargetBuffs.All(b => m_BuffApplications.Any(a => a.Buff == b && a.Saved == true) &&
                                m_BuffApplications.Any(a => a.Buff == b && a.Saved == false));
        private readonly List<ContextDiceValue> m_Repetitions = new List<ContextDiceValue>();
        private readonly Dictionary<BlueprintBuff, ContextDiceValue[]> m_BuffRepetitions = new Dictionary<BlueprintBuff, ContextDiceValue[]>();

        internal bool AddsBuff(BlueprintBuff buff, UnitEntityData target) => !target.Buffs.HasFact(buff) ||
            IsKineticEffect && buff.Stacking == Kingmaker.UnitLogic.Buffs.Blueprints.StackingType.Rank &&
            target.Buffs.GetBuff(buff).GetRank() < buff.Ranks;

        internal float AddedBuffRanks(BlueprintBuff buff, MechanicsContext context, UnitEntityData target)
        {
            if (!IsKineticEffect || buff.Stacking != Kingmaker.UnitLogic.Buffs.Blueprints.StackingType.Rank) return 1f;
            float count = 1f;
            if (m_BuffRepetitions.TryGetValue(buff, out var values))
                foreach (var value in values) count *= CombatMath.EvaluateDice(value, context, context?.Params?.CasterLevel ?? 1);
            return Math.Max(0f, Math.Min(count, buff.Ranks - (target.Buffs.GetBuff(buff)?.GetRank() ?? 0)));
        }

        #region Cache

        private static readonly Dictionary<BlueprintAbility, AbilityProfile> Cache = new Dictionary<BlueprintAbility, AbilityProfile>();

        public static AbilityProfile Get(AbilityData ability)
        {
            return ability?.Blueprint == null ? null : Get(ability.Blueprint);
        }

        public static AbilityProfile Get(BlueprintAbility blueprint)
        {
            if (blueprint == null) return null;
            if (Cache.TryGetValue(blueprint, out var cached)) return cached;

            var profile = new AbilityProfile(blueprint);
            try
            {
                profile.Analyze();
            }
            catch (Exception ex)
            {
                // ★ v0.3.12: logged without debug mode (once: the profile is cached). A profile that failed half-way is
                // still used, so this line is the only evidence that its valuation may be incomplete.
                Main.Log($"[Profile] {blueprint.name} ({blueprint.AssetGuid}): analysis error {ex.GetType().Name}: {ex.Message} - effects may be incomplete");
            }
            Cache[blueprint] = profile;
            _built++;
            return profile;
        }

        private static int _built;

        /// <summary>★ v0.3.17: profiles built since the last call (the slow-decision log names the cause).</summary>
        public static int TakeBuiltCount()
        {
            int n = _built;
            _built = 0;
            return n;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            InfusionProfiles.Clear();
            ClearConditionCache();
        }

        /// <summary>Per-target condition results are keyed by unit id; drop them when a combat ends.</summary>
        public static void ClearConditionCache()
        {
            CheckerCache.Clear();
            // ★ v0.3.22: a nested modifier projection/restoration also invalidates detached cast contexts.
            for (var scope = s_AimScope; scope != null; scope = scope.Previous)
            { scope.Contexts.Clear(); scope.Results.Clear(); }
        }

        #endregion

        #region Analysis

        private void Analyze()
        {
            var visited = new HashSet<BlueprintAbility>();
            AnalyzeAbility(Blueprint, visited, 0, EffectSubject.Target);
            BuffsLastOneRound = m_AllBuffs > 0 && m_ShortBuffs == m_AllBuffs;
            BuffsSpentByAttack = m_AllBuffs > 0 && m_SpentByAttackBuffs == m_AllBuffs;
            BuffsSpentByRoll = m_AllBuffs > 0 && !BuffsSpentByAttack && m_SpentByRollBuffs == m_AllBuffs;
            BuffsSpentByCast = m_AllBuffs > 0 && m_SpentByCastBuffs == m_AllBuffs;
            MarksForOwnAttacks = TargetBuffs.Any(IsCasterOnlyAttackMark);

            // A personal ability's caster IS its target: its caster heals are heals of the target.
            if (Blueprint.Range == AbilityRange.Personal && CasterHeals.Count > 0)
            {
                Heals.AddRange(CasterHeals);
                CasterHeals.Clear();
            }

            IsCharge = Blueprint.GetComponent<AbilityCustomCharge>() != null;
            VitalStrike = Blueprint.GetComponent<AbilityCustomVitalStrike>();
            Cleave = Blueprint.GetComponent<AbilityCustomCleave>();
            DevastatingBlow = IsDevastatingBlow(Blueprint.GetComponent<AbilityCustomMeleeAttack>());
            CasterProvokes = FindCasterProvoke(Blueprint);

            var around = Blueprint.GetComponent<AbilityTargetsAround>();
            SelectsTargets = around != null;
            if (around != null)
            {
                AoERadius = around.AoERadius.Meters;
                AoETargets = around.Targets;
                // ★ v0.3.8: the area's own filter on who is affected (AbilityTargetsAround.Select checks it per unit).
                try
                {
                    var condition = HarmonyLib.AccessTools.Field(typeof(AbilityTargetsAround), "m_Condition")?.GetValue(around) as ConditionsChecker;
                    if (condition?.Conditions != null && condition.Conditions.Length > 0)
                        m_AreaCondition = condition;
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Profile] {Blueprint.name} area condition: {ex.Message}");
                }
            }
            else if (m_AreaHitsAllies || m_AreaHitsEnemies)
            {
                // ★ v0.3.7: a spawned area (Grease, Web, Stinking Cloud) is the spell's area of effect. The blueprint's
                // own AoERadius (what the targeting circle shows) is preferred over the area's size.
                float shown = 0f;
                try { shown = Blueprint.AoERadius.Meters; } catch { }
                AoERadius = shown > 0f ? shown : m_AreaRadius;
                AoETargets = !m_AreaHitsAllies ? Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy
                           : !m_AreaHitsEnemies ? Kingmaker.UnitLogic.Abilities.Components.TargetType.Ally
                           : Kingmaker.UnitLogic.Abilities.Components.TargetType.Any;
            }

            RefreshControl();
        }

        private void RefreshControl()
        {
            // Control: the spell's own descriptor plus the descriptors / conditions of what it applies.
            SpellDescriptor descriptor = IsKineticEffect ? SpellDescriptor.None : Blueprint.SpellDescriptor;
            foreach (var buff in TargetBuffs)
            {
                var comp = buff.GetComponent<SpellDescriptorComponent>();
                if (comp != null) descriptor |= comp.Descriptor.Value;
            }
            Control = AbilityClassifier.AnalyzeCCTypeFromDescriptor(descriptor);
            ControlStrength = DescriptorControl(descriptor);
            foreach (var buff in TargetBuffs)
            {
                var s = BuffControl(buff);
                if (s > ControlStrength) ControlStrength = s;
            }
            // ★ v0.3.21 (verification audit): a pit holds who falls in - prone at its bottom until it climbs out (DC 25,
            // UnitInPitController) - soft control, as Entangle is.
            if (Pit != null && ControlStrength < ControlStrength.Soft) ControlStrength = ControlStrength.Soft;

            // ★ v0.3.12: what it puts on its target is harmful, whatever the blueprint's intent flags say. Orvath's
            // Entangle (not flagged Harmful) was cast on himself as a "buff" of 9.4: its Entangled buff counted as an
            // unrecognised beneficial buff. Abilities flagged helpful to allies, and personal abilities (their target
            // is the caster: a condition there is a drawback, not an attack), keep their drawbacks out of this.
            // ★ v0.3.21 (verification audit): nor abilities that can only be aimed at allies - their harmful buff is the
            // price of the effect, not an attack: Lesser Restoration turns Exhausted into Fatigued, the Devil's Hell's Decree
            // dazzles the ally it empowers. Read as attacks, they were never offered.
            AppliesHarmfulBuffs = !HelpfulToAllies && Blueprint.Range != AbilityRange.Personal && !AimsAtFriendsOnly(Blueprint) &&
                                  TargetBuffs.Any(IsHarmfulBuff);
        }

        /// <summary>
        /// ★ v0.3.18 (AA-1): using the ability makes the caster provoke attacks of opportunity: the game's own
        /// ContextActionProvokeAttackOfOpportunity on the caster, in any action list of the blueprint's components -
        /// Tabletop Tweaks' Coup de Grace (its CoupDeGraceTTT component) provokes this way; the vanilla one does not.
        /// </summary>
        public bool CasterProvokes { get; private set; }

        private static bool FindCasterProvoke(BlueprintAbility bp)
        {
            try
            {
                foreach (var component in bp.ComponentsArray ?? new BlueprintComponent[0])
                {
                    if (component == null) continue;
                    foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (field.FieldType != typeof(ActionList)) continue;
                        if (ContainsCasterProvoke((field.GetValue(component) as ActionList)?.Actions, 0)) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] {bp?.name} provoke check: {ex.Message}");
            }
            return false;
        }

        private static bool ContainsCasterProvoke(GameAction[] actions, int depth) =>
            ContainsAction(actions, a => a is ContextActionProvokeAttackOfOpportunity provoke && provoke.ApplyToCaster, depth);

        /// <summary>★ v0.3.20 (test5): whether an action matching <paramref name="match"/> is in <paramref name="actions"/>
        /// or any action list nested inside them (conditionals, saves, random picks).</summary>
        internal static bool ContainsAction(GameAction[] actions, Func<GameAction, bool> match, int depth = 0)
        {
            if (actions == null || depth > 6) return false;
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (match(action)) return true;
                foreach (var child in ChildActionLists(action))
                    if (ContainsAction(child, match, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>★ v0.3.18: the buff a toggle-buff effect component switches (a "Buff" property or "m_Buff" field).</summary>
        private static BlueprintBuff ToggledBuff(BlueprintComponent component)
        {
            try
            {
                var type = component.GetType();
                object value = type.GetProperty("Buff", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component)
                               ?? type.GetField("m_Buff", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component);
                if (value is BlueprintBuff buff) return buff;
                if (value is BlueprintBuffReference reference) return reference.Get();
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] toggle buff of {component?.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>★ v0.3.12: conditions a buff puts on its owner (AddCondition, BuffStatusCondition).</summary>
        private static IEnumerable<UnitCondition> ConditionsOf(BlueprintBuff buff)
        {
            foreach (var c in buff.GetComponents<AddCondition>()) yield return c.Condition;
            foreach (var c in buff.GetComponents<Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition>()) yield return c.Condition;
        }

        /// <summary>★ v0.3.12: flagged harmful by its blueprint, or it imposes a controlling condition.</summary>
        internal static bool IsHarmfulBuff(BlueprintBuff buff)
        {
            try
            {
                return buff.Harmful || BuffControl(buff) != ControlStrength.None ||   // ★ v0.3.21: faction change, Command too
                       TakesAwayOnly(buff) || KillsOnStart(buff);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ★ v0.3.21 (audit review): a buff not flagged harmful whose effect on its bearer only takes away - every part the
        /// reader values is a penalty (Symbol of Vulnerability: -4 saves), or it strips concealment (Faerie Fire, Symbol of
        /// Revelation: DoNotBenefitFromConcealment). Read as helpful, the symbols were aimed at the party.
        /// </summary>
        private static bool TakesAwayOnly(BlueprintBuff buff)
        {
            if (buff.GetComponent<DoNotBenefitFromConcealment>() != null) return true;
            var parts = BuffPartsReader.Read(buff).Parts;
            return parts.Count > 0 && parts.All(IsPenalty);
        }

        /// <summary>
        /// ★ v0.3.21 (review): the buff kills its bearer when it starts (AddFactContextActions → ContextActionKill: True
        /// Judgment's damage buff, put on the target on a failed save).
        /// </summary>
        internal static bool KillsOnStart(BlueprintBuff buff)
        {
            try
            {
                return buff?.ComponentsArray != null &&
                       buff.ComponentsArray.OfType<AddFactContextActions>().Any(a => HasKill(a.Activated?.Actions, 0));
            }
            catch
            {
                return false;
            }
        }

        private static bool HasKill(GameAction[] actions, int depth)
        {
            if (actions == null || depth > 4) return false;
            foreach (var action in actions)
            {
                switch (action)
                {
                    case ContextActionKill _:
                        return true;
                    case Conditional c when HasKill(c.IfTrue?.Actions, depth + 1) || HasKill(c.IfFalse?.Actions, depth + 1):
                        return true;
                    case ContextActionSavingThrow t when HasKill(t.Actions?.Actions, depth + 1):
                        return true;
                    case ContextActionConditionalSaved cs when HasKill(cs.Failed?.Actions, depth + 1):
                        return true;
                }
            }
            return false;
        }

        private static bool IsPenalty(BuffPart part)
        {
            if (part.Descriptor == Kingmaker.Enums.ModifierDescriptor.Penalty) return true;
            int sign = part.Value != null && part.Value.ValueType == ContextValueType.Simple ? part.Value.Value : part.Value == null ? part.Fixed : 0;
            return sign * part.Multiplier < 0;
        }

        private void AnalyzeAbility(BlueprintAbility bp, HashSet<BlueprintAbility> visited, int depth, EffectSubject subject)
        {
            if (bp == null || depth > 4 || !visited.Add(bp)) return;

            var run = bp.GetComponent<AbilityEffectRunAction>();
            if (run != null)
            {
                if (run.SavingThrowType != SavingThrowType.Unknown && Save == SavingThrowType.Unknown)
                    Save = run.SavingThrowType;
                Walk(run.Actions?.Actions, visited, depth, subject, 0, NoGuards);
            }
            // ★ v0.3.21 (gaps): actions run on the clicked unit or point besides the main effect
            // (AdditionalAbilityEffectRunActionOnClickedTarget.Apply): Haunting Mists' whole effect, the lingering areas of
            // Ice Storm, Obsidian Flow, Tar Pool, Volcanic Storm.
            foreach (var extra in bp.GetComponents<AdditionalAbilityEffectRunActionOnClickedTarget>())
                Walk(extra.Action?.Actions, visited, depth, subject, 0, NoGuards);

            // ★ v0.3.18 (AA-1): an ability whose effect switches a buff on the caster on or off (Tabletop Tweaks'
            // AbilityEffectToggleBuff: Warrior Spirit, Black Blade attunements). Read without naming the mod: an effect
            // component whose type ends in "ToggleBuff" and that exposes its buff. The buff counts as applied and removed,
            // so the profile is a toggle (IsToggle) and the engine only casts it while the buff is off and worth having -
            // a cast while it is on would switch it off.
            foreach (var component in bp.ComponentsArray ?? new BlueprintComponent[0])
            {
                if (!(component is Kingmaker.UnitLogic.Abilities.Components.Base.AbilityApplyEffect) || !component.GetType().Name.EndsWith("ToggleBuff")) continue;
                var toggled = ToggledBuff(component);
                if (toggled == null) continue;
                CasterBuffs.Add(toggled);
                RemovedBuffs.Add(toggled);
                m_AllBuffs++;
                HasLongBuff = true;
            }

            var projectile = bp.GetComponent<AbilityDeliverProjectile>();
            // ★ v0.3.18 (C2): a line or cone projectile makes no attack roll, whatever its flag says (the game's
            // AbilityDeliverProjectile only rolls for other shapes).
            if (projectile != null && projectile.NeedAttackRoll &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Line &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Cone)
                NeedsAttackRoll = RangedAttackRoll = true;
            if (bp.GetComponent<AbilityDeliverTouch>() != null) NeedsAttackRoll = true;
            // ★ v0.3.18 (AA-4): a chain (Chain Lightning, Firebird's Chains of Fire, Mystical Mayhem's Eldritch Chain).
            foreach (var ricochet in bp.GetComponents<AbilityDeliverRicochet>())
                if (!Ricochets.Contains(ricochet)) Ricochets.Add(ricochet);
            var chain = bp.GetComponent<AbilityDeliverChain>();
            if (chain != null && Chain == null)
            {
                Chain = chain;
                try { ChainTargets = HarmonyLib.Traverse.Create(chain).Field("m_TargetType").GetValue<Kingmaker.UnitLogic.Abilities.Components.TargetType>(); }
                catch { ChainTargets = Kingmaker.UnitLogic.Abilities.Components.TargetType.Any; }
            }

            var sticky = bp.StickyTouch;
            if (sticky?.TouchDeliveryAbility != null)
            {
                NeedsAttackRoll = true;
                AnalyzeAbility(sticky.TouchDeliveryAbility, visited, depth + 1, subject);
            }
        }

        private void Walk(GameAction[] actions, HashSet<BlueprintAbility> visited, int abilityDepth, EffectSubject subject, int depth,
                          EffectGuard[] guards)
        {
            if (actions == null || depth > 12) return;
            bool toCaster = subject == EffectSubject.Caster;

            foreach (var action in actions)
            {
                if (action == null) continue;
                m_ActionTypes.Add(action.GetType().Name);

                // Conditional: each branch's effects only happen when the condition has that outcome for the target.
                if (action is Conditional conditional)
                {
                    Walk(conditional.IfTrue?.Actions, visited, abilityDepth, subject, depth + 1, With(guards, conditional.ConditionsChecker, true, toCaster));
                    Walk(conditional.IfFalse?.Actions, visited, abilityDepth, subject, depth + 1, With(guards, conditional.ConditionsChecker, false, toCaster));
                    continue;
                }

                if (action is Kingmaker.UnitLogic.Class.Kineticist.Actions.ContextActionHealBurn)
                    HealsBurn = true;

                // ★ v0.3.3: what runs on a freshly summoned creature (ContextActionSpawnMonster.AfterSpawn: summon
                // markers, Demonology's ritual buff) is not an effect on the caster or the target.
                if (subject == EffectSubject.Summoned)
                {
                    if (action is ContextActionSpawnMonster spawned)
                    {
                        Summons = true;
                        if (spawned.Blueprint != null && !SummonedUnits.Contains(spawned.Blueprint)) SummonedUnits.Add(spawned.Blueprint);
                    }
                    foreach (var child in ChildActionLists(action))
                        Walk(child, visited, abilityDepth, subject, depth + 1, guards);
                    continue;
                }

                if (IsKineticEffect && action is ContextActionRepeatedActions repeated)
                {
                    m_Repetitions.Add(repeated.Value);
                    try { Walk(repeated.Actions?.Actions, visited, abilityDepth, subject, depth + 1, guards); }
                    finally { m_Repetitions.RemoveAt(m_Repetitions.Count - 1); }
                    continue;
                }

                switch (action)
                {
                    // ★ v0.3.18 (review): damage run on the caster (a self-cost) is not damage to the target.
                    case ContextActionDealDamage dmg when subject != EffectSubject.Caster:
                        Damage.Add(dmg);
                        m_Guards[dmg] = guards;
                        if (dmg.HalfIfSaved) HalfOnSave = true;
                        break;
                    // ★ v0.3.18 (AA-1): a kill counts as damage equal to the target's HP, through the branch it sits in
                    // (a failed save, an HP limit). Isekai's Instakill, Devour Prey, Power Word Kill.
                    case ContextActionKill kill when subject == EffectSubject.Target:
                        Damage.Add(kill);
                        m_Guards[kill] = guards;
                        break;
                    // ★ v0.3.18 (AA-1): damage and heals the profile cannot read but the game can preview.
                    case GameAction provided when provided is IDealDamageProvider && subject == EffectSubject.Target:
                        Damage.Add(provided);
                        m_Guards[provided] = guards;
                        break;
                    case GameAction providedHeal when providedHeal is IHealDamageProvider && !(providedHeal is ContextActionHealTarget) &&
                                                     subject == EffectSubject.Target:
                        Heals.Add(providedHeal);
                        m_Guards[providedHeal] = guards;
                        break;
                    case ContextActionHealTarget heal:
                        // A heal run on the caster (life drain) or on other units is not a heal of the target.
                        if (subject != EffectSubject.Target) CasterHeals.Add(heal);
                        else Heals.Add(heal);
                        m_Guards[heal] = guards;
                        break;
                    case ContextActionApplyBuff apply when apply.Buff != null:
                        if (IsKineticEffect) m_BuffRepetitions[apply.Buff] = m_Repetitions.ToArray();
                        // ★ v0.3.22: infusion carriers often save/apply the real condition in Activated,
                        // then remove themselves. Follow those actions in the same guarded reader, once per buff.
                        if (IsKineticEffect && m_ActivatedBuffs.Add(apply.Buff))
                        {
                            foreach (var onFact in apply.Buff.GetComponents<AddFactContextActions>())
                            {
                                Walk(onFact.Activated?.Actions, visited, abilityDepth,
                                     toCaster || apply.ToCaster ? EffectSubject.Caster : subject, depth + 1, guards);
                                if (!(toCaster || apply.ToCaster) && onFact.NewRound?.Actions?.Length > 0)
                                {
                                    var tick = new AbilityProfile(Blueprint) { IsKineticEffect = true };
                                    tick.Walk(onFact.NewRound.Actions, visited, abilityDepth, subject, depth + 1, NoGuards);
                                    if (tick.DealsDamage)
                                        PeriodicEffects.Add(new PeriodicEffect
                                        {
                                            Buff = apply.Buff, Tick = tick,
                                            SaveEnds = tick.Save != SavingThrowType.Unknown &&
                                                RemovesSelf(onFact.NewRound, apply.Buff)
                                        });
                                }
                            }
                            if (apply.Buff.GetComponents<AddFactContextActions>().Any(a => RemovesSelf(a.Activated, apply.Buff)))
                                break; // the carrier itself has no persistent benefit or duration
                        }
                        if (toCaster || apply.ToCaster) CasterBuffs.Add(apply.Buff);
                        else TargetBuffs.Add(apply.Buff);
                        AddBuffGuards(apply.Buff, guards);
                        // ★ v0.3.19 (test11): the action itself, caster buffs too, for the buff's real duration
                        // (EffectiveRounds). Area buffs last while the area does.
                        if (!m_InArea && !m_Applications.ContainsKey(apply.Buff)) m_Applications[apply.Buff] = apply;
                        // ★ v0.3.21 (gaps): a buff that carries an aura around its bearer (AddAreaEffect: Archon's Aura, the
                        // domain and blessing greater powers) - valued by what the aura gives (TacticalEngine.Auras.cs).
                        if (!m_InArea && (subject == EffectSubject.Target || toCaster || apply.ToCaster) &&
                            apply.Buff.GetComponent<Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect>()?.AreaEffect is BlueprintAbilityAreaEffect aura &&
                            !Auras.Any(a => a.Carrier == apply.Buff))
                            Auras.Add(new AuraEffect
                            {
                                Carrier = apply.Buff, Area = aura,
                                OnCaster = toCaster || apply.ToCaster || Blueprint.Range == AbilityRange.Personal
                            });
                        // ★ v0.3.15: each application with its own branch and duration (Color Spray: 2d4 rounds on 2 HD
                        // or less, 1d4 on 3-4 HD, 1 round on 5 HD or more).
                        if (!(toCaster || apply.ToCaster) && !m_InArea)
                            m_BuffApplications.Add(new BuffApplication
                            {
                                Buff = apply.Buff, Guards = guards, Rounds = ExpectedRounds(apply), Saved = m_SavedBranch,
                                Extendable = !apply.Permanent && !apply.SameDuration && !apply.UseDurationSeconds &&
                                             apply.DurationValue != null && apply.DurationValue.IsExtendable
                            });
                        m_AllBuffs++;
                        if (LastsOneRound(apply)) m_ShortBuffs++;
                        // ★ v0.3.7: a buff applied by a spawned area is tied to the area, whatever its own duration.
                        if (m_InArea) { CountSpent(apply.Buff); break; }
                        if (apply.Permanent || (apply.DurationValue != null && (int)apply.DurationValue.Rate > 0))
                            HasLongBuff = true;
                        if (LastsADay(apply)) m_DayLongBuffs.Add(apply.Buff);
                        if (apply.Permanent || (apply.DurationValue != null && apply.DurationValue.Rate >= DurationRate.Hours))
                            m_PersistentBuffs.Add(apply.Buff);
                        CountSpent(apply.Buff);
                        break;
                    case ContextActionCombatManeuver maneuver when subject == EffectSubject.Target:
                        Maneuvers.Add(new ManeuverEffect { Type = maneuver.Type, Action = maneuver });
                        m_Guards[maneuver] = guards;
                        break;
                    case ContextActionCombatManeuverCustom custom when subject == EffectSubject.Target:
                        Maneuvers.Add(new ManeuverEffect { Type = custom.Type, Action = custom });
                        m_Guards[custom] = guards;
                        break;
                    // ★ v0.3.21: a weapon attack made by the ability (ContextActionMeleeAttack.RunAction).
                    case ContextActionMeleeAttack melee when subject == EffectSubject.Target && MeleeAttack == null:
                        MeleeAttack = melee;
                        m_Guards[melee] = guards;
                        break;
                    case ContextActionDisarm disarm when subject == EffectSubject.Target:
                        Maneuvers.Add(new ManeuverEffect { Type = Kingmaker.RuleSystem.Rules.CombatManeuver.Disarm, Action = disarm });
                        m_Guards[disarm] = guards;
                        break;
                    case Demoralize demoralize when subject == EffectSubject.Target && demoralize.Buff != null:
                        Intimidation = demoralize;
                        TargetBuffs.Add(demoralize.Buff);
                        AddBuffGuards(demoralize.Buff, guards);
                        m_AllBuffs++;
                        break;
                    case ContextActionSpawnMonster spawn:
                        Summons = true;
                        if (spawn.Blueprint != null && !SummonedUnits.Contains(spawn.Blueprint)) SummonedUnits.Add(spawn.Blueprint);
                        break;
                    case ContextActionSpawnAreaEffect spawnArea:
                        if (spawnArea.AreaEffect != null) AreaDurations[spawnArea.AreaEffect] = spawnArea.DurationValue;
                        WalkAreaEffect(spawnArea.AreaEffect, visited, abilityDepth, subject, depth, guards);
                        break;
                    case ContextActionDispelMagic dispel:
                        DispelActions.Add(dispel);
                        Dispels = true;
                        break;
                    // ★ v0.3.19 (class audit B2): lengthens a buff the target already has (Witch Cackle and Chant: one
                    // more round of each of the witch's active hexes). Read as nothing before: never cast.
                    case ContextActionReduceBuffDuration extend when extend.Increase && extend.TargetBuff != null && subject == EffectSubject.Target:
                        if (!Extensions.Any(e => e.Key == extend.TargetBuff))
                            Extensions.Add(new KeyValuePair<BlueprintBuff, ContextDurationValue>(extend.TargetBuff, extend.DurationValue));
                        break;
                    case ContextActionEnchantWornItem enchant when subject != EffectSubject.Other:
                        m_Enchantments.Add(new KeyValuePair<ContextActionEnchantWornItem, bool>(
                            enchant, toCaster || enchant.ToCaster || subject == EffectSubject.Caster));
                        // ★ v0.3.21: the main hand's weapon (the off hand's is not what the AI attacks with first).
                        if (enchant.Slot == Kingmaker.UI.GenericSlot.EquipSlotBase.SlotType.PrimaryHand && enchant.Enchantment != null)
                            WeaponEnchants.Add(new WeaponEnchantEffect
                            {
                                Enchantments = new[] { enchant.Enchantment }, OnCaster = toCaster || enchant.ToCaster || subject == EffectSubject.Caster,
                                Duration = enchant.DurationValue, Permanent = enchant.Permanent
                            });
                        break;
                    // ★ v0.3.21: EnhanceWeapon.RunAction - Magic Fang enchants natural weapons and the empty hand, Magic
                    // Weapon the weapon in the main hand (or, for the off-hand version, a two-handed one there).
                    case EnhanceWeapon enhance when subject != EffectSubject.Other && enhance.Enchantment.Length > 0 && enhance.Enchantment.All(e => e != null):
                        WeaponEnchants.Add(new WeaponEnchantEffect
                        {
                            Enchantments = enhance.Enchantment.ToArray(), Greater = enhance.Greater, EnchantLevel = enhance.EnchantLevel,
                            Natural = enhance.EnchantmentType == EnhanceWeapon.EnchantmentApplyType.MagicFang,
                            OffHand = enhance.EnchantmentType == EnhanceWeapon.EnchantmentApplyType.MagicWeapon && enhance.UseSecondaryHand,
                            OnCaster = toCaster || subject == EffectSubject.Caster, Duration = enhance.DurationValue
                        });
                        break;
                    case ContextActionRemoveBuff remove:
                        RemovesBuffs = true;
                        if (!remove.ToCaster && subject == EffectSubject.Target && remove.Buff != null)
                        {
                            RemovedBuffs.Add(remove.Buff);
                            m_Removals.Add(remove);
                            m_Guards[remove] = guards;
                        }
                        break;
                    case ContextActionRemoveBuffsByDescriptor removeByDescriptor:
                        RemovesBuffs = true;
                        if (subject == EffectSubject.Target)
                        {
                            RemovedDescriptors |= removeByDescriptor.SpellDescriptor.Value;
                            m_Removals.Add(removeByDescriptor);
                            m_Guards[removeByDescriptor] = guards;
                        }
                        break;
                    case ContextActionSavingThrow save:
                        if (save.Type != SavingThrowType.Unknown && Save == SavingThrowType.Unknown) Save = save.Type;
                        break;
                    case ContextActionConditionalSaved _:
                        HalfOnSave = HalfOnSave || DealsDamage;
                        break;
                }

                // ★ v0.3.18 (AA-1): the game's own enchant actions only (ContextActionEnchantWornItem, the weapon / armour /
                // shield enchant pools). Any type name with "Enchant" matched Mystical Mayhem's Enchant Item and counted
                // Tabletop Tweaks' Mage's Disjunction as an enchantment before its dispel.
                var actionType = action.GetType();
                string typeName = actionType.Name;
                if ((typeName.Contains("Enchant") || typeName.Contains("EnhanceWeapon")) &&
                    (actionType.Namespace ?? "").StartsWith("Kingmaker"))
                    Enchants = true;

                // ★ v0.3.18 (review): what a maneuver or a Demoralize does on success happens only then, and its failure
                // list only on a failure: counted as an unknown outcome, the failure list not at all. They were walked
                // as unconditional children - both lists for everyone - and Demoralize's Trickster list (run only for a
                // caster with that feature) for every caster.
                switch (action)
                {
                    case ContextActionCombatManeuver m:
                        Walk(m.OnSuccess?.Actions, visited, abilityDepth, subject, depth + 1, WithRoll(guards));
                        continue;
                    case ContextActionCombatManeuverCustom mc:
                        Walk(mc.Success?.Actions, visited, abilityDepth, subject, depth + 1, WithRoll(guards));
                        continue;
                    case Demoralize _:
                        continue;
                    // ★ v0.3.18 (review): full damage on a failed save and less on a passed one are one damage, not two
                    // added together: the failed branch is walked, and the passed one only stands for "half on a save".
                    case ContextActionConditionalSaved saved when HasDirectDamage(saved.Failed) && HasDirectDamage(saved.Succeed):
                        Walk(saved.Failed?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                        HalfOnSave = true;
                        continue;
                    case ContextActionConditionalSaved saved when IsKineticEffect:
                        var previousSaved = m_SavedBranch;
                        try
                        {
                            m_SavedBranch = false;
                            Walk(saved.Failed?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                            m_SavedBranch = true;
                            Walk(saved.Succeed?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                        }
                        finally { m_SavedBranch = previousSaved; }
                        continue;
                }

                var childSubject = ChildSubject(subject, action);
                foreach (var child in ChildActionLists(action))
                    Walk(child, visited, abilityDepth, childSubject, depth + 1, guards);

                foreach (var referenced in ReferencedAbilities(action))
                    AnalyzeAbility(referenced, visited, abilityDepth + 1, childSubject);
            }
        }

        /// <summary>
        /// ★ v0.3.7: the effects of a spawned area live in its components, not in action fields: AbilityAreaEffectRunAction
        /// (UnitEnter / Round / UnitMove action lists) and AbilityAreaEffectBuff (a buff while inside). Without this, Grease,
        /// Web, Stinking Cloud and the like were "unverifiable" and never counted as control.
        /// </summary>
        private void WalkAreaEffect(BlueprintAbilityAreaEffect area, HashSet<BlueprintAbility> visited, int abilityDepth,
                                    EffectSubject subject, int depth, EffectGuard[] guards)
        {
            if (area == null || subject != EffectSubject.Target || !m_VisitedAreas.Add(area)) return;
            bool wasInArea = m_InArea;
            m_InArea = true;
            try
            {
                // Size is a radius only for a cylinder; a wall's Size is its length (about half of it reaches either
                // way from the aim point); "all area" has no usable size. The ability's own AoERadius wins (Analyze).
                float radius = area.Shape == AreaEffectShape.Cylinder ? area.Size.Meters
                             : area.Shape == AreaEffectShape.Wall ? area.Size.Meters / 2f : 0f;
                m_AreaRadius = Math.Max(m_AreaRadius, radius);
                m_AreaHitsAllies |= area.CanTargetAllies;
                m_AreaHitsEnemies |= area.CanTargetEnemies;

                foreach (var run in area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction>())
                {
                    // A unit standing in the area takes the per-round effect; the on-enter effect is usually the same
                    // damage for whoever walks in, so it is not counted on top of it. Damage on moving inside (Spike
                    // Growth) depends on the target choosing to move: not counted.
                    // ★ v0.3.12: UnitExit is deliberately not walked. Exit lists usually undo the enter effect (remove
                    // the area's buff); walked like the others, those removals would make the spell look like a cleanse
                    // or a toggle (RemovesAnything / IsToggle).
                    int before = Damage.Count;
                    Walk(run.UnitEnter?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                    int afterEnter = Damage.Count;
                    Walk(run.Round?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                    if (Damage.Count > afterEnter) PeriodicAreas.Add(area);
                    if (Damage.Count > afterEnter && afterEnter > before)
                        Damage.RemoveRange(before, afterEnter - before);
                    int beforeMove = Damage.Count;
                    Walk(run.UnitMove?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                    if (Damage.Count > beforeMove)
                        Damage.RemoveRange(beforeMove, Damage.Count - beforeMove);
                }
                // ★ v0.3.21 (verification audit): Create Pit, Acid Pit, Hungry Pit, Spiked Pit (AreaEffectPit): a Reflex save
                // avoids the fall (UnitInPitController.ReflexCheck); the fall's damage and, for some, damage each round.
                var pit = area.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AreaEffectPit>();
                if (pit != null)
                {
                    Pit = pit;
                    if (Save == SavingThrowType.Unknown) Save = SavingThrowType.Reflex;
                    Walk(pit.OnFallAction?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                    Walk(pit.EveryRoundAction?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                }
                foreach (var areaBuff in area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff>())
                {
                    var buff = areaBuff.Buff;
                    if (buff == null) continue;
                    TargetBuffs.Add(buff);
                    var buffGuards = With(guards, areaBuff.Condition, true, false);
                    AddBuffGuards(buff, buffGuards);
                    m_AllBuffs++;
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[Profile] {Blueprint.name} ({Blueprint.AssetGuid}): area effect {area.name} error {ex.GetType().Name}: {ex.Message}");   // ★ v0.3.12
            }
            finally
            {
                m_InArea = wasInArea;
            }
        }

        /// <summary>Actions that run their children on someone other than the current subject.</summary>
        private static EffectSubject ChildSubject(EffectSubject subject, GameAction action)
        {
            switch (action)
            {
                case ContextActionOnContextCaster _:
                    return EffectSubject.Caster;
                case ContextActionSpawnMonster _:
                    return EffectSubject.Summoned;
                case ContextActionOnOwner _:
                case ContextActionPartyMembers _:
                case ContextActionsOnPet _:
                case ContextActionOnRandomTargetsAround _:
                case ContextActionOnSwarmTargets _:
                case ContextActionOnRandomAreaTarget _:
                    return EffectSubject.Other;
                default:
                    return subject;
            }
        }

        /// <summary>★ v0.3.18 (review): the effect runs only when a roll the cast makes succeeds (a combat maneuver, an
        /// Intimidate check): not known before the cast (Unknown), never unconditional.</summary>
        private static bool HasDirectDamage(ActionList list) =>
            list?.Actions != null && list.Actions.Any(a => a is ContextActionDealDamage);

        private static EffectGuard[] WithRoll(EffectGuard[] guards)
        {
            var result = new EffectGuard[guards.Length + 1];
            guards.CopyTo(result, 0);
            result[guards.Length] = new EffectGuard { Checker = null, Branch = true };
            return result;
        }

        private static EffectGuard[] With(EffectGuard[] guards, ConditionsChecker checker, bool branch, bool onCaster)
        {
            if (checker == null) return guards;
            var result = new EffectGuard[guards.Length + 1];
            guards.CopyTo(result, 0);
            result[guards.Length] = new EffectGuard { Checker = checker, Branch = branch, OnCaster = onCaster };
            return result;
        }

        /// <summary>
        /// ★ v0.3.6: the buff removes itself after one use: RemoveBuffOnAttack, or ModifyD20 with DispellOnRerollFinished
        /// (Perfect Strike: "roll twice" once, then gone). v0.3.5 missed the latter and kept Perfect Strike up after fights.
        /// </summary>
        private void CountSpent(BlueprintBuff buff)
        {
            if (IsSpentByUse(buff)) m_SpentByAttackBuffs++;
            if (IsSpentByUse(buff) || IsSpentByRoll(buff)) m_SpentByRollBuffs++;
            if (IsSpentByCast(buff)) m_SpentByCastBuffs++;
        }

        /// <summary>★ v0.3.19 (test9): the list removes the buff that runs it (directly or in a branch).</summary>
        /// <remarks>★ v0.3.19 (test11): by ContextActionRemoveSelf or by naming it (ContextActionRemoveBuff of
        /// <paramref name="owner"/>): Guidance's triggers remove GuidanceBuff by name (Spells/Level0/GuidanceBuff), so the
        /// test9 rule never saw it.</remarks>
        private static bool RemovesSelf(ActionList list, BlueprintBuff owner, int depth = 0)
        {
            if (list?.Actions == null || depth > 3) return false;
            foreach (var action in list.Actions)
            {
                if (action is ContextActionRemoveSelf) return true;
                if (owner != null && action is ContextActionRemoveBuff rb && rb.Buff == owner) return true;
                if (action is Conditional c && (RemovesSelf(c.IfTrue, owner, depth + 1) || RemovesSelf(c.IfFalse, owner, depth + 1))) return true;
                // ★ v0.3.22: round saves can remove a burning buff.
                if (ChildActionLists(action).Any(children => RemovesSelf(new ActionList { Actions = children }, owner, depth + 1))) return true;
            }
            return false;
        }

        /// <summary>★ v0.3.19 (test9): gone after its owner's next d20 roll of some kind (see <see cref="BuffsSpentByRoll"/>).</summary>
        internal static bool IsSpentByRoll(BlueprintBuff buff)
        {
            try
            {
                if (buff?.ComponentsArray == null) return false;
                foreach (var c in buff.ComponentsArray)
                {
                    switch (c)
                    {
                        case AddInitiatorAttackRollTrigger t when RemovesSelf(t.Action, buff): return true;
                        case AddInitiatorSavingThrowTrigger t when RemovesSelf(t.Action, buff): return true;
                        case AddInitiatorSkillRollTrigger t when RemovesSelf(t.Action, buff): return true;
                        case AddInitiatorPartySkillRollTrigger t when RemovesSelf(t.Action, buff): return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>★ v0.3.19 (test9): gone when its owner next casts (see <see cref="BuffsSpentByCast"/>).</summary>
        internal static bool IsSpentByCast(BlueprintBuff buff)
        {
            try
            {
                return buff?.ComponentsArray != null &&
                       (buff.ComponentsArray.OfType<AddAbilityUseTrigger>().Any(t => RemovesSelf(t.Action, buff)) ||
                        // ★ v0.3.21 (gaps): metamagic for the next spell, removed when that spell is cast (Magus arcana,
                        // Universalist school powers: MetamagicOnNextSpell.OnEventAboutToTrigger(RuleCastSpell)).
                        buff.ComponentsArray.OfType<MetamagicOnNextSpell>().Any(m => !m.DoNotRemove));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSpentByUse(BlueprintBuff buff)
        {
            try
            {
                if (buff.GetComponent<RemoveBuffOnAttack>() != null) return true;
                // ★ v0.3.19 (test9): the same, by a weapon-attack trigger that removes the buff.
                if (buff.GetComponents<AddInitiatorAttackWithWeaponTrigger>().Any(t => RemovesSelf(t.Action, buff))) return true;
                return buff.GetComponents<ModifyD20>().Any(c => c.DispellOnRerollFinished);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A day or longer (or about a day in hours): a "used today" marker rather than a combat buff.</summary>
        private static bool LastsADay(ContextActionApplyBuff apply)
        {
            try
            {
                if (apply.Permanent || apply.SameDuration || apply.DurationValue == null) return false;
                var d = apply.DurationValue;
                if (d.Rate == DurationRate.Days) return true;
                return d.Rate == DurationRate.Hours && d.BonusValue != null && d.BonusValue.IsValueSimple && d.BonusValue.Value >= 20;
            }
            catch
            {
                return false;
            }
        }

        private sealed class BuffApplication
        {
            public BlueprintBuff Buff;
            public EffectGuard[] Guards;
            /// <summary>Expected rounds, or a negative value when the duration depends on values only the cast knows.</summary>
            public float Rounds;
            /// <summary>★ v0.3.18 (AA-4): Extend doubles it (ContextDurationValue.IsExtendable).</summary>
            public bool Extendable;
            public bool? Saved; // ★ v0.3.22: successful-save duration can differ from failed-save duration
        }

        private readonly List<BuffApplication> m_BuffApplications = new List<BuffApplication>();
        private readonly Dictionary<BlueprintBuff, ContextActionApplyBuff> m_Applications = new Dictionary<BlueprintBuff, ContextActionApplyBuff>();

        /// <summary>★ v0.3.19 (test11): the action that applies <paramref name="buff"/> (the first one), or null.</summary>
        public ContextActionApplyBuff ApplicationOf(BlueprintBuff buff) =>
            buff != null && m_Applications.TryGetValue(buff, out var a) ? a : null;

        /// <summary>
        /// ★ v0.3.15: expected duration of a buff application in rounds: fixed dice and bonus, any rate. A duration by
        /// caster level or another computed value is unknown (negative), except minutes or longer, which outlast a fight.
        /// </summary>
        private static float ExpectedRounds(ContextActionApplyBuff apply)
        {
            try
            {
                if (apply.Permanent) return 100f;
                if (apply.SameDuration) return -1f;
                if (apply.UseDurationSeconds) return Math.Max(1f, apply.DurationSeconds / 6f);
                var d = apply.DurationValue;
                if (d == null) return -1f;
                float perRate = d.Rate == DurationRate.Rounds ? 1f : d.Rate == DurationRate.Minutes ? 10f :
                                d.Rate == DurationRate.TenMinutes ? 100f : 600f;
                bool simple = (d.BonusValue == null || d.BonusValue.IsValueSimple) &&
                              (d.DiceType <= DiceType.One || d.DiceCountValue == null || d.DiceCountValue.IsValueSimple);
                if (!simple) return d.Rate == DurationRate.Rounds ? -1f : 10f;
                float dice = d.DiceType <= DiceType.Zero ? 0f
                           : (d.DiceCountValue?.Value ?? 0) * ((int)d.DiceType + 1) / 2f;
                float rounds = (dice + (d.BonusValue?.Value ?? 0)) * perRate;
                return rounds > 0f ? rounds : -1f;
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>
        /// ★ v0.3.15: how many rounds this ability's effects on <paramref name="target"/> are expected to last, from the
        /// branch that applies to that target; negative when unknown. Color Spray on a 5 HD Cultist Champion stuns for one
        /// round, not the three the control value assumed (Orvath cast it there five times).
        /// </summary>
        public float EffectRoundsOn(AbilityData ability, UnitEntityData target, float horizon = float.PositiveInfinity)
        {
            if (BuffOnBothSaveOutcomes)
            {
                var applications = m_BuffApplications.Where(a => Evaluate(a.Guards, ability, target) != EffectApplies.No).ToList();
                float failed = applications.Where(a => a.Saved == false).Select(a => a.Rounds).DefaultIfEmpty(0f).Max();
                float saved = applications.Where(a => a.Saved == true).Select(a => a.Rounds).DefaultIfEmpty(0f).Max();
                return ValueModel.SaveDuration(CombatMath.SaveFailChance(ability, this, target), failed, saved, horizon);
            }
            // ★ v0.3.18 (AA-4): Demoralize lasts by the check's result.
            if (Intimidation != null && target != null && ability?.Caster?.Unit != null)
            {
                CombatMath.IntimidateOdds(ability.Caster.Unit, target, Intimidation, out _, out float rounds);
                return rounds;
            }
            if (m_BuffApplications.Count == 0 || target == null) return -1f;
            // ★ v0.3.18 (AA-4): Extend metamagic doubles an extendable duration (ContextDurationValue.Calculate).
            bool extended = false;
            try { extended = ability != null && ability.HasMetamagic(Metamagic.Extend); } catch { }
            float sure = -1f, maybe = float.MaxValue;
            foreach (var a in m_BuffApplications)
            {
                if (a.Rounds <= 0f) continue;
                var applies = Evaluate(a.Guards, ability, target);
                if (applies == EffectApplies.No) continue;
                float rounds = extended && a.Extendable ? a.Rounds * 2f : a.Rounds;
                if (applies == EffectApplies.Unknown) maybe = Math.Min(maybe, rounds);
                else sure = Math.Max(sure, rounds);
            }
            if (sure > 0f) return sure;
            return maybe < float.MaxValue ? maybe : -1f;
        }

        /// <summary>Lasts at most one round (SameDuration copies a parent duration we cannot see: not short).</summary>
        private static bool LastsOneRound(ContextActionApplyBuff apply)
        {
            try
            {
                if (apply.Permanent || apply.SameDuration) return false;
                if (apply.UseDurationSeconds) return apply.DurationSeconds <= 6.01f;
                var d = apply.DurationValue;
                if (d == null || (int)d.Rate > 0) return false;
                bool noDice = d.DiceType == Kingmaker.RuleSystem.DiceType.Zero || (d.DiceCountValue?.IsValueSimple == true && d.DiceCountValue.Value == 0);
                return noDice && d.BonusValue != null && d.BonusValue.IsValueSimple && d.BonusValue.Value <= 1;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Per-target evaluation

        /// <summary>
        /// Conditions that only read facts of the target/caster (no dice, no area or encounter state), so evaluating
        /// them outside the real cast is safe. Anything else makes the branch "Unknown" (counted, as in v0.3.0).
        /// </summary>
        private static readonly HashSet<Type> SafeConditions = new HashSet<Type>
        {
            typeof(ContextConditionHasFact), typeof(ContextConditionHasBuff), typeof(ContextConditionHasBuffFromCaster),
            typeof(ContextConditionHasBuffWithDescriptor), typeof(ContextConditionAlignment), typeof(ContextConditionCasterHasFact),
            typeof(ContextConditionIsAlly), typeof(ContextConditionIsEnemy), typeof(ContextConditionIsCaster),
            typeof(ContextConditionTargetIsYourself), typeof(ContextConditionAlive), typeof(ContextConditionIsUnconscious),
            typeof(ContextConditionIsHelpless), typeof(ContextConditionIsPartyMember), typeof(ContextConditionCharacterClass),
            typeof(ContextConditionSize), typeof(ContextConditionIsAnimalCompanion),
            // ★ v0.3.18 (AA-1): more of the game's conditions whose check only reads the target or caster (each checked
            // in the decompiled game for side effects): HP share, flanked / flat-footed / engaged, equipment, buffs by
            // school, rank or uniqueness, stats, caster kind, blueprint, sight, main target, distance, combat state.
            // Spirit Weapon Enchantment's weapon check was "not evaluated - both branches counted" in every log.
            // Favored Enemy is left out: its check creates a part on the caster.
            typeof(ContextConditionCompareTargetHP), typeof(ContextConditionCompareCasterHP), typeof(ContextConditionIsFlanked),
            typeof(ContextConditionIsFlatFooted), typeof(ContextConditionTargetIsEngaged), typeof(ContextConditionIsWeaponEquipped),
            typeof(ContextConditionIsShieldEquipped), typeof(ContextConditionIsTwoHandedEquipped),
            typeof(ContextConditionCasterHasTwoWeapons), typeof(ContextConditionCasterWeaponInTwoHands),
            typeof(ContextConditionCasterHasBuffWithDescriptor), typeof(ContextConditionHasBuffWithSpellSchool),
            typeof(ContextConditionHasUniqueBuff), typeof(ContextConditionStatValue), typeof(ContextConditionTargetIsArcaneCaster),
            typeof(ContextConditionTargetIsDivineCaster), typeof(ContextConditionTargetIsBlueprint),
            typeof(ContextConditionTargetCanSeeInvisible), typeof(ContextConditionIsMainTarget),
            typeof(ContextConditionDistanceToTarget), typeof(ContextConditionIsInCombat), typeof(ContextConditionBuffRank),
            typeof(ContextConditionHasBuffImmunityWithDescriptor), typeof(ContextConditionHasTouchSpellCharge),
            typeof(ContextConditionGender), typeof(ContextConditionIsMainCharacter)
        };

        /// <summary>
        /// ★ v0.3.8: also "hit dice at most N" (Sleep, Daze, Color Spray). ★ v0.3.17: also with AddSharedValue (the limit
        /// plus a value the ability computes, e.g. Sleep): evaluated with that value's range (<see cref="WithSharedValues"/>).
        /// Counted for every target before, Sleep was cast on enemies over its limit again and again (log 06aebf2d).
        /// </summary>
        internal static bool IsSafe(Condition c) =>
            c == null || SafeConditions.Contains(c.GetType()) || c is ContextConditionHitDice;

        /// <summary>
        /// ★ v0.3.17: the range of a shared value as the ability's own ContextCalculateSharedValue defines it (dice count
        /// and bonus from the context, without rolling): lowest, average and highest. No component: 0, as in the game.
        /// </summary>
        internal static void SharedValueRange(AbilityData ability, MechanicsContext context, AbilitySharedValue type,
                                             out int min, out int avg, out int max)
        {
            min = avg = max = 0;
            var source = ability?.Blueprint?.GetComponents<ContextCalculateSharedValue>()?.LastOrDefault(c => c.ValueType == type);
            if (source?.Value == null) return;
            int count = source.Value.DiceCountValue?.Calculate(context) ?? 0;
            int bonus = source.Value.BonusValue?.Calculate(context) ?? 0;
            int sides = source.Value.DiceType.Sides();
            min = (int)((count * Math.Min(1, sides) + bonus) * source.Modifier);
            max = (int)((count * sides + bonus) * source.Modifier);
            avg = (int)Math.Round((count * (sides + 1) / 2.0 * (sides > 0 ? 1 : 0) + bonus) * source.Modifier);
        }

        /// <summary>
        /// ★ v0.3.17: evaluates the checker; shared values its Hit Dice conditions add are set in this throwaway context to
        /// their lowest and highest: the same answer both ways is the answer, otherwise the answer at the average.
        /// </summary>
        private static bool WithSharedValues(ConditionsChecker checker, AbilityData ability, MechanicsContext context)
        {
            var types = checker.Conditions.OfType<ContextConditionHitDice>().Where(h => h.AddSharedValue)
                               .Select(h => h.SharedValue).Distinct().ToList();
            if (types.Count == 0) return Run(checker);
            var ranges = types.Select(t =>
            {
                SharedValueRange(ability, context, t, out int lo, out int mid, out int hi);
                return new { Type = t, Lo = lo, Mid = mid, Hi = hi };
            }).ToList();
            // ★ v0.3.22: checks can use a detached cast context more than once, or an infusion's real source
            // context. The old early return left the high/average test value written into that context.
            var saved = ranges.Select(r => context[r.Type]).ToArray();
            try
            {
                foreach (var r in ranges) context[r.Type] = r.Lo;
                bool low = Run(checker);
                foreach (var r in ranges) context[r.Type] = r.Hi;
                bool high = Run(checker);
                if (low == high) return low;
                foreach (var r in ranges) context[r.Type] = r.Mid;
                return Run(checker);
            }
            finally { for (int i = 0; i < ranges.Count; i++) context[ranges[i].Type] = saved[i]; }
        }

        /// <summary>
        /// ★ v0.3.17 (test5): the Hit Dice limits in this ability's branch conditions as the profile evaluates them, and
        /// the target's level - for the log when the ability did nothing to its target.
        /// </summary>
        public string HitDiceNote(AbilityData ability, UnitEntityData target)
        {
            try
            {
                var conditions = m_Guards.Values.Concat(m_BuffGuards.Values.SelectMany(b => b)).SelectMany(g => g)
                    .SelectMany(g => g.Checker?.Conditions ?? new Condition[0]).OfType<ContextConditionHitDice>().Distinct().ToList();
                if (conditions.Count == 0) return null;
                var context = CombatMath.ContextForEffects(ability, new TargetWrapper(target));
                var parts = conditions.Select(hd =>
                {
                    if (!hd.AddSharedValue) return $"level <= {hd.HitDice}";
                    SharedValueRange(ability, context, hd.SharedValue, out int lo, out int mid, out int hi);
                    return $"level <= {hd.HitDice} + {hd.SharedValue} ({lo}..{hi}, average {mid})";
                });
                return $"target level {target.Descriptor?.Progression?.CharacterLevel ?? 0}; Hit Dice conditions: {string.Join(", ", parts)}";
            }
            catch (Exception ex)
            {
                return "Hit Dice conditions unreadable: " + ex.Message;
            }
        }

        internal static bool Run(ConditionsChecker checker)
        {
            bool and = checker.Operation == Operation.And;
            bool acc = and;
            foreach (var condition in checker.Conditions)
            {
                if (condition == null) continue;
                bool r = condition.Check();
                if (and && !r) return false;
                if (!and && r) return true;
            }
            return acc;
        }

        /// <summary>One checker's outcomes in one frame (★ v0.3.18 review: dropped when the frame changes, so the
        /// per-aim keys do not pile up over a fight).</summary>
        private sealed class CheckerResults
        {
            public int Frame;
            public readonly Dictionary<string, bool?> Results = new Dictionary<string, bool?>();
        }

        /// <summary>Checker outcome per (checker, target) within one frame; a decision evaluates many candidates.</summary>
        private static readonly Dictionary<ConditionsChecker, CheckerResults> CheckerCache =
            new Dictionary<ConditionsChecker, CheckerResults>();

        private static readonly HashSet<string> ReportedUnknown = new HashSet<string>();

        /// <summary>
        /// ★ v0.3.18 (test10): what the cast being scored is aimed at (a creature or the ground). Branch conditions are
        /// evaluated in a cast context with that as its main target, as the game's own cast has it: "is the main target"
        /// (ContextConditionIsMainTarget: a bomb's direct hit against its splash) was true for every unit of an area,
        /// because each was evaluated as the main target of its own throwaway cast. Outside a scored aim (single-target
        /// questions), each unit is its own main target, as before.
        /// </summary>
        [ThreadStatic] private static TargetWrapper s_Aim;
        [ThreadStatic] private static AimScope s_AimScope;

        internal static TargetWrapper CurrentAim => s_Aim;

        internal static IDisposable AimedAt(TargetWrapper aim)
        {
            var scope = new AimScope(s_Aim, s_AimScope);
            s_Aim = aim;
            s_AimScope = scope;
            return scope;
        }

        private sealed class AimScope : IDisposable
        {
            private readonly TargetWrapper m_Previous;
            internal readonly AimScope Previous;
            internal readonly List<ConditionCast> Contexts = new List<ConditionCast>();
            internal readonly Dictionary<ConditionKey, bool?> Results = new Dictionary<ConditionKey, bool?>();
            public AimScope(TargetWrapper previous, AimScope previousScope) { m_Previous = previous; Previous = previousScope; }
            public void Dispose() { s_Aim = m_Previous; s_AimScope = Previous; Contexts.Clear(); Results.Clear(); }
        }

        // ★ v0.3.22: within one aim, identity keys avoid formatted strings and repeated auto-metamagic queries
        // merely to look up a guard result. A new aim (including wall orientation) owns a new dictionary.
        private readonly struct ConditionKey : IEquatable<ConditionKey>
        {
            private readonly object checker, ability, target, caster, source;
            internal ConditionKey(object checker, object ability, object target, object caster, object source)
            { this.checker = checker; this.ability = ability; this.target = target; this.caster = caster; this.source = source; }
            public bool Equals(ConditionKey other) => ReferenceEquals(checker, other.checker) && ReferenceEquals(ability, other.ability) &&
                ReferenceEquals(target, other.target) && ReferenceEquals(caster, other.caster) && ReferenceEquals(source, other.source);
            public override bool Equals(object other) => other is ConditionKey key && Equals(key);
            public override int GetHashCode()
            {
                int Hash(object value) => value == null ? 0 : RuntimeHelpers.GetHashCode(value);
                unchecked { return ((((Hash(checker) * 397) ^ Hash(ability)) * 397 ^ Hash(target)) * 397 ^ Hash(caster)) * 397 ^ Hash(source); }
            }
        }

        private sealed class ConditionCast
        {
            internal AbilityData Ability;
            internal MechanicsContext Ambient;
            internal TargetWrapper AmbientTarget;
            internal RulebookEvent Rule;
            internal AbilityExecutionContext Context;
        }

        // ★ v0.3.22: one scored cast has one main aim and native parameter/rank snapshot. Its condition readers
        // visit each recipient with GetDataScope; rebuilding the same cast for every guard/recipient copied the
        // native rulebook history and reran parameter rules hundreds of times for Sleep. Keep this private to
        // conditions: damage providers and other readers still receive fresh execution contexts.
        private static MechanicsContext ConditionContext(AbilityData ability, TargetWrapper aim)
        {
            var infusion = KineticEffectContext.For(ability);
            if (infusion != null) return infusion;
            var scope = s_AimScope;
            if (scope == null || s_Aim == null) return CombatMath.CreateContext(ability, aim);
            var ambient = ContextData<MechanicsContext.Data>.Current;
            var rule = Rulebook.CurrentContext?.CurrentEvent;
            foreach (var entry in scope.Contexts)
                if (ReferenceEquals(entry.Ability, ability) && ReferenceEquals(entry.Ambient, ambient?.Context) &&
                    ReferenceEquals(entry.AmbientTarget, ambient?.CurrentTarget) && ReferenceEquals(entry.Rule, rule))
                {
                    if (DecisionReadScope.Current != null) DecisionReadScope.Current.ConditionContextHits++;
                    return entry.Context;
                }
            var context = CombatMath.CreateContext(ability, aim);
            scope.Contexts.Add(new ConditionCast { Ability = ability, Ambient = ambient?.Context,
                AmbientTarget = ambient?.CurrentTarget, Rule = rule, Context = context });
            return context;
        }

        public EffectApplies AppliesTo(GameAction effect, AbilityData ability, UnitEntityData target)
        {
            return Evaluate(effect != null && m_Guards.TryGetValue(effect, out var g) ? g : NoGuards, ability, target);
        }

        public EffectApplies BuffAppliesTo(BlueprintBuff buff, AbilityData ability, UnitEntityData target)
        {
            if (SharedAreaPreview.TryShare(ability, buff, target, out float share))
                return share > 0f ? EffectApplies.Yes : EffectApplies.No;
            if (buff == null || !m_BuffGuards.TryGetValue(buff, out var branches) || branches.Count == 0)
                return EffectApplies.Unconditional;
            // Applies when any branch it is applied in does.
            bool unknown = false;
            foreach (var guards in branches)
            {
                var result = Evaluate(guards, ability, target);
                if (result == EffectApplies.Unconditional) return EffectApplies.Unconditional;
                if (result == EffectApplies.Yes) return EffectApplies.Yes;
                if (result == EffectApplies.Unknown) unknown = true;
            }
            return unknown ? EffectApplies.Unknown : EffectApplies.No;
        }

        /// <summary>
        /// ★ v0.3.14 (test5): two effects sit in opposite branches of the same condition, so at most one of them runs.
        /// Fire Blast's damage is in both branches of an "IsEqual" condition the profile cannot evaluate; both were added
        /// up, about twice its real damage against every target.
        /// </summary>
        public bool Exclusive(GameAction a, GameAction b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            if (!m_Guards.TryGetValue(a, out var ga) || !m_Guards.TryGetValue(b, out var gb)) return false;
            return ga.Any(x => gb.Any(y => ReferenceEquals(x.Checker, y.Checker) && x.OnCaster == y.OnCaster && x.Branch != y.Branch));
        }

        public bool HasGuardedEffects => m_Guards.Values.Any(g => g.Length > 0) || m_BuffGuards.Values.Any(b => b.Any(g => g.Length > 0));

        private static EffectApplies Evaluate(EffectGuard[] guards, AbilityData ability, UnitEntityData target)
        {
            if (guards.Length == 0) return EffectApplies.Unconditional;
            if (ability == null || target == null) return EffectApplies.Unknown;
            var caster = ability.Caster?.Unit;

            bool unknown = false;
            foreach (var guard in guards)
            {
                if (guard.Checker == null) { unknown = true; continue; }   // a roll's outcome (WithRoll)
                var subject = guard.OnCaster ? caster : target;
                if (subject == null) { unknown = true; continue; }
                bool? result = CheckFor(guard.Checker, ability, subject, caster);
                if (!result.HasValue) { unknown = true; continue; }
                if (result.Value != guard.Branch) return EffectApplies.No;
            }
            return unknown ? EffectApplies.Unknown : EffectApplies.Yes;
        }

        /// <summary>
        /// ★ v0.3.19 (class audit B1): <paramref name="checker"/> for <paramref name="target"/> under a context that is not an
        /// ability's (a toggle's area): the game's own check, only when every condition is one of the safe ones; null when
        /// it cannot be evaluated here.
        /// </summary>
        internal static bool? CheckSafe(ConditionsChecker checker, MechanicsContext context, UnitEntityData target)
        {
            if (checker?.Conditions == null || checker.Conditions.Length == 0) return true;
            if (context == null || target == null || !checker.Conditions.All(IsSafe)) return null;
            // Hit Dice limits with shared values need the ability's own value: not here.
            if (checker.Conditions.Any(c => c is ContextConditionHitDice)) return null;
            try
            {
                using (context.GetDataScope(new TargetWrapper(target)))
                    return checker.Check();
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] condition check on {target.CharacterName}: {ex.Message}");
                return null;
            }
        }

        private static bool? CheckFor(ConditionsChecker checker, AbilityData ability, UnitEntityData target, UnitEntityData caster)
        {
            // Caster-dependent conditions (IsAlly, CasterHasFact, HasBuffFromCaster...): key by caster and target.
            // ★ v0.3.18 (review): and by the ability itself (a scroll and the spell, metamagic copies: caster level and
            // shared values differ) - one checker is shared by every copy of the blueprint.
            var aim = s_Aim ?? new TargetWrapper(target);
            var scope = s_Aim != null ? s_AimScope : null;
            var localKey = new ConditionKey(checker, ability, target, caster, KineticEffectContext.For(ability));
            CheckerResults perUnit = null;
            string key = null;
            if (scope != null)
            {
                if (scope.Results.TryGetValue(localKey, out var cached))
                {
                    if (DecisionReadScope.Current != null) DecisionReadScope.Current.ConditionResultHits++;
                    return cached;
                }
            }
            else
            {
                int frame = Time.frameCount;
                key = $"{caster?.UniqueId}|{target.UniqueId}|{ability.Blueprint?.AssetGuid}|{AbilityDiscovery.MetamagicTag(ability)}|" +
                         $"{ability.SourceItem?.UniqueId}|{ability.Spellbook?.Blueprint?.AssetGuid}|{KineticEffectContext.KeyFor(ability)}|" +
                         (aim.Unit != null ? aim.Unit.UniqueId : $"{aim.Point.x:R},{aim.Point.y:R},{aim.Point.z:R}:{aim.Orientation:R}");
                if (!CheckerCache.TryGetValue(checker, out perUnit))
                    CheckerCache[checker] = perUnit = new CheckerResults { Frame = frame };
                if (perUnit.Frame != frame)
                {
                    perUnit.Results.Clear();
                    perUnit.Frame = frame;
                }
                if (perUnit.Results.TryGetValue(key, out var cached)) return cached;
            }

            bool? result = null;
            try
            {
                if (checker.Conditions == null || checker.Conditions.Length == 0)
                {
                    result = true;
                }
                else if (!checker.Conditions.All(IsSafe))
                {
                    string types = string.Join(",", checker.Conditions.Where(c => !IsSafe(c))
                                                                     .Select(c => c.GetType().Name).Distinct());
                    if (ReportedUnknown.Add($"{ability.Blueprint?.AssetGuid}|{types}"))
                        Main.Log($"[Profile] {ability.Name}: branch condition {types} not evaluated - both branches counted");
                }
                else
                {
                    var context = ConditionContext(ability, aim);
                    if (context != null)
                    {
                        using (context.GetDataScope(new TargetWrapper(target)))
                        {
                            result = WithSharedValues(checker, ability, context);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] condition check for {ability.Name} on {target.CharacterName}: {ex.Message}");
                result = null;
            }

            if (scope != null) scope.Results[localKey] = result;
            else perUnit.Results[key] = result;
            return result;
        }

        /// <summary>
        /// ★ v0.3.2: the target currently has a HARMFUL buff (debuff, poison, fear...) this ability would remove. Removing a
        /// good buff is not a cleanse: Orvath's "Shift Form: Mortal Guise" removes the Overlord form and was valued as a
        /// cleanse, so he switched forms back and forth every turn.
        /// </summary>
        public bool WouldRemoveSomething(AbilityData ability, UnitEntityData target)
        {
            return BuffsRemovedFrom(ability, target).Any(b => IsHarmfulFor(b, target));
        }

        /// <summary>Good buffs on the target this ability would remove (a mode switch such as Overlord ↔ Mortal Guise).</summary>
        public List<Kingmaker.UnitLogic.Buffs.Buff> BeneficialBuffsRemoved(AbilityData ability, UnitEntityData target)
        {
            return BuffsRemovedFrom(ability, target).Where(b => !IsHarmfulFor(b, target)).ToList();
        }

        private IEnumerable<Kingmaker.UnitLogic.Buffs.Buff> BuffsRemovedFrom(AbilityData ability, UnitEntityData target)
        {
            var result = new List<Kingmaker.UnitLogic.Buffs.Buff>();
            if (target?.Buffs == null || m_Removals.Count == 0) return result;
            try
            {
                var buffs = target.Buffs.Enumerable.ToList();
                foreach (var removal in m_Removals)
                {
                    if (AppliesTo(removal, ability, target) == EffectApplies.No) continue;
                    foreach (var buff in buffs)
                    {
                        bool removed = removal is ContextActionRemoveBuff rb && rb.Buff == buff.Blueprint ||
                                       removal is ContextActionRemoveBuffsByDescriptor rd && buff.MaybeContext != null &&
                                       (buff.MaybeContext.SpellDescriptor & rd.SpellDescriptor.Value) != SpellDescriptor.None;
                        if (removed && !result.Contains(buff)) result.Add(buff);
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>Flagged harmful by its blueprint, or put on the unit by one of its enemies.</summary>
        private static bool IsHarmfulFor(Kingmaker.UnitLogic.Buffs.Buff buff, UnitEntityData unit)
        {
            try
            {
                if (buff.Blueprint != null && buff.Blueprint.Harmful) return true;
                var caster = buff.MaybeContext?.MaybeCaster;
                return caster != null && caster.IsEnemy(unit);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ★ v0.3.2: single-target effects (damage, heal, target buffs, removals), as opposed to caster-only side effects.
        /// When none of them applies to a target, aiming the ability at it does nothing useful there (e.g. Lay On Hands -
        /// Others on a living enemy, whose only remaining effect is a buff on the paladin).
        /// </summary>
        /// <summary>
        /// ★ v0.3.8: puts a mark on the target that only helps the caster's own attacks against it (Smite Evil, Studied
        /// Target: AttackBonusAgainstTarget / DamageBonusAgainstTarget with CheckCaster only). Worth casting only on the
        /// enemy the caster is about to attack.
        /// </summary>
        public bool MarksForOwnAttacks { get; private set; }

        private ConditionsChecker m_AreaCondition;
        private EffectGuard[] m_AreaGuard;
        private bool m_ReadSharedAreaPlan;
        private SharedAreaPreview.Plan m_SharedAreaPlan;
        internal SharedAreaPreview.Plan SharedAreaPlan
        {
            get
            {
                if (!m_ReadSharedAreaPlan) { m_ReadSharedAreaPlan = true; m_SharedAreaPlan = SharedAreaPreview.Plan.Create(this); }
                return m_SharedAreaPlan;
            }
        }

        /// <summary>
        /// ★ v0.3.8: the area spell can affect this unit at all: its AbilityTargetsAround filter (Sleep: few hit dice) is
        /// evaluated like the game does, per unit. Unknown conditions count as "reaches". Orvath cast Sleep twice in one
        /// turn on the Cultist Champion; the filter was never read.
        /// </summary>
        public bool AreaReaches(AbilityData ability, UnitEntityData unit)
        {
            if (unit != null && !KineticAreaPreview.Reaches(this, unit)) return false;
            if (m_AreaCondition == null || unit == null) return true;
            var guard = m_AreaGuard ?? (m_AreaGuard = new[] { new EffectGuard { Checker = m_AreaCondition, Branch = true } });
            if (Evaluate(guard, ability, unit) != EffectApplies.No) return true;
            // ★ v0.3.9: once per ability (v0.3.8 logged every unit: ~20 lines for Soul Power, whose filter wants corpses).
            if (ReportedUnknown.Add($"area|{Blueprint.AssetGuid}"))
            {
                string types = string.Join(",", m_AreaCondition.Conditions.Where(c => c != null).Select(c => c.GetType().Name).Distinct());
                Main.Log($"[Profile] {ability.Name}: its target filter ({types}) excludes some units, e.g. {unit.CharacterName} " +
                         $"(level {unit.Descriptor?.Progression?.CharacterLevel}); those are not counted");
            }
            return false;
        }

        /// <summary>
        /// ★ v0.3.14: how <paramref name="ability"/> is aimed at <paramref name="unit"/>: the unit itself, or its spot for a
        /// spell cast at a point (Entangle, Grease, Web). Null when neither is allowed. Shared by both engines: real time
        /// only tried the unit, so point-cast area spells were never aimed at enemies.
        /// </summary>
        public static TargetWrapper TargetOn(AbilityData ability, UnitEntityData unit)
        {
            if (ability == null || unit == null) return null;
            // ★ v0.3.18: an area ability aimed at a creature is applied to that creature first: the game checks its spell
            // resistance and immunities there (AbilityExecutionProcess.DoApplyEffect, RuleSpellResistanceCheck), and when
            // it is immune the whole effect is lost - the area is never created. Orvath's Grease aimed at an airborne
            // Quasit (immune to ground effects) did nothing, though the area would have caught the enemies around it.
            // Aimed at the ground where the creature stands, there is no such check: same area, same place.
            // ★ v0.3.18 (review): only where that check would stop the area - not for an ability that picks its targets
            // (AbilityTargetsAround: each one gets its own check) or one with an attack roll (a bomb's direct hit needs
            // the creature as its target, AbilityDeliverProjectile).
            var profile = Get(ability);
            if (ability.CanTargetPoint && unit != ability.Caster?.Unit && profile != null && profile.IsAoE &&
                !profile.SelectsTargets && !profile.NeedsAttackRoll)
            {
                var ground = new TargetWrapper(unit.Position);
                if (ability.CanTarget(ground)) return ground;
            }
            var onUnit = new TargetWrapper(unit);
            if (ability.CanTarget(onUnit)) return onUnit;
            if (ability.CanTargetPoint)
            {
                var onPoint = new TargetWrapper(unit.Position);
                if (ability.CanTarget(onPoint)) return onPoint;
            }
            return null;
        }

        /// <summary>
        /// ★ v0.3.14: Selective metamagic keeps allies out of every part of this ability, by the game's rules: a burst
        /// only when its target filter has no "is ally" condition, a lasting area only when the game's (possibly
        /// mod-patched) spawn check says so (<see cref="SelectiveRule"/>). Tabletop Tweaks makes lasting areas such as
        /// Entangle affect allies even with Selective; v0.3.13 counted every ally as spared.
        /// </summary>
        public bool SelectiveSparesAllies(AbilityData ability)
        {
            if (ability == null || !ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Selective)) return false;
            if (m_AreaCondition != null && m_AreaCondition.HasIsAllyCondition()) return false;
            foreach (var area in m_VisitedAreas)
                if (!SelectiveRule.AreaSparesAllies(ability, area)) return false;
            return true;
        }

        /// <summary>
        /// ★ v0.3.12: the first ally this area would harm when centred on <paramref name="center"/>, or null. The same
        /// rule as the turn-based friendly-fire check: enemy-only areas and Selective metamagic spare allies; the area's
        /// own target filter applies; control without damage only counts for allies it would actually affect (not
        /// already under it, not immune). Used by the real-time engine, whose own area penalty never reached its score.
        /// </summary>
        public UnitEntityData AllyCaught(AbilityData ability, Vector3 center, IEnumerable<UnitEntityData> allies)
        {
            if (!IsAoE || AoETargets == Kingmaker.UnitLogic.Abilities.Components.TargetType.Enemy) return null;
            if (!DealsDamage && !HarmfulEffects && ControlStrength == ControlStrength.None) return null;
            if (SelectiveSparesAllies(ability)) return null;
            foreach (var ally in allies)
            {
                if (ally == null || Vector3.Distance(ally.Position, center) > AoERadius + ally.Corpulence) continue;
                if (!AreaReaches(ability, ally)) continue;
                if (!DealsDamage)
                {
                    bool affected = TargetBuffs.Any(b => BuffAppliesTo(b, ability, ally) != EffectApplies.No && !ally.Buffs.HasFact(b));
                    if (!affected || (Control != CCType.None && AbilityClassifier.HasCCImmunity(ally, Control))) continue;
                }
                return ally;
            }
            return null;
        }

        private static bool IsCasterOnlyAttackMark(BlueprintBuff buff)
        {
            try
            {
                // true = helps only the caster, false = helps the caster's friends too
                var casterOnly = buff.GetComponents<Kingmaker.Designers.Mechanics.Buffs.AttackBonusAgainstTarget>()
                                     .Select(c => c.CheckCaster && !c.CheckCasterFriend)
                                     .Concat(buff.GetComponents<Kingmaker.Designers.Mechanics.Buffs.DamageBonusAgainstTarget>()
                                                 .Select(c => c.CheckCaster && !c.CheckCasterFriend))
                                     .ToList();
                return casterOnly.Count > 0 && casterOnly.All(x => x);
            }
            catch
            {
                return false;
            }
        }

        public bool HasTargetEffects => Damage.Count > 0 || Heals.Count > 0 || TargetBuffs.Count > 0 || m_Removals.Count > 0 ||
                                        Maneuvers.Count > 0;

        public bool AnyTargetEffectApplies(AbilityData ability, UnitEntityData target)
        {
            return Damage.Any(d => AppliesTo(d, ability, target) != EffectApplies.No) ||
                   Heals.Any(h => AppliesTo(h, ability, target) != EffectApplies.No) ||
                   TargetBuffs.Any(b => BuffAppliesTo(b, ability, target) != EffectApplies.No) ||
                   m_Removals.Any(r => AppliesTo(r, ability, target) != EffectApplies.No) ||
                   Maneuvers.Any(m => AppliesTo(m.Action, ability, target) != EffectApplies.No);
        }

        private static readonly HashSet<BlueprintAbility> Described = new HashSet<BlueprintAbility>();

        /// <summary>★ v0.3.2: once per ability, what its buffs are made of (evidence for tuning from play logs).</summary>
        private readonly HashSet<string> m_ActionTypes = new HashSet<string>();
        private static readonly HashSet<BlueprintAbility> StructureDescribed = new HashSet<BlueprintAbility>();

        /// <summary>
        /// ★ v0.3.8: for abilities with no recognised effect (Coup de Grace was scored as "utility 1.0"): log once what the
        /// blueprint is made of, so a later version can value it from evidence instead of guesses.
        /// </summary>
        public void DescribeStructureOnce()
        {
            if (!StructureDescribed.Add(Blueprint)) return;
            try
            {
                var components = (Blueprint.ComponentsArray ?? new BlueprintComponent[0])
                    .Where(c => c != null).Select(c => c.GetType().Name).Distinct();
                Main.Log($"[Profile] {Blueprint.Name} ({Blueprint.AssetGuid}): no recognised effect - components=[{string.Join(",", components)}] " +
                         $"actions=[{string.Join(",", m_ActionTypes.OrderBy(t => t))}] range={Blueprint.Range} " +
                         $"fullRound={Blueprint.IsFullRoundAction} type={Blueprint.ActionType}");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Profile] structure of {Blueprint.name}: {ex.Message}");
            }
        }

        public void DescribeOnce()
        {
            if (!Described.Add(Blueprint)) return;
            try
            {
                var parts = TargetBuffs.Concat(CasterBuffs).Distinct()
                    .Select(b => BuffEffectAnalyzer.Analyze(b).ToString()).ToList();
                if (RemovesAnything)
                    parts.Add($"removes {RemovedDescriptors}{(RemovedBuffs.Count > 0 ? " + " + string.Join(",", RemovedBuffs.Select(b => b.name)) : "")}");
                if (IsOneShot) parts.Add(BuffsLastOneRound ? "lasts 1 round" : "spent by next attack");
                // ★ v0.3.14: which conditions decide whether a target gets the buff (Seelah smote a Rat Swarm: either it is
                // evil in the game's data or Smite's evil check is not where the profile looks).
                foreach (var buff in TargetBuffs.Distinct())
                {
                    if (!m_BuffGuards.TryGetValue(buff, out var branches) || branches.Any(g => g.Length == 0)) continue;
                    parts.Add($"{buff.name} only if " + string.Join(" or ", branches.Select(guards => string.Join(" & ", guards.Select(g =>
                        (g.Branch ? "" : "not ") + (g.OnCaster ? "caster " : "") +
                        string.Join("+", (g.Checker?.Conditions ?? new Condition[0]).Where(c => c != null)
                                                                                  .Select(c => (c.Not ? "!" : "") + c.GetType().Name.Replace("ContextCondition", "")))))).Distinct()));
                }
                if (parts.Count > 0)
                    Main.Log($"[Profile] {Blueprint.Name}: {string.Join(" ; ", parts)}");
            }
            catch { }
        }

        #endregion

        #region Reflection helpers

        private static readonly Dictionary<Type, FieldInfo[]> ActionFields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, FieldInfo[]> AbilityRefFields = new Dictionary<Type, FieldInfo[]>();
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private bool? m_SummonCatchesAllies;

        /// <summary>
        /// ★ v0.3.18 (test11): a creature it summons has an ability of its own that spawns an area able to affect its side
        /// (a Dretch's Stinking Cloud). The game's AI runs summons and casts such an area where the fight is, on the
        /// party's melee fighters too: Camellia was nauseated for five rounds and Seelah for six by the Dretch Orvath
        /// summoned to open every fight (0.3.18-test10 real-time log), and Orvath walked into its cloud in turn-based.
        /// Read from the creature's own facts (and the facts those add), one level deep.
        /// </summary>
        public bool SummonCatchesAllies
        {
            get
            {
                if (m_SummonCatchesAllies.HasValue) return m_SummonCatchesAllies.Value;
                bool found = false;
                try
                {
                    foreach (var creature in SummonedUnits)
                    {
                        var facts = new List<Kingmaker.Blueprints.Facts.BlueprintUnitFact>();
                        foreach (var fact in creature.AddFacts) if (fact != null) facts.Add(fact);
                        foreach (var fact in facts.ToList())
                            foreach (var add in fact.GetComponents<AddFacts>())
                                foreach (var more in add.Facts) if (more != null) facts.Add(more);
                        foreach (var ability in facts.OfType<BlueprintAbility>())
                        {
                            var run = ability.GetComponent<AbilityEffectRunAction>();
                            if (run?.Actions?.Actions != null && SpawnsAllyArea(run.Actions.Actions, 0)) { found = true; break; }
                        }
                        if (found) break;
                    }
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Profile] summoned creature abilities: {ex.Message}");
                }
                m_SummonCatchesAllies = found;
                return found;
            }
        }

        private static bool SpawnsAllyArea(GameAction[] actions, int depth)
        {
            if (actions == null || depth > 4) return false;
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action is ContextActionSpawnAreaEffect spawn && spawn.AreaEffect != null && spawn.AreaEffect.CanTargetAllies)
                    return true;
                foreach (var child in ChildActionLists(action))
                    if (SpawnsAllyArea(child, depth + 1)) return true;
            }
            return false;
        }

        private static IEnumerable<GameAction[]> ChildActionLists(GameAction action)
        {
            var type = action.GetType();
            if (!ActionFields.TryGetValue(type, out var fields))
            {
                fields = AllFields(type).Where(f =>
                    f.FieldType == typeof(ActionList) ||
                    f.FieldType == typeof(GameAction[]) ||
                    typeof(GameAction).IsAssignableFrom(f.FieldType)).ToArray();
                ActionFields[type] = fields;
            }

            foreach (var f in fields)
            {
                object value;
                try { value = f.GetValue(action); } catch { continue; }
                switch (value)
                {
                    case ActionList list when list.Actions != null:
                        yield return list.Actions;
                        break;
                    case GameAction[] array:
                        yield return array;
                        break;
                    case GameAction single:
                        yield return new[] { single };
                        break;
                }
            }
        }

        private static IEnumerable<BlueprintAbility> ReferencedAbilities(GameAction action)
        {
            var type = action.GetType();
            if (!AbilityRefFields.TryGetValue(type, out var fields))
            {
                fields = AllFields(type).Where(f => f.FieldType == typeof(BlueprintAbilityReference)).ToArray();
                AbilityRefFields[type] = fields;
            }

            foreach (var f in fields)
            {
                BlueprintAbility bp = null;
                try { bp = (f.GetValue(action) as BlueprintAbilityReference)?.Get(); } catch { }
                if (bp != null) yield return bp;
            }
        }

        private static IEnumerable<FieldInfo> AllFields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(Fields | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        #endregion

        #region Control classification

        /// <summary>
        /// ★ v0.3.18 (test10): the buffs it puts on its targets impose no condition but a fall (Prone) - or none, the effect
        /// living in a component the AI does not read (Grease's buff knocks down from its own actions). An area of such an
        /// ability only hinders movement; one that holds (Entangled, CantMove), blinds or sickens does more.
        /// </summary>
        public bool OnlyKnocksDown => TargetBuffs.All(b => ConditionsOf(b).All(c => c == UnitCondition.Prone));

        /// <summary>
        /// ★ v0.3.18 (test10): the share of an enemy's worth per round that this buff takes while it lasts, on the
        /// engine's control scale (hard control 1, soft 0.4, a plain debuff 0.25): from its descriptor and conditions, as
        /// a profile classifies the buffs it applies.
        /// </summary>
        internal static float ControlShareOf(BlueprintBuff buff)
        {
            if (buff == null) return 0f;
            try
            {
                var strength = DescriptorControl(buff.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None);
                var s = BuffControl(buff);
                if (s > strength) strength = s;
                return strength == ControlStrength.Hard ? 1f : strength == ControlStrength.Soft ? 0.4f :
                       strength == ControlStrength.Minor ? 0.15f : 0.25f;
            }
            catch
            {
                return 0.25f;
            }
        }

        /// <summary>
        /// ★ v0.3.20 (test5): the control a descriptor alone says. Compulsion is how a spell works (it compels), not what
        /// it does: Bane and Bless carry it with Fear and Emotion, Hold Person with Paralysis, Daze with Daze. It counted
        /// as Charm (hard control), so the Neophyte Cleric's Bane (-1 to attacks and against fear) was valued as taking an
        /// enemy out of the fight - "Bane 24 on Seelah" made him twice as dangerous as his fellows (0.3.20-test4 log), and
        /// the party's own Bane counted the same. A compulsion's real effect is named by its other descriptors, its
        /// conditions or a faction change (<see cref="BuffControl"/>).
        /// </summary>
        private static ControlStrength DescriptorControl(SpellDescriptor descriptor) =>
            ClassifyControl(AbilityClassifier.AnalyzeCCTypeFromDescriptor(descriptor & ~SpellDescriptor.Compulsion));

        /// <summary>★ v0.3.20 (test5): the control a buff imposes by its conditions, or by turning its bearer to the
        /// caster's side (ChangeFaction: Dominate Person, whose descriptor is only Compulsion and Mind-Affecting).</summary>
        internal static ControlStrength BuffControl(BlueprintBuff buff)
        {
            var strength = ControlStrength.None;
            if (buff == null) return strength;
            if (buff.GetComponent<Kingmaker.UnitLogic.FactLogic.ChangeFaction>() != null) return ControlStrength.Hard;
            // ★ v0.3.21: Command (Flee, Approach): the game walks its bearer away from or up to the caster and takes it out
            // of its own control (SetFleeOrApproachLogic: UnitPartFleeOrApproach, PreventDirectControl) - its turn is lost.
            if (buff.GetComponent<SetFleeOrApproachLogic>() != null) return ControlStrength.Hard;
            foreach (var condition in ConditionsOf(buff))
            {
                var s = ClassifyCondition(condition);
                if (s > strength) strength = s;
            }
            return strength;
        }

        private static readonly FieldInfo CustomMeleeType = HarmonyLib.AccessTools.Field(typeof(AbilityCustomMeleeAttack), "m_Type");

        /// <summary>★ v0.3.21: the custom melee attack is the game's Devastating Blow (its type 1; the only one in 1.4).</summary>
        private static bool IsDevastatingBlow(AbilityCustomMeleeAttack melee)
        {
            if (melee == null || CustomMeleeType == null) return false;
            try { return Convert.ToInt32(CustomMeleeType.GetValue(melee)) == 1; }
            catch { return false; }
        }

        private static ControlStrength ClassifyControl(CCType cc)
        {
            if ((cc & CCType.HardCC) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Confusion | CCType.Daze | CCType.Charm | CCType.Nauseate)) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Blind | CCType.Slow | CCType.Entangle | CCType.MovementImpair)) != 0) return ControlStrength.Soft;
            // ★ v0.3.18 (test10): a fear descriptor alone is Shaken's (-2 to attacks and saves); real fear raises it by its
            // condition (Frightened, Cowering: hard control).
            if ((cc & CCType.Fear) != 0) return ControlStrength.Minor;
            return ControlStrength.None;
        }

        private static ControlStrength ClassifyCondition(UnitCondition condition)
        {
            switch (condition)
            {
                case UnitCondition.Sleeping:
                case UnitCondition.Paralyzed:
                case UnitCondition.Stunned:
                case UnitCondition.Helpless:
                case UnitCondition.CantAct:
                case UnitCondition.Confusion:
                case UnitCondition.Dazed:
                case UnitCondition.Petrified:
                case UnitCondition.Nauseated:
                case UnitCondition.Cowering:
                case UnitCondition.Frightened:
                    return ControlStrength.Hard;
                // ★ v0.3.18 (test10): -2 to attacks, saves and checks (Shaken, Sickened), -1 to attacks (Dazzled), -2 Str /
                // Dex (Fatigued): a penalty, not control. As soft control (0.4 x the enemy's worth a round, like Entangle or
                // Blindness) Demoralize beat attacks on adjacent enemies and was cast 52 times in one turn-based log, 62 in a
                // real-time one; Flare beat attacks too.
                case UnitCondition.Shaken:
                case UnitCondition.Sickened:
                case UnitCondition.Dazzled:
                case UnitCondition.Fatigued:
                // ★ v0.3.21 (gaps): half speed in a lingering area (Ice Storm, Stone Call, Tar Pool): it slows an approach,
                // nothing more - not the flat 0.25 of an unread debuff.
                case UnitCondition.DifficultTerrain:
                    return ControlStrength.Minor;
                case UnitCondition.Entangled:
                case UnitCondition.Slowed:
                case UnitCondition.Staggered:
                case UnitCondition.Blindness:
                case UnitCondition.Exhausted:
                case UnitCondition.Prone:
                case UnitCondition.CantMove:
                    return ControlStrength.Soft;
                default:
                    return ControlStrength.None;
            }
        }

        #endregion
    }
}
