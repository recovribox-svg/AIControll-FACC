// ★ v0.3.0: Expected-value math from the game's own rules.
//
// Weapon attacks: RuleCalculateAttackBonus + RuleCalculateAC + RuleCalculateWeaponStats, triggered exactly the way the
// game itself does for read-only purposes (PowerAttackWatcher, UnitDescriptionHelper, UIUtilityItem). These are pure
// calculation rules; a fake RuleAttackRoll is deliberately NOT used because its fortification branch rolls Dice.D100.
// The list of attacks in a full attack comes from UnitAttack { ForceFullAttack, OnlyForInspect }.Init, which is how the
// game's unit description UI enumerates them (hands, iterative penalties, natural limbs, haste...).
//
// Spells: dice are evaluated with the ability's own execution context after RecalculateRanks() (ContextRankConfig is
// deterministic); Recalculate() is avoided because shared values can roll dice. Save DC comes from
// AbilityData.CalculateParams().DC; spell resistance from the existing UnitPartSpellResistance-based helper.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class WeaponAttackEstimate
    {
        public float ExpectedDamage;
        public int Attacks;
        public float Reach;
        public bool IsRanged;
        public ItemEntityWeapon Weapon;
        public float FirstHitChance;
        /// <summary>★ v0.3.14 (test5): average damage of one hit of the first attack (after damage reduction).</summary>
        public float FirstPerHit;
        /// <summary>★ test5: chance the first attack is a critical hit (threat range × hit chance).</summary>
        public float FirstCritChance;
        public float CritMultiplier = 2f;
        /// <summary>★ v0.3.18: chance at least one of the attacks hits (the kill chance, ValueModel.KillChance).</summary>
        public float AnyHitChance;
    }

    public static class CombatMath
    {
        #region Primitives

        /// <summary>d20 hit probability with natural 1 / natural 20.</summary>
        public static float HitProbability(int attackBonus, int armorClass)
        {
            return Mathf.Clamp((21f + attackBonus - armorClass) / 20f, 0.05f, 0.95f);
        }

        public static float Average(DiceFormula dice)
        {
            int sides = dice.Dice.Sides();
            return sides <= 0 ? 0f : dice.Rolls * (sides + 1) / 2f;
        }

        public static float HpPercent(UnitEntityData unit)
        {
            int max = Math.Max(1, unit.MaxHP);
            return Mathf.Clamp01(unit.HPLeft / (float)max);
        }

        /// <summary>
        /// ★ v0.3.14: the HP fraction below which a heal on <paramref name="target"/> is worth casting: the character's
        /// "Heal at HP %" plus a margin (a heal lands after the next hits), or the global potion threshold for drinking
        /// one's own potion. One rule for both engines; real time never read the setting.
        /// </summary>
        public static float HealThreshold(AbilityData ability, UnitEntityData caster, UnitEntityData target,
                                          CompanionAI_Pathfinder.Settings.CharacterSettings settings)
        {
            if (ability?.SourceItem != null && target == caster)
                return (CompanionAI_Pathfinder.Settings.ModSettings.Instance?.PotionHealThresholdPercent ?? 50) / 100f;
            return Math.Max(0.35f, (settings?.HealAtHPPercent ?? 30) / 100f + 0.2f);
        }

        #endregion

        #region Weapon attacks

        /// <summary>
        /// Attack list the game would execute (full = iterative + off-hand + natural limbs, else the single attack).
        /// Empty when the unit cannot make weapon attacks at all.
        /// </summary>
        public static IList<AttackHandInfo> EnumerateAttacks(UnitEntityData unit, UnitEntityData target, bool full)
        {
            try
            {
                var probe = new UnitAttack(target) { ForceFullAttack = full, IsSingleAttack = !full, OnlyForInspect = true };
                probe.Init(unit);
                return probe.AllAttacks;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] EnumerateAttacks {unit.CharacterName}: {ex.Message}");
                return Array.Empty<AttackHandInfo>();
            }
        }

        public static WeaponAttackEstimate EstimateWeaponAttack(UnitEntityData unit, UnitEntityData target, bool full) =>
            EstimateWeaponAttack(unit, target, full, null);

        /// <summary>
        /// ★ v0.3.18 (AA-3): one attack of an attack routine as the game's rules give it now: attack bonus, the target's AC
        /// for it, the damage of a hit before damage reduction, critical range and multiplier, sneak attack, the DR it meets.
        /// </summary>
        private sealed class AttackLine
        {
            public ItemEntityWeapon Weapon;
            public int Index;
            public int AttackBonus, ArmorClass;
            public float RawPerHit, Sneak;
            public int DamageReduction;
            public float ThreatChance;
            public int CritMultiplier;
        }

        private sealed class AttackRoutine
        {
            public int Frame;
            public int Attacks;
            public ItemEntityWeapon FirstWeapon;
            public float Reach;
            public readonly List<AttackLine> Lines = new List<AttackLine>();
        }

        /// <summary>Attack routines read this frame: the attack-mode variants of one attack reuse the rules' results.</summary>
        private static readonly Dictionary<string, AttackRoutine> Routines = new Dictionary<string, AttackRoutine>();

        /// <summary>
        /// ★ v0.3.18 (AA-3): <paramref name="delta"/> gives, per attack (weapon, index in the routine), the attack and damage
        /// change of an attack mode switched on or off from its current state (TacticalEngine.Modes.cs). The rules' results
        /// are read once per frame and reused for every variant.
        /// </summary>
        public static WeaponAttackEstimate EstimateWeaponAttack(UnitEntityData unit, UnitEntityData target, bool full,
                                                                Func<ItemEntityWeapon, int, KeyValuePair<int, float>> delta)
        {
            var routine = ReadRoutine(unit, target, full);
            if (routine == null) return null;

            var result = new WeaponAttackEstimate { Attacks = routine.Attacks };
            result.Weapon = routine.FirstWeapon;
            result.IsRanged = routine.FirstWeapon?.Blueprint?.IsRanged ?? false;
            result.Reach = routine.Reach;
            float noHit = 1f;
            foreach (var line in routine.Lines)
            {
                var d = delta?.Invoke(line.Weapon, line.Index) ?? new KeyValuePair<int, float>(0, 0f);
                float p = HitProbability(line.AttackBonus + d.Key, line.ArmorClass);
                if (line.Index == 0) result.FirstHitChance = p;
                noHit *= 1f - p;
                float perHit = Math.Max(0f, line.RawPerHit + d.Value - line.DamageReduction);
                float critExtra = line.ThreatChance * p * (line.CritMultiplier - 1) * perHit;
                if (line.Index == 0)
                {
                    result.FirstPerHit = perHit;
                    result.FirstCritChance = line.ThreatChance * p * p;     // threat, then the confirmation roll
                    result.CritMultiplier = Math.Max(1, line.CritMultiplier);
                }
                result.ExpectedDamage += p * (perHit + line.Sneak) + critExtra;
            }
            result.AnyHitChance = 1f - noHit;
            return result;
        }

        /// <summary>★ v0.3.18: forget the attack routines read (a new decision, or a state changed for a preview).</summary>
        public static void ClearAttackCache() => Routines.Clear();

        private static AttackRoutine ReadRoutine(UnitEntityData unit, UnitEntityData target, bool full)
        {
            int frame = Time.frameCount;
            string key = $"{unit.UniqueId}|{target.UniqueId}|{full}";
            if (Routines.TryGetValue(key, out var cached) && cached.Frame == frame) return cached;
            var attacks = EnumerateAttacks(unit, target, full);
            if (attacks.Count == 0) return null;
            if (Routines.Count > 512) Routines.Clear();

            var routine = new AttackRoutine { Frame = frame, Attacks = attacks.Count, FirstWeapon = attacks[0].Weapon };
            routine.Reach = UnitAttack.GetApproachRadius(routine.FirstWeapon, unit, target);

            bool flanked = target.CombatState?.IsFlanked ?? false;
            int sneakDice = SafeStat(unit.Stats.SneakAttack);

            for (int i = 0; i < attacks.Count; i++)
            {
                var weapon = attacks[i].Weapon;
                if (weapon == null) continue;
                try
                {
                    int ab = Rulebook.Trigger(new RuleCalculateAttackBonus(unit, target, weapon, attacks[i].AttackBonusPenalty)
                    {
                        TargetIsFlanked = flanked
                    }).Result;
                    var acRule = Rulebook.Trigger(new RuleCalculateAC(unit, target, weapon.Blueprint.AttackType));

                    var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                    float perHit = 0f;
                    foreach (var d in stats.DamageDescription)
                        perHit += Average(d.Dice) + d.Bonus;

                    var dr = HitChanceCalculator.CalculateDamageReduction(unit, target, weapon, perHit);
                    routine.Lines.Add(new AttackLine
                    {
                        Weapon = weapon,
                        Index = i,
                        AttackBonus = ab,
                        ArmorClass = acRule.Result,
                        RawPerHit = perHit,
                        DamageReduction = dr != null && !dr.CanBypass && dr.DRValue > 0 ? dr.DRValue : 0,
                        ThreatChance = Mathf.Clamp01(stats.CriticalRange / 20f),
                        CritMultiplier = stats.CriticalMultiplier,
                        Sneak = sneakDice > 0 && (flanked || acRule.IsTargetFlatFooted) ? 3.5f * sneakDice : 0f
                    });
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Math] attack estimate {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                }
            }
            Routines[key] = routine;
            return routine;
        }

        /// <summary>
        /// ★ v0.3.9: expected effect of a coup de grace (Pathfinder rules): the attack hits automatically and is a critical
        /// hit with the wielded weapon; a target that survives must make a Fortitude save (DC 10 + damage dealt) or die.
        /// Creatures immune to critical hits and death effects (undead, constructs) only take the normal damage.
        /// Returns (damage, chance the target dies). Derived from the rules, not from a mod's component, so it works with
        /// and without Tabletop Tweaks (which replaces the vanilla implementation).
        /// </summary>
        public static bool EstimateCoupDeGrace(UnitEntityData unit, UnitEntityData target, out float damage, out float killChance)
        {
            damage = 0f;
            killChance = 0f;
            var weapon = unit?.Body?.PrimaryHand?.MaybeWeapon;
            if (weapon == null || target == null) return false;
            try
            {
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                float perHit = 0f;
                foreach (var d in stats.DamageDescription)
                    perHit += Average(d.Dice) + d.Bonus;
                var dr = HitChanceCalculator.CalculateDamageReduction(unit, target, weapon, perHit);
                if (dr != null && !dr.CanBypass && dr.DRValue > 0)
                    perHit = Math.Max(0f, perHit - dr.DRValue);

                bool immune = AbilityClassifier.IsUndeadOrConstruct(target);
                damage = perHit * (immune ? 1 : Math.Max(2, stats.CriticalMultiplier));
                int sneakDice = SafeStat(unit.Stats.SneakAttack);       // a helpless target is denied its Dex bonus
                if (sneakDice > 0 && !immune) damage += 3.5f * sneakDice;

                if (damage >= target.HPLeft) killChance = 1f;
                else if (!immune)
                {
                    int fort = SafeStat(target.Stats.SaveFortitude);
                    float dc = 10f + damage;
                    killChance = Mathf.Clamp((dc - fort - 1f) / 20f, 0.05f, 0.95f);
                }
                return true;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] coup de grace {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                return false;
            }
        }

        private static int SafeStat(ModifiableValue value)
        {
            try { return value?.ModifiedValue ?? 0; } catch { return 0; }
        }

        #endregion

        #region Spells and abilities

        public static AbilityExecutionContext CreateContext(AbilityData ability, TargetWrapper target)
        {
            try
            {
                var ctx = ability.CreateExecutionContext(target);
                ctx.RecalculateRanks();
                return ctx;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] context for {ability.Name}: {ex.Message}");
                return null;
            }
        }

        public static float EvaluateDice(ContextDiceValue value, MechanicsContext ctx, int casterLevel,
                                         bool maximize = false, bool empower = false)
        {
            if (value == null) return 0f;
            int count = EvaluateValue(value.DiceCountValue, ctx, casterLevel);
            int bonus = EvaluateValue(value.BonusValue, ctx, 0);
            int sides = value.DiceType.Sides();
            // ★ v0.3.13: the game's metamagic rule (RuleCalculateDamage, RuleHealDamage, ContextValueHelper): Maximize
            // takes the highest roll of every die, Empower multiplies dice + bonus by 1.5.
            float total = (maximize ? count * sides : count * (sides + 1) / 2f) + bonus;
            return empower ? total * 1.5f : total;
        }

        /// <summary>
        /// ★ v0.3.13: Maximize / Empower on this cast, including the caster's automatic metamagic
        /// (AbilityData.HasMetamagic reads UnitPartSpecialMetamagic: AutoMetamagic toggles such as IsekaiMod's
        /// "Overpowered Ability - Auto Empower/Maximize", metamagic rods).
        /// </summary>
        private static void DamageMetamagic(AbilityData ability, out bool maximize, out bool empower)
        {
            maximize = empower = false;
            try
            {
                maximize = ability.HasMetamagic(Metamagic.Maximize);
                empower = ability.HasMetamagic(Metamagic.Empower);
            }
            catch { }
        }

        private static int EvaluateValue(ContextValue value, MechanicsContext ctx, int fallback)
        {
            if (value == null) return 0;
            if (value.ValueType == ContextValueType.Simple) return value.Value;
            try
            {
                if (ctx != null)
                {
                    int v = value.Calculate(ctx);
                    if (v > 0) return v;
                }
            }
            catch { }
            return fallback;
        }

        public static float EstimateAbilityDamage(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Damage.Count == 0) return 0f;
            if (target != null && profile.Damage.All(d => profile.AppliesTo(d, ability, target) == EffectApplies.No)) return 0f;
            var ctx = CreateContext(ability, target != null ? new TargetWrapper(target) : null);
            int cl = Math.Max(1, ability.CalculateParams()?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            var parts = new List<KeyValuePair<GameAction, float>>();
            foreach (var action in profile.Damage)
            {
                // ★ v0.3.1: only the branch that runs for this target (Inflict Wounds heals undead, Cure harms them).
                if (target != null && profile.AppliesTo(action, ability, target) == EffectApplies.No) continue;
                switch (action)
                {
                    case ContextActionDealDamage dmg:
                        float avg = EvaluateDice(dmg.Value, ctx, Math.Min(cl, 10), maximize, empower);
                        if (dmg.Half) avg *= 0.5f;
                        parts.Add(new KeyValuePair<GameAction, float>(dmg, AfterTargetReduction(target, dmg, avg)));
                        break;
                    // A kill is valued apart (KillApplies): it lands on a failed save, not half on a passed one.
                    case ContextActionKill _:
                        break;
                    case IDealDamageProvider provider:
                        if (target != null) parts.Add(new KeyValuePair<GameAction, float>(action, PreviewDamage(ability, ctx, provider, target)));
                        break;
                }
            }
            return SumAlternatives(profile, parts);
        }

        /// <summary>
        /// ★ v0.3.14 (test5): effects in opposite branches of one condition are alternatives: count the larger, not both
        /// (AbilityProfile.Exclusive).
        /// </summary>
        private static float SumAlternatives(AbilityProfile profile, List<KeyValuePair<GameAction, float>> parts)
        {
            float total = 0f;
            var taken = new List<GameAction>();
            foreach (var part in parts.OrderByDescending(p => p.Value))
            {
                if (taken.Any(t => profile.Exclusive(t, part.Key))) continue;
                taken.Add(part.Key);
                total += part.Value;
            }
            return total;
        }

        /// <summary>
        /// ★ v0.3.14: the damage left after the target's energy resistance, damage reduction and immunities, by the game's
        /// own estimate (UnitPartDamageReduction.EstimateDamage / IsImmune, with its bypass rules). Spell damage ignored
        /// them: Orvath's Fire Blast was valued at 25 against a Dretch (demons resist fire 10) for five rounds running.
        /// Weapon attacks already use the DR helper (HitChanceCalculator.CalculateDamageReduction).
        /// </summary>
        private static float AfterTargetReduction(UnitEntityData target, ContextActionDealDamage dmg, float value)
        {
            if (target == null || value <= 0f || dmg?.DamageType == null) return value;
            try
            {
                var type = dmg.DamageType;
                // ★ v0.3.14 (test5): what this target actually took from this damage type earlier in the fight (immune,
                // resistance, vulnerability), whatever the reason - a component the model does not know, another mod.
                var seen = DamageObserver.Lookup(target, DamageObserver.KeyOf(type));
                if (seen != null) return seen.Apply(value);
                bool energy = type.Type == Kingmaker.RuleSystem.Rules.Damage.DamageType.Energy;
                // ★ test5: the game has two energy-immunity components; only AddEnergyImmunity registers with
                // UnitPartDamageReduction. AddEnergyDamageImmunity is read the way the game's own character sheet reads it
                // (UnitDescriptionHelper.VisitComponents). Orvath cast Fire Blast on Quasits 31 times.
                if (energy && HasEnergyDamageImmunity(target, type.Energy)) return 0f;
                var part = target.Get<Kingmaker.UnitLogic.Parts.UnitPartDamageReduction>();
                if (part == null) return value;
                if (energy && part.IsImmune(type.Energy)) return 0f;
                _ = part.AllSources.Count();                       // initialises the part's resistance chunks
                var damage = type.CreateDamage(new DiceFormula(0, DiceType.Zero), 0);
                return part.EstimateDamage(value, damage, null);
            }
            catch
            {
                return value;
            }
        }

        private static bool HasEnergyDamageImmunity(UnitEntityData target, Kingmaker.Enums.Damage.DamageEnergyType energy)
        {
            bool immune = false;
            try
            {
                Kingmaker.Utility.UnitDescription.UnitDescriptionHelper.VisitComponents<Kingmaker.UnitLogic.FactLogic.AddEnergyDamageImmunity>(
                    target, (c, f) => { if (c.EnergyType == energy) immune = true; });
            }
            catch { }
            return immune;
        }

        public static float EstimateAbilityHeal(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Heals.Count == 0) return 0f;
            if (target != null && profile.Heals.All(h => profile.AppliesTo(h, ability, target) == EffectApplies.No)) return 0f;
            var ctx = CreateContext(ability, target != null ? new TargetWrapper(target) : null);
            int cl = Math.Max(1, ability.CalculateParams()?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            var parts = new List<KeyValuePair<GameAction, float>>();
            foreach (var action in profile.Heals)
            {
                if (target != null && profile.AppliesTo(action, ability, target) == EffectApplies.No) continue;
                if (action is ContextActionHealTarget heal)
                    parts.Add(new KeyValuePair<GameAction, float>(heal, EvaluateDice(heal.Value, ctx, Math.Min(cl, 5), maximize, empower)));
                else if (action is IHealDamageProvider provider && target != null)
                    parts.Add(new KeyValuePair<GameAction, float>(action, PreviewHeal(ability, ctx, provider, target)));
            }
            return SumAlternatives(profile, parts);
        }

        /// <summary>★ v0.3.18 (AA-1): one of the ability's kill actions (ContextActionKill) applies to this target.</summary>
        public static bool KillApplies(AbilityData ability, AbilityProfile profile, UnitEntityData target) =>
            target != null && profile.Damage.Any(a => a is ContextActionKill && profile.AppliesTo(a, ability, target) != EffectApplies.No);

        /// <summary>
        /// ★ v0.3.18 (AA-1): the damage of an action the profile cannot read, as the game previews it for its attack
        /// tooltip (TacticalCombatAttackTooltipVM.CalculateAbilityNumbers): the action builds its damage rule in the
        /// ability's context (IDealDamageProvider.GetDamageRule, not triggered), and the game's damage calculation
        /// (UnitHelper.CalculateAbilityDamage, a fake rule) gives its least and most, after the target's reductions.
        /// The mean of the two. 0 when the action cannot say.
        /// </summary>
        private static float PreviewDamage(AbilityData ability, MechanicsContext ctx, IDealDamageProvider provider, UnitEntityData target)
        {
            if (ctx == null || target == null) return 0f;
            try
            {
                var caster = ability.Caster?.Unit;
                using (ContextData<Kingmaker.UI.Models.Log.GameLogDisabled>.Request())
                using (ctx.GetDataScope(new TargetWrapper(target)))
                {
                    var low = provider.GetDamageRule(caster, target);
                    var high = provider.GetDamageRule(caster, target);
                    if (low == null || high == null) return 0f;
                    int min = UnitHelper.CalculateAbilityDamage(low, Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Minimized);
                    int max = UnitHelper.CalculateAbilityDamage(high, Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Maximized);
                    return Math.Max(0f, (min + max) / 2f);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] damage preview of {ability?.Name} on {target.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>★ v0.3.18 (AA-1): the heal of an action the profile cannot read, as the game previews it (IHealDamageProvider).</summary>
        private static float PreviewHeal(AbilityData ability, MechanicsContext ctx, IHealDamageProvider provider, UnitEntityData target)
        {
            if (ctx == null || target == null) return 0f;
            try
            {
                var caster = ability.Caster?.Unit;
                using (ContextData<Kingmaker.UI.Models.Log.GameLogDisabled>.Request())
                using (ctx.GetDataScope(new TargetWrapper(target)))
                {
                    float total = 0f;
                    foreach (var type in new[] { Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Minimized,
                                                 Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Maximized })
                    {
                        var rule = provider.GetHealRule(caster, target);
                        if (rule == null) return 0f;
                        rule.IsFake = true;
                        rule.CalculationType.Set(type, null);
                        total += Rulebook.Trigger(rule).ValueWithoutReduction;
                    }
                    return Math.Max(0f, total / 2f);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] heal preview of {ability?.Name} on {target.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// ★ v0.3.14: chance <paramref name="caster"/> gets the spell off at all while entangled or under a "casting is
        /// difficult" condition. The game then makes a concentration check before every spell, potions and scrolls
        /// included, wands excepted (UnitUseAbility.MakeConcentrationCheckIfCastingIsDifficult, RuleCheckConcentration:
        /// d20 + concentration against 15 + spell level, twice the level when very difficult). Camellia's Daze (twice) and
        /// Cure Light Wounds and Seelah's potion failed inside Orvath's Entangle. 1 when no check applies.
        /// </summary>
        public static float CastingConcentrationChance(UnitEntityData caster, AbilityData ability)
        {
            try
            {
                if (caster == null || ability?.Blueprint == null || !ability.Blueprint.IsSpell) return 1f;
                if (ability.SourceItem != null && ability.SourceItemUsableBlueprint == null) return 1f;
                if (ability.SourceItemUsableBlueprint != null &&
                    ability.SourceItemUsableBlueprint.Type == Kingmaker.Blueprints.Items.Equipment.UsableItemType.Wand) return 1f;
                var state = caster.Descriptor.State;
                bool veryDifficult = state.HasCondition(UnitCondition.SpellCastingIsVeryDifficult);
                if (!veryDifficult && !state.HasCondition(UnitCondition.Entangled) && !state.HasCondition(UnitCondition.SpellCastingIsDifficult))
                    return 1f;
                var p = ability.CalculateParams();
                int dc = 15 + p.SpellLevel + (veryDifficult ? p.SpellLevel : 0);
                int need = dc - p.Concentration;                       // d20 result needed
                return Mathf.Clamp01((21f - need) / 20f);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// ★ v0.3.7: chance an ability's effect lands on <paramref name="enemy"/>: spell-resistance pass × attack-roll hit
        /// chance × failed save (for damage, a successful save still counts half when the spell does half on a save).
        /// One formula for damage/control scoring and Progressive's viability test, in both combat modes.
        /// </summary>
        public static float EffectChance(UnitEntityData caster, UnitEntityData enemy, AbilityData ability, AbilityProfile profile, bool damage)
        {
            float p = SpellResistancePass(caster, enemy, ability);
            if (profile.NeedsAttackRoll) p *= SpellAttackHitChance(caster, enemy, ability);
            if (profile.Intimidation != null)
            {
                IntimidateOdds(caster, enemy, profile.Intimidation, out float success, out _);
                p *= success;
            }
            if (profile.Save != SavingThrowType.Unknown)
            {
                float fail = SaveFailChance(ability, profile, enemy);
                p *= damage ? fail + (1f - fail) * (profile.HalfOnSave ? 0.5f : 0f) : fail;
            }
            return p;
        }

        /// <summary>
        /// ★ v0.3.18 (AA-4): Demoralize by the game's rule (Demoralize.RunAction): d20 + Intimidate + the action's bonus
        /// against 10 + the target's character level + its Wisdom bonus, the DC adjusted by difficulty like every skill
        /// check (RuleSkillCheck.AdjustDCByDifficulty). On a success the buff lasts 1 + (result - DC) / 5 rounds, one more
        /// with Frightening Thug: <paramref name="rounds"/> is that average over the successful rolls.
        /// </summary>
        public static void IntimidateOdds(UnitEntityData caster, UnitEntityData target, Demoralize action, out float chance, out float rounds)
        {
            chance = 0.5f;
            rounds = 1f;
            if (caster == null || target == null || action == null) return;
            try
            {
                int dc = 10 + target.Descriptor.Progression.CharacterLevel + target.Stats.Wisdom.Bonus;
                dc = RuleSkillCheck.AdjustDCByDifficulty(dc, out _);
                int bonus = caster.Stats.CheckIntimidate.ModifiedValue + action.Bonus;
                int thug = caster.Descriptor.State.Features.FrighteningThug ? 1 : 0;
                int successes = 0;
                float sum = 0f;
                for (int d20 = 1; d20 <= 20; d20++)
                {
                    int result = d20 + bonus;
                    if (result < dc) continue;
                    successes++;
                    sum += 1 + (result - dc) / 5 + thug;
                }
                chance = successes / 20f;
                rounds = successes > 0 ? sum / successes : 1f;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] intimidate {caster.CharacterName}->{target.CharacterName}: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.3.18 (AA-4): a combat maneuver by the game's rule (RuleCombatManeuver): d20 + CMB against the target's CMD
        /// for that maneuver, a natural 1 fails (unless the caster always has a chance), a natural 20 succeeds, a target
        /// immune to maneuvers is never affected. The CMB with the action's stat replacement (casting stat, kineticist main
        /// stat, caster level as base attack: Hydraulic Push). <paramref name="rounds"/>: the effect's average duration over
        /// the successful rolls, (margin / 5 + 1) rounds as ApplyManeuver gives it.
        /// </summary>
        public static void ManeuverOdds(UnitEntityData caster, UnitEntityData target, AbilityData ability, AbilityProfile.ManeuverEffect maneuver,
                                        out float chance, out float rounds)
        {
            chance = 0.5f;
            rounds = 1f;
            if (caster == null || target == null || maneuver == null) return;
            try
            {
                if (target.Descriptor.State.HasCondition(UnitCondition.ImmuneToCombatManeuvers)) { chance = 0f; return; }
                var cmbRule = new RuleCalculateCMB(caster, target, maneuver.Type);
                if (maneuver.Action is ContextActionCombatManeuver action && action.ReplaceStat)
                {
                    var stat = action.NewStat;
                    if (action.UseKineticistMainStat)
                        stat = caster.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>()?.MainStatType ?? stat;
                    if (action.UseCastingStat) stat = ability?.Spellbook?.Blueprint.CastingAttribute ?? StatType.Charisma;
                    if (action.UseCasterLevelAsBaseAttack && ability != null) cmbRule.ReplaceBAB = ability.CalculateParams().CasterLevel;
                    if (action.UseBestMentalStat)
                    {
                        var st = caster.Stats;
                        stat = st.Charisma.ModifiedValue >= st.Intelligence.ModifiedValue && st.Charisma.ModifiedValue >= st.Wisdom.ModifiedValue
                            ? StatType.Charisma
                            : st.Wisdom.ModifiedValue > st.Intelligence.ModifiedValue ? StatType.Wisdom : StatType.Intelligence;
                    }
                    cmbRule.ReplaceStrength = stat;
                }
                int cmb = Rulebook.Trigger(cmbRule).Result;
                int cmd = Rulebook.Trigger(new RuleCalculateCMD(caster, target, maneuver.Type)).Result;
                bool alwaysChance = caster.Descriptor.State.Features.AlwaysChance;
                int successes = 0;
                float sum = 0f;
                for (int d20 = 1; d20 <= 20; d20++)
                {
                    bool success = d20 == 20 || ((d20 != 1 || alwaysChance) && d20 + cmb >= cmd);
                    if (!success) continue;
                    successes++;
                    sum += Math.Max(0, (d20 + cmb - cmd) / 5 + 1);
                }
                chance = successes / 20f;
                rounds = successes > 0 ? sum / successes : 1f;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] maneuver {caster.CharacterName}->{target.CharacterName}: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.3.18 (AA-4): the expected damage Vital Strike adds to one attack (AbilityCustomVitalStrike.VitalStrikePart):
        /// (VitalStrikeMod - 1) more rolls of the weapon's damage dice, and with the mythic feat as many more times the
        /// weapon damage's bonus, added after the critical multiplier - so only the hit chance counts.
        /// </summary>
        public static float VitalStrikeExtra(UnitEntityData unit, WeaponAttackEstimate est,
                                             Kingmaker.UnitLogic.Abilities.Components.AbilityCustomVitalStrike vitalStrike)
        {
            int times = Math.Max(0, (vitalStrike?.VitalStrikeMod ?? 0) - 1);
            if (times == 0 || est?.Weapon == null) return 0f;
            try
            {
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, est.Weapon));
                var weaponDamage = stats.DamageDescription.FirstOrDefault();
                if (weaponDamage == null) return 0f;
                float extra = times * Average(weaponDamage.Dice);
                if (vitalStrike.MythicBlueprint != null && unit.Descriptor.HasFact(vitalStrike.MythicBlueprint))
                    extra += times * Math.Max(0, weaponDamage.Bonus);
                return est.FirstHitChance * extra;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] vital strike {unit.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>★ v0.3.18 (AA-4): how many targets a chain reaches in all (its TargetsCount for this cast).</summary>
        public static int ChainCount(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile?.Chain?.TargetsCount == null) return 1;
            try
            {
                if (profile.Chain.TargetsCount.ValueType == ContextValueType.Simple) return Math.Max(1, profile.Chain.TargetsCount.Value);
                var ctx = CreateContext(ability, new TargetWrapper(target));
                return ctx == null ? 1 : Math.Max(1, profile.Chain.TargetsCount.Calculate(ctx));
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>Probability the target fails its save (1 when the ability has no save).</summary>
        public static float SaveFailChance(AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (profile.Save == SavingThrowType.Unknown || target == null) return 1f;
            try
            {
                int dc = ability.CalculateParams()?.DC ?? 10;
                int bonus;
                switch (profile.Save)
                {
                    case SavingThrowType.Fortitude: bonus = target.Stats.SaveFortitude.ModifiedValue; break;
                    case SavingThrowType.Reflex: bonus = target.Stats.SaveReflex.ModifiedValue; break;
                    case SavingThrowType.Will: bonus = target.Stats.SaveWill.ModifiedValue; break;
                    default: return 1f;
                }
                // ★ v0.3.15: contextual save bonuses the game applied the last time (SaveObserver).
                var seen = SaveObserver.Lookup(target, profile.Save, ability.Blueprint);
                if (seen != null)
                {
                    if (seen.AutoPass) return 0f;
                    bonus += seen.Delta;
                }
                // Fails when d20 + bonus < DC.
                float fail = Mathf.Clamp((dc - bonus - 1) / 20f, 0.05f, 0.95f);
                // ★ v0.3.18 (AA-4): Persistent - a passed save is rolled again (AbilityEffectRunAction,
                // ContextActionSavingThrow): the target resists only by passing both.
                try
                {
                    if (ability.HasMetamagic(Metamagic.Persistent)) fail = 1f - (1f - fail) * (1f - fail);
                }
                catch { }
                return fail;
            }
            catch
            {
                return 0.5f;
            }
        }

        public static float SpellResistancePass(UnitEntityData caster, UnitEntityData target, AbilityData ability)
        {
            if (target == null) return 1f;
            try
            {
                var sr = HitChanceCalculator.CalculateSpellResistance(caster, target, ability);
                return sr.IsImmune ? 0f : Mathf.Clamp01(sr.PenetrationChance);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>
        /// Chance an attack-roll spell (touch / ranged touch / weapon-delivered) hits.
        /// ★ v0.3.18 (C2): by the game's rules, as weapons are: the attack bonus with the delivery's own weapon (the ray or
        /// touch "weapon" of AbilityDeliverProjectile / AbilityDeliverTouch, the bow of an archer's blast) and its stat
        /// replacement (kinetic blasts by Dexterity, Intelligence domains), against the AC for that attack type
        /// (touch, ranged touch). The old estimate was base attack + Strength or Dexterity against a hand-made touch AC,
        /// without the bonuses the game's rule adds (Weapon Focus: Ray, buffs, flanking). It is kept as the fallback.
        /// </summary>
        public static float SpellAttackHitChance(UnitEntityData caster, UnitEntityData target, AbilityData ability)
        {
            try
            {
                if (caster != null && target != null && DeliveryWeapon(caster, ability, out var weapon, out var stat))
                {
                    int ab;
                    using (stat.HasValue ? ContextData<AttackBonusStatReplacement>.Request().Setup(stat.Value) : null)
                        ab = Rulebook.Trigger(new RuleCalculateAttackBonus(caster, target, weapon, 0)).Result;
                    int ac = Rulebook.Trigger(new RuleCalculateAC(caster, target, weapon.Blueprint.AttackType)).Result;
                    return Mathf.Clamp(HitProbability(ab, ac), 0.05f, 0.95f);
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] spell attack roll {ability?.Name}: {ex.Message}");
            }
            try
            {
                return Mathf.Clamp(HitChanceCalculator.CalculateAbilityHitChance(caster, target, ability).HitChance, 0.05f, 0.95f);
            }
            catch
            {
                return 0.6f;
            }
        }

        /// <summary>
        /// ★ v0.3.18 (C2): the target cannot receive <paramref name="buff"/> from <paramref name="ability"/>: the game's
        /// buff immunities (RuleCanApplyBuff) - BuffDescriptorImmunity for the ability's descriptors (unless the caster
        /// has the feature that ignores it, or Mythic's reduce-resistances), and SpecificBuffImmunity for that buff. Read
        /// from the components the way the game decides, without triggering the rule (one of its handlers,
        /// BuffSubstitutionOnApply, applies other buffs). Spell immunity is the spell-resistance check
        /// (HitChanceCalculator.CalculateSpellResistance), condition immunity AbilityClassifier.HasCCImmunity.
        /// </summary>
        public static bool BuffImmune(UnitEntityData target, Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff buff, AbilityData ability)
        {
            if (target == null || buff == null || ability == null) return false;
            try
            {
                var caster = ability.Caster?.Unit;
                if (caster != null && (bool)caster.State.Features.MythicReduceResistances) return false;
                var descriptor = ability.SpellDescriptor;
                foreach (var component in BuffImmunities(target))
                {
                    if (component is Kingmaker.UnitLogic.FactLogic.BuffDescriptorImmunity byDescriptor)
                    {
                        if (!byDescriptor.Descriptor.HasAnyFlag(descriptor)) continue;
                        if (byDescriptor.IgnoreFeature != null && caster != null && caster.Descriptor.HasFact(byDescriptor.IgnoreFeature)) continue;
                        if (byDescriptor.CheckFact && (caster == null || !caster.Descriptor.HasFact(byDescriptor.FactToCheck))) continue;
                        return true;
                    }
                    if (component is Kingmaker.UnitLogic.FactLogic.SpecificBuffImmunity specific && specific.Buff == buff)
                    {
                        bool alignment = caster == null ? specific.Alignment == Kingmaker.Enums.AlignmentComponent.None
                                                        : Kingmaker.Enums.AlignmentExtension.HasComponent(caster.Descriptor.Alignment.ValueRaw, specific.Alignment);
                        bool casterFact = !(HarmonyLib.Traverse.Create(specific).Field("m_CheckCasterFact").GetValue<bool>()) ||
                                          (caster != null && caster.Descriptor.HasFact(
                                              HarmonyLib.Traverse.Create(specific).Field("m_FactToCheck").GetValue<Kingmaker.Blueprints.BlueprintUnitFactReference>()?.Get()));
                        if (alignment && casterFact) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] buff immunity {buff.name} on {target.CharacterName}: {ex.Message}");
            }
            return false;
        }

        private static readonly Dictionary<string, KeyValuePair<int, List<Kingmaker.Blueprints.BlueprintComponent>>> ImmunityComponents =
            new Dictionary<string, KeyValuePair<int, List<Kingmaker.Blueprints.BlueprintComponent>>>();

        /// <summary>The unit's active buff-immunity components, gathered once per frame (a decision asks for every ability and buff).</summary>
        private static List<Kingmaker.Blueprints.BlueprintComponent> BuffImmunities(UnitEntityData unit)
        {
            int frame = Time.frameCount;
            if (ImmunityComponents.TryGetValue(unit.UniqueId, out var cached) && cached.Key == frame) return cached.Value;
            var list = new List<Kingmaker.Blueprints.BlueprintComponent>();
            foreach (var fact in unit.Facts.List)
            {
                var components = fact?.Blueprint?.ComponentsArray;
                if (components == null || !fact.Active) continue;
                foreach (var component in components)
                    if (component is Kingmaker.UnitLogic.FactLogic.BuffDescriptorImmunity ||
                        component is Kingmaker.UnitLogic.FactLogic.SpecificBuffImmunity)
                        list.Add(component);
            }
            if (ImmunityComponents.Count > 256) ImmunityComponents.Clear();
            ImmunityComponents[unit.UniqueId] = new KeyValuePair<int, List<Kingmaker.Blueprints.BlueprintComponent>>(frame, list);
            return list;
        }

        /// <summary>One item of each delivery weapon blueprint, made the way the game makes it for each cast.</summary>
        private static readonly Dictionary<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, ItemEntityWeapon> DeliveryWeapons =
            new Dictionary<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, ItemEntityWeapon>();

        /// <summary>
        /// ★ v0.3.18: the weapon the game makes the spell's attack roll with (AbilityDeliverProjectile.Deliver /
        /// AbilityDeliverTouch.Deliver), and the attack stat it uses instead of the weapon's own when it says so.
        /// </summary>
        private static bool DeliveryWeapon(UnitEntityData caster, AbilityData ability, out ItemEntityWeapon weapon, out StatType? stat)
        {
            weapon = null;
            stat = null;
            var bp = ability?.GetDeliverBlueprint() ?? ability?.Blueprint;
            if (bp == null) return false;
            var projectile = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityDeliverProjectile>(bp);
            if (projectile != null && projectile.NeedAttackRoll && !projectile.IsHandOfTheApprentice &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Line &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Cone)
            {
                if (Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(ability) &&
                    projectile.Type == Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Simple)
                    weapon = caster.Body?.PrimaryHand?.MaybeWeapon;
                else weapon = Made(projectile.Weapon);
                if (projectile.ReplaceAttackRollBonusStat)
                    stat = (bool)caster.State.Features.DomainsByIntelligenceFeature ? StatType.Intelligence : projectile.AttackRollBonusStat;
                return weapon != null;
            }
            var touch = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityDeliverTouch>(bp);
            if (touch?.TouchWeapon != null)
            {
                weapon = Made(touch.TouchWeapon);
                return weapon != null;
            }
            return false;
        }

        private static ItemEntityWeapon Made(Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon blueprint)
        {
            if (blueprint == null) return null;
            if (!DeliveryWeapons.TryGetValue(blueprint, out var item))
                DeliveryWeapons[blueprint] = item = blueprint.CreateEntity<ItemEntityWeapon>();
            return item;
        }

        /// <summary>Use Magic Device success chance for items that need it (1 otherwise).</summary>
        public static float UseMagicDeviceChance(AbilityData ability, UnitEntityData unit)
        {
            try
            {
                int? dc = ability.UseMagicDeviceDC;
                if (!dc.HasValue) return 1f;
                if (!unit.Descriptor.HasUMDSkill) return 0f;
                int skill = unit.Stats.ActualSkillUseMagicDevice.ModifiedValue;
                return Mathf.Clamp((21f + skill - dc.Value) / 20f, 0.05f, 1f);
            }
            catch
            {
                return 1f;
            }
        }

        #endregion

        #region Threat

        /// <summary>
        /// Rough damage-per-round an enemy is worth removing. CR-based so it is stable and cheap; adjusted for enemies
        /// that are already disabled and for enemies currently attacking the party.
        /// </summary>
        /// <summary>★ v0.3.9: cannot act right now (asleep, paralysed, stunned, helpless...).</summary>
        public static bool IsDisabled(UnitEntityData unit)
        {
            var state = unit?.Descriptor?.State;
            if (state == null) return false;
            return state.HasCondition(UnitCondition.Sleeping) || state.HasCondition(UnitCondition.Paralyzed) ||
                   state.HasCondition(UnitCondition.Stunned) || state.HasCondition(UnitCondition.Helpless) ||
                   state.HasCondition(UnitCondition.CantAct) || state.IsUnconscious;
        }

        public static float Threat(UnitEntityData enemy)
        {
            if (enemy == null) return 0f;
            float cr = 1f;
            try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
            float threat = ValueModel.PerRound(cr);   // ★ v0.3.18: the shared worth per round (ValueModel)

            if (IsDisabled(enemy)) threat *= 0.25f;

            try { if (enemy.CombatState.EngagedUnits.Count > 0) threat *= 1.2f; } catch { }
            return threat;
        }

        #endregion
    }
}
