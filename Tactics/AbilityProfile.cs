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
    public enum ControlStrength { None = 0, Soft = 1, Hard = 2 }

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
        public bool Dispels { get; private set; }
        public bool RemovesBuffs { get; private set; }
        public bool Enchants { get; private set; }

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
        /// <summary>One round or one attack's worth of effect.</summary>
        public bool IsOneShot => BuffsLastOneRound || BuffsSpentByAttack;

        public SavingThrowType Save { get; private set; } = SavingThrowType.Unknown;
        public bool HalfOnSave { get; private set; }
        public bool NeedsAttackRoll { get; private set; }
        /// <summary>
        /// ★ v0.3.18: delivered by a projectile with an attack roll (rays, kinetic blasts): a ranged touch attack roll, which
        /// provokes attacks of opportunity from enemies engaging the caster even when the spell was cast defensively.
        /// </summary>
        public bool RangedAttackRoll { get; private set; }
        public bool IsCharge { get; private set; }

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
        public ControlStrength ControlStrength { get; private set; }

        public bool HarmfulToEnemies => Blueprint.EffectOnEnemy == AbilityEffectOnUnit.Harmful;
        public bool HelpfulToAllies => Blueprint.EffectOnAlly == AbilityEffectOnUnit.Helpful;

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
            if (!HarmfulEffects && ControlStrength == ControlStrength.None) return false;
            return TargetBuffs.Any(b => BuffAppliesTo(b, ability, target) != EffectApplies.No);
        }

        /// <summary>Longest buff duration category we saw (rounds = short, minutes or longer = long).</summary>
        public bool HasLongBuff { get; private set; }

        public bool HasDetectableEffect =>
            Damage.Count > 0 || Heals.Count > 0 || CasterHeals.Count > 0 || TargetBuffs.Count > 0 || CasterBuffs.Count > 0 ||
            Summons || Dispels || RemovesBuffs || Enchants;

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
        private int m_ShortBuffs, m_SpentByAttackBuffs, m_AllBuffs;
        private float m_AreaRadius;
        private bool m_AreaHitsAllies, m_AreaHitsEnemies;
        /// <summary>★ v0.3.7: walking a spawned area's actions (its buffs last while the area does, not for minutes).</summary>
        private bool m_InArea;
        private readonly HashSet<BlueprintAbilityAreaEffect> m_VisitedAreas = new HashSet<BlueprintAbilityAreaEffect>();

        /// <summary>★ v0.3.13: the areas this ability spawns (Entangle, Grease, Web...).</summary>
        public IEnumerable<BlueprintAbilityAreaEffect> SpawnedAreas => m_VisitedAreas;

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
            ClearConditionCache();
        }

        /// <summary>Per-target condition results are keyed by unit id; drop them when a combat ends.</summary>
        public static void ClearConditionCache() => CheckerCache.Clear();

        #endregion

        #region Analysis

        private void Analyze()
        {
            var visited = new HashSet<BlueprintAbility>();
            AnalyzeAbility(Blueprint, visited, 0, EffectSubject.Target);
            BuffsLastOneRound = m_AllBuffs > 0 && m_ShortBuffs == m_AllBuffs;
            BuffsSpentByAttack = m_AllBuffs > 0 && m_SpentByAttackBuffs == m_AllBuffs;
            MarksForOwnAttacks = TargetBuffs.Any(IsCasterOnlyAttackMark);

            // A personal ability's caster IS its target: its caster heals are heals of the target.
            if (Blueprint.Range == AbilityRange.Personal && CasterHeals.Count > 0)
            {
                Heals.AddRange(CasterHeals);
                CasterHeals.Clear();
            }

            IsCharge = Blueprint.GetComponent<AbilityCustomCharge>() != null;
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

            // Control: the spell's own descriptor plus the descriptors / conditions of what it applies.
            SpellDescriptor descriptor = Blueprint.SpellDescriptor;
            foreach (var buff in TargetBuffs)
            {
                var comp = buff.GetComponent<SpellDescriptorComponent>();
                if (comp != null) descriptor |= comp.Descriptor.Value;
            }
            Control = AbilityClassifier.AnalyzeCCTypeFromDescriptor(descriptor);
            ControlStrength = ClassifyControl(Control);
            foreach (var buff in TargetBuffs)
            {
                foreach (var condition in ConditionsOf(buff))
                {
                    var s = ClassifyCondition(condition);
                    if (s > ControlStrength) ControlStrength = s;
                }
            }

            // ★ v0.3.12: what it puts on its target is harmful, whatever the blueprint's intent flags say. Orvath's
            // Entangle (not flagged Harmful) was cast on himself as a "buff" of 9.4: its Entangled buff counted as an
            // unrecognised beneficial buff. Abilities flagged helpful to allies, and personal abilities (their target
            // is the caster: a condition there is a drawback, not an attack), keep their drawbacks out of this.
            AppliesHarmfulBuffs = !HelpfulToAllies && Blueprint.Range != AbilityRange.Personal && TargetBuffs.Any(IsHarmfulBuff);
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

        private static bool ContainsCasterProvoke(GameAction[] actions, int depth)
        {
            if (actions == null || depth > 6) return false;
            foreach (var action in actions)
            {
                if (action is ContextActionProvokeAttackOfOpportunity provoke && provoke.ApplyToCaster) return true;
                foreach (var child in ChildActionLists(action))
                    if (ContainsCasterProvoke(child, depth + 1)) return true;
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
        private static bool IsHarmfulBuff(BlueprintBuff buff)
        {
            try
            {
                return buff.Harmful || ConditionsOf(buff).Any(c => ClassifyCondition(c) != ControlStrength.None);
            }
            catch
            {
                return false;
            }
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
                    if (action is ContextActionSpawnMonster) Summons = true;
                    foreach (var child in ChildActionLists(action))
                        Walk(child, visited, abilityDepth, subject, depth + 1, guards);
                    continue;
                }

                switch (action)
                {
                    case ContextActionDealDamage dmg:
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
                        if (toCaster || apply.ToCaster) CasterBuffs.Add(apply.Buff);
                        else TargetBuffs.Add(apply.Buff);
                        AddBuffGuards(apply.Buff, guards);
                        // ★ v0.3.15: each application with its own branch and duration (Color Spray: 2d4 rounds on 2 HD
                        // or less, 1d4 on 3-4 HD, 1 round on 5 HD or more).
                        if (!(toCaster || apply.ToCaster) && !m_InArea)
                            m_BuffApplications.Add(new BuffApplication { Buff = apply.Buff, Guards = guards, Rounds = ExpectedRounds(apply) });
                        m_AllBuffs++;
                        if (LastsOneRound(apply)) m_ShortBuffs++;
                        // ★ v0.3.7: a buff applied by a spawned area is tied to the area, whatever its own duration.
                        if (m_InArea) { if (IsSpentByUse(apply.Buff)) m_SpentByAttackBuffs++; break; }
                        if (apply.Permanent || (apply.DurationValue != null && (int)apply.DurationValue.Rate > 0))
                            HasLongBuff = true;
                        if (LastsADay(apply)) m_DayLongBuffs.Add(apply.Buff);
                        if (apply.Permanent || (apply.DurationValue != null && apply.DurationValue.Rate >= DurationRate.Hours))
                            m_PersistentBuffs.Add(apply.Buff);
                        if (IsSpentByUse(apply.Buff)) m_SpentByAttackBuffs++;
                        break;
                    case ContextActionSpawnMonster _:
                        Summons = true;
                        break;
                    case ContextActionSpawnAreaEffect spawnArea:
                        WalkAreaEffect(spawnArea.AreaEffect, visited, abilityDepth, subject, depth, guards);
                        break;
                    case ContextActionDispelMagic _:
                        Dispels = true;
                        break;
                    case ContextActionEnchantWornItem enchant when subject != EffectSubject.Other:
                        m_Enchantments.Add(new KeyValuePair<ContextActionEnchantWornItem, bool>(
                            enchant, toCaster || enchant.ToCaster || subject == EffectSubject.Caster));
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
                    if (Damage.Count > afterEnter && afterEnter > before)
                        Damage.RemoveRange(before, afterEnter - before);
                    int beforeMove = Damage.Count;
                    Walk(run.UnitMove?.Actions, visited, abilityDepth, subject, depth + 1, guards);
                    if (Damage.Count > beforeMove)
                        Damage.RemoveRange(beforeMove, Damage.Count - beforeMove);
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
        private static bool IsSpentByUse(BlueprintBuff buff)
        {
            try
            {
                if (buff.GetComponent<RemoveBuffOnAttack>() != null) return true;
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
        }

        private readonly List<BuffApplication> m_BuffApplications = new List<BuffApplication>();

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
        public float EffectRoundsOn(AbilityData ability, UnitEntityData target)
        {
            if (m_BuffApplications.Count == 0 || target == null) return -1f;
            float sure = -1f, maybe = float.MaxValue;
            foreach (var a in m_BuffApplications)
            {
                if (a.Rounds <= 0f) continue;
                var applies = Evaluate(a.Guards, ability, target);
                if (applies == EffectApplies.No) continue;
                if (applies == EffectApplies.Unknown) maybe = Math.Min(maybe, a.Rounds);
                else sure = Math.Max(sure, a.Rounds);
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
        private static bool IsSafe(Condition c) =>
            c == null || SafeConditions.Contains(c.GetType()) || c is ContextConditionHitDice;

        /// <summary>
        /// ★ v0.3.17: the range of a shared value as the ability's own ContextCalculateSharedValue defines it (dice count
        /// and bonus from the context, without rolling): lowest, average and highest. No component: 0, as in the game.
        /// </summary>
        private static void SharedValueRange(AbilityData ability, MechanicsContext context, AbilitySharedValue type,
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
            foreach (var r in ranges) context[r.Type] = r.Lo;
            bool low = Run(checker);
            foreach (var r in ranges) context[r.Type] = r.Hi;
            bool high = Run(checker);
            if (low == high) return low;
            foreach (var r in ranges) context[r.Type] = r.Mid;
            return Run(checker);
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
                var context = CombatMath.CreateContext(ability, new TargetWrapper(target));
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

        private static bool Run(ConditionsChecker checker)
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

        private sealed class CheckerResult
        {
            public int Frame;
            public bool? Result;
        }

        /// <summary>Checker outcome per (checker, target) within one frame; a decision evaluates many candidates.</summary>
        private static readonly Dictionary<ConditionsChecker, Dictionary<string, CheckerResult>> CheckerCache =
            new Dictionary<ConditionsChecker, Dictionary<string, CheckerResult>>();

        private static readonly HashSet<string> ReportedUnknown = new HashSet<string>();

        public EffectApplies AppliesTo(GameAction effect, AbilityData ability, UnitEntityData target)
        {
            return Evaluate(effect != null && m_Guards.TryGetValue(effect, out var g) ? g : NoGuards, ability, target);
        }

        public EffectApplies BuffAppliesTo(BlueprintBuff buff, AbilityData ability, UnitEntityData target)
        {
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
                var subject = guard.OnCaster ? caster : target;
                if (subject == null) { unknown = true; continue; }
                bool? result = CheckFor(guard.Checker, ability, subject, caster);
                if (!result.HasValue) { unknown = true; continue; }
                if (result.Value != guard.Branch) return EffectApplies.No;
            }
            return unknown ? EffectApplies.Unknown : EffectApplies.Yes;
        }

        private static bool? CheckFor(ConditionsChecker checker, AbilityData ability, UnitEntityData target, UnitEntityData caster)
        {
            int frame = Time.frameCount;
            // Caster-dependent conditions (IsAlly, CasterHasFact, HasBuffFromCaster...): key by caster and target.
            string key = $"{caster?.UniqueId}|{target.UniqueId}";
            if (!CheckerCache.TryGetValue(checker, out var perUnit))
                CheckerCache[checker] = perUnit = new Dictionary<string, CheckerResult>();
            if (perUnit.TryGetValue(key, out var cached) && cached.Frame == frame) return cached.Result;

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
                    var context = CombatMath.CreateContext(ability, new TargetWrapper(target));
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

            perUnit[key] = new CheckerResult { Frame = frame, Result = result };
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

        /// <summary>
        /// ★ v0.3.8: the area spell can affect this unit at all: its AbilityTargetsAround filter (Sleep: few hit dice) is
        /// evaluated like the game does, per unit. Unknown conditions count as "reaches". Orvath cast Sleep twice in one
        /// turn on the Cultist Champion; the filter was never read.
        /// </summary>
        public bool AreaReaches(AbilityData ability, UnitEntityData unit)
        {
            if (m_AreaCondition == null || unit == null) return true;
            var guard = new[] { new EffectGuard { Checker = m_AreaCondition, Branch = true } };
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
            if (ability.CanTargetPoint && unit != ability.Caster?.Unit && (Get(ability)?.IsAoE ?? false))
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

        public bool HasTargetEffects => Damage.Count > 0 || Heals.Count > 0 || TargetBuffs.Count > 0 || m_Removals.Count > 0;

        public bool AnyTargetEffectApplies(AbilityData ability, UnitEntityData target)
        {
            return Damage.Any(d => AppliesTo(d, ability, target) != EffectApplies.No) ||
                   Heals.Any(h => AppliesTo(h, ability, target) != EffectApplies.No) ||
                   TargetBuffs.Any(b => BuffAppliesTo(b, ability, target) != EffectApplies.No) ||
                   m_Removals.Any(r => AppliesTo(r, ability, target) != EffectApplies.No);
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

        private static ControlStrength ClassifyControl(CCType cc)
        {
            if ((cc & CCType.HardCC) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Confusion | CCType.Daze | CCType.Charm | CCType.Nauseate)) != 0) return ControlStrength.Hard;
            if ((cc & (CCType.Fear | CCType.Blind | CCType.Slow | CCType.Entangle | CCType.MovementImpair)) != 0) return ControlStrength.Soft;
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
                case UnitCondition.Shaken:
                case UnitCondition.Sickened:
                case UnitCondition.Entangled:
                case UnitCondition.Slowed:
                case UnitCondition.Staggered:
                case UnitCondition.Blindness:
                case UnitCondition.Fatigued:
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
