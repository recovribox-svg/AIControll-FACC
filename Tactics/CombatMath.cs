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
        /// <summary>★ v0.3.18 (review): the first attack's attack bonus and the target's AC against it (the rules' values).</summary>
        public int FirstAttackBonus, FirstArmorClass;
        /// <summary>★ v0.3.20 (playtest 1): what the target keeps of extra physical damage on the first weapon (0.5 for a
        /// Tiny swarm against piercing and slashing weapons, 0 when immune, 1 normally).</summary>
        public float FirstPhysicalFactor = 1f;
        /// <summary>★ v0.3.21 (gaps): a hit stops the target's regeneration (its fire dice, alignment, material or magic is
        /// one the regeneration names: AddEffectRegeneration).</summary>
        public bool EndsRegeneration;
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
            return Mathf.Clamp01(HpBeforeDown(unit) / (float)max);
        }

        /// <summary>
        /// ★ v0.3.19 (test6): the hit points before the unit goes down: the game knocks it out when damage + nonlethal
        /// damage reach its hit points (UnitLifeController), so nonlethal damage - a kineticist's burn is burn x
        /// character level of it until rest - comes off what is left. A kineticist with 3 burn at level 10 and no
        /// wound read as 100 % and was 30 HP closer to dropping.
        /// </summary>
        public static float HpBeforeDown(UnitEntityData unit)
        {
            if (unit == null) return 0f;
            int nonLethal = 0;
            try { nonLethal = Math.Max(0, unit.DamageNonLethal); } catch { }
            return Math.Max(0f, unit.HPLeft - nonLethal);
        }

        private static readonly System.Reflection.FieldInfo SizeChangeType =
            HarmonyLib.AccessTools.Field(typeof(Kingmaker.Designers.Mechanics.Buffs.ChangeUnitSize), "m_Type");

        /// <summary>
        /// ★ v0.3.19 (test9): the body size (Corpulence) <paramref name="unit"/> would have with <paramref name="buffs"/>
        /// on it, when one of them makes it bigger (ChangeUnitSize: Enlarge Person, Animal Growth, Righteous Might...), the
        /// way the game scales it (UnitEntityView.Corpulence = base x GetSizeScale(size), at least 0.5); 0 when none grows
        /// it. Read from the buff, so any size-raising buff of any mod counts.
        /// </summary>
        public static float GrownCorpulence(UnitEntityData unit, IEnumerable<Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff> buffs)
        {
            if (unit?.View == null || buffs == null) return 0f;
            try
            {
                var current = unit.Descriptor.State.Size;
                var biggest = current;
                foreach (var buff in buffs)
                {
                    if (buff == null) continue;
                    foreach (var change in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.Designers.Mechanics.Buffs.ChangeUnitSize>(buff))
                    {
                        bool delta = SizeChangeType == null || SizeChangeType.GetValue(change)?.ToString() == "Delta";
                        int size = delta ? (int)unit.Descriptor.OriginalSize + change.SizeDelta : (int)change.Size;
                        size = Mathf.Clamp(size, (int)Kingmaker.Enums.Size.Fine, (int)Kingmaker.Enums.Size.Colossal);
                        if (size > (int)biggest) biggest = (Kingmaker.Enums.Size)size;
                    }
                }
                if (biggest == current) return 0f;
                float now = unit.View.GetSizeScale();
                if (now <= 0.01f) return 0f;
                return Math.Max(0.5f, unit.Corpulence * unit.View.GetSizeScale(biggest) / now);
            }
            catch
            {
                return 0f;
            }
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
            /// <summary>★ v0.3.20 (playtest 1): one hit as the game's damage rule gives it against the target (see
            /// ReadHitOnTarget), and what the target keeps of extra physical damage.</summary>
            public float GameHit, PhysicalFactor = 1f;
            /// <summary>★ v0.3.19 (test5): the blast a kinetic blade casts on each hit (after the target's resistances).</summary>
            public float BladeBlast;
            public int DamageReduction;
            public float ThreatChance;
            public int CritMultiplier;
            public bool EndsRegeneration;
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
                // ★ v0.3.19 (test5): a kinetic blade's blast lands with the hit and is multiplied by its critical
                // (ContextActionDealDamage takes the weapon's critical multiplier from the attack roll).
                // ★ v0.3.20 (playtest 1): the game's damage of a hit on this target; an attack mode's damage change counts
                // past the damage reduction and at what the target keeps of physical damage.
                float modeChange = Math.Max(0f, line.RawPerHit + d.Value - line.DamageReduction) - Math.Max(0f, line.RawPerHit - line.DamageReduction);
                float perHit = Math.Max(0f, line.GameHit + modeChange * line.PhysicalFactor) + line.BladeBlast;
                float critExtra = line.ThreatChance * p * (line.CritMultiplier - 1) * perHit;
                if (line.Index == 0)
                {
                    result.FirstAttackBonus = line.AttackBonus + d.Key;
                    result.FirstArmorClass = line.ArmorClass;
                    result.FirstPerHit = perHit;
                    result.FirstCritChance = line.ThreatChance * p * p;     // threat, then the confirmation roll
                    result.CritMultiplier = Math.Max(1, line.CritMultiplier);
                    result.FirstPhysicalFactor = line.PhysicalFactor;
                }
                if (line.EndsRegeneration) result.EndsRegeneration = true;
                result.ExpectedDamage += p * (perHit + line.Sneak) + critExtra;
            }
            result.AnyHitChance = 1f - noHit;
            return result;
        }

        /// <summary>★ v0.3.18: forget the attack routines read (a new decision, or a state changed for a preview).</summary>
        public static void ClearAttackCache()
        {
            Routines.Clear();
            HitsOnTarget.Clear();   // ★ v0.3.20 (test3): read with the routines, forgotten with them
        }

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
                    int reduction = dr != null && !dr.CanBypass && dr.DRValue > 0 ? dr.DRValue : 0;
                    // ★ v0.3.20 (playtest 1): the target's side of the damage by the game's own rule. Bows against a
                    // Rat Swarm were valued at twice what they deal (SwarmDamageResistance halves piercing and slashing
                    // weapon damage, kinetic blasts excepted), so a full attack beat Ember's Fire Blast and Camellia's
                    // Eldritch Blast; critical hits and sneak attacks were counted on targets immune to them.
                    var hit = ReadHitOnTarget(unit, target, weapon, stats);
                    routine.Lines.Add(new AttackLine
                    {
                        Weapon = weapon,
                        Index = i,
                        AttackBonus = ab,
                        ArmorClass = acRule.Result,
                        RawPerHit = perHit,
                        GameHit = hit.Read ? hit.Damage : Math.Max(0f, perHit - reduction),
                        PhysicalFactor = hit.Read ? hit.PhysicalFactor : 1f,
                        BladeBlast = BladeBlastPerHit(unit, target, weapon),
                        DamageReduction = reduction,
                        ThreatChance = hit.CritImmune ? 0f : Mathf.Clamp01(stats.CriticalRange / 20f),
                        CritMultiplier = stats.CriticalMultiplier,
                        EndsRegeneration = Regenerates(target) &&
                                           EndsRegenerationOf(target, stats.DamageDescription.Select(x => x.TypeDescription),
                                                              stats.EnhancementTotal > 0 || weapon.EnchantmentValue > 0, WeaponAlignments(weapon)),
                        Sneak = sneakDice > 0 && (flanked || acRule.IsTargetFlatFooted) && !ImmuneToPrecision(target)
                            ? 3.5f * sneakDice * (hit.Read ? hit.PhysicalFactor : 1f) : 0f
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

        /// <summary>★ v0.3.20 (playtest 1): one weapon hit on a target as the game's damage rule gives it.</summary>
        private sealed class HitOnTarget
        {
            public int Frame;
            public bool Read;
            public float Damage;
            public float PhysicalFactor = 1f;
            public bool CritImmune;
        }

        private static readonly Dictionary<string, HitOnTarget> HitsOnTarget = new Dictionary<string, HitOnTarget>();

        /// <summary>
        /// ★ v0.3.20 (playtest 1): the damage of one hit of <paramref name="weapon"/> on <paramref name="target"/>, before a
        /// critical, by the game's own damage calculation - the way the game estimates a weapon's damage itself
        /// (UnitHelper.EstimateAttackDamage / CalculateAbilityDamage: a fake RuleCalculateDamage of the weapon's damage,
        /// nothing dealt, no log). It runs every handler on the target: damage reduction, energy resistance on a weapon's
        /// energy dice, a swarm's resistance to weapons (SwarmDamageResistance), vulnerabilities, the difficulty setting's
        /// decline. Also read from it: critical-hit immunity (CritImmunity unless IgnoreCritImmunity, as the damage rule
        /// applies it) and what the target keeps of physical damage. Mean of the least and the most. Once a frame.
        /// </summary>
        private static HitOnTarget ReadHitOnTarget(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon, RuleCalculateWeaponStats stats)
        {
            int frame = Time.frameCount;
            string key = $"{unit.UniqueId}|{target.UniqueId}|{weapon.UniqueId}";
            if (HitsOnTarget.TryGetValue(key, out var cached) && cached.Frame == frame) return cached;
            if (HitsOnTarget.Count > 512) HitsOnTarget.Clear();
            var hit = new HitOnTarget { Frame = frame };
            try
            {
                using (ContextData<Kingmaker.UI.Models.Log.GameLogDisabled>.Request())
                {
                    int? least = FakeWeaponDamage(unit, target, weapon, stats, Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Minimized, out _);
                    int? most = FakeWeaponDamage(unit, target, weapon, stats, Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType.Maximized, out var rule);
                    if (least.HasValue && most.HasValue && rule != null)
                    {
                        hit.Damage = (least.Value + most.Value) / 2f;
                        hit.CritImmune = rule.CritImmunity && !rule.IgnoreCritImmunity;
                        var physical = rule.CalculatedDamage.Select(v => v.Source)
                            .FirstOrDefault(d => d is Kingmaker.RuleSystem.Rules.Damage.PhysicalDamage);
                        if (physical != null)
                            hit.PhysicalFactor = physical.Immune ? 0f : DeclineFactor(physical.Decline.Value) * physical.Vulnerability;
                        hit.Read = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] hit on target {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
            }
            HitsOnTarget[key] = hit;
            return hit;
        }

        /// <summary>★ v0.3.20: the weapon's damage bundle (as UnitHelper.ApplyDamage builds it) through a fake damage rule.</summary>
        private static int? FakeWeaponDamage(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon, RuleCalculateWeaponStats stats,
                                             Kingmaker.RuleSystem.Rules.Damage.DamageCalculationType calculation,
                                             out Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage rule)
        {
            rule = null;
            Kingmaker.RuleSystem.Rules.Damage.DamageBundle bundle = null;
            foreach (var description in stats.DamageDescription)
            {
                var damage = description.CreateDamage();
                damage.CalculationType.Set(calculation, null);
                if (bundle == null) bundle = new Kingmaker.RuleSystem.Rules.Damage.DamageBundle(weapon, damage);
                else bundle.Add(damage);
            }
            if (bundle == null) return null;
            var deal = new Kingmaker.RuleSystem.Rules.Damage.RuleDealDamage(unit, target, bundle) { IsFake = true, DisableBattleLogSelf = true };
            rule = Rulebook.Trigger(new Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage(deal));
            return rule.CalculatedDamage.Sum(v => v.FinalValue);
        }

        /// <summary>★ v0.3.20: share of a damage roll a decline leaves (RuleCalculateDamage.CalculateDamageValue).</summary>
        public static float DeclineFactor(Kingmaker.RuleSystem.Rules.Damage.DamageDeclineType decline)
        {
            switch (decline)
            {
                case Kingmaker.RuleSystem.Rules.Damage.DamageDeclineType.None: return 1f;
                case Kingmaker.RuleSystem.Rules.Damage.DamageDeclineType.ByQuarter: return 0.75f;
                case Kingmaker.RuleSystem.Rules.Damage.DamageDeclineType.ByHalf: return 0.5f;
                default: return 0f;
            }
        }

        /// <summary>
        /// ★ v0.3.19 (test5): the damage of the blast a kinetic blade casts on a hit (KineticistController
        /// .TryHandleKineticBladeAttack: RuleCastSpell of WeaponKineticBlade.GetBlastAbility with the hit's attack roll).
        /// The blade itself deals little: without it a Kinetic Knight's attacks were worth a fraction of what they do.
        /// </summary>
        private static float BladeBlastPerHit(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon)
        {
            try
            {
                var blade = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.WeaponKineticBlade>(weapon.Blueprint);
                var blast = blade?.GetBlastAbility(unit.Descriptor);
                if (blast == null) return 0f;
                // ★ v0.3.19 (test6): the game activates the blade before the attack (a free action, its infusion's burn:
                // KineticBlade<Element>BurnAbility); with less burn left this round the blade cannot activate
                // (RestrictionCanUseKineticBlade) and the hit casts nothing.
                var part = unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>();
                if (part != null && !part.IsBladeActivated &&
                    (Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.CalculateAbilityBurnCost(blade.GetActivationAbility(unit.Descriptor))?.Total ?? 0) > part.LeftBurnThisRound)
                    return 0f;
                var profile = AbilityProfile.Get(blast);
                if (profile == null) return 0f;
                return EstimateAbilityDamage(blast, profile, target, weapon) * SpellResistancePass(unit, target, blast);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] kinetic blade blast {unit.CharacterName}->{target.CharacterName}: {ex.Message}");
                return 0f;
            }
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
                bool immune = AbilityClassifier.IsUndeadOrConstruct(target);
                damage = perHit * (immune ? 1 : Math.Max(2, stats.CriticalMultiplier));
                int sneakDice = SafeStat(unit.Stats.SneakAttack);       // a helpless target is denied its Dex bonus
                if (sneakDice > 0 && !immune) damage += 3.5f * sneakDice;
                // ★ v0.3.18 (test11): damage reduction comes off the hit's total once, after the critical multiplier
                // and with the sneak attack in it (it was taken off the unmultiplied hit before).
                var dr = HitChanceCalculator.CalculateDamageReduction(unit, target, weapon, damage);
                if (dr != null && !dr.CanBypass && dr.DRValue > 0)
                    damage = Math.Max(0f, damage - dr.DRValue);

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
            // ★ v0.3.18 (review): no dice (DiceType.Zero: 0 sides) adds nothing; it added count / 2.
            float dice = sides <= 0 ? 0f : maximize ? count * sides : count * (sides + 1) / 2f;
            float total = dice + bonus;
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

        internal static int EvaluateValue(ContextValue value, MechanicsContext ctx, int fallback)
        {
            if (value == null) return 0;
            if (value.ValueType == ContextValueType.Simple) return value.Value;
            // ★ v0.3.18 (review): a shared value is 0 in a context nobody cast (it is calculated during the cast): its
            // average from the ability's own ContextCalculateSharedValue.
            if (value.ValueType == ContextValueType.Shared && ctx is Kingmaker.UnitLogic.Abilities.AbilityExecutionContext shared)
            {
                AbilityProfile.SharedValueRange(shared.Ability, ctx, value.ValueShared, out _, out int avg, out _);
                if (avg > 0) return avg;
            }
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

        public static float EstimateAbilityDamage(AbilityData ability, AbilityProfile profile, UnitEntityData target) =>
            EstimateAbilityDamage(ability, profile, target, null);

        /// <param name="bladeWeapon">★ v0.3.19 (test5): the kinetic blade whose hit casts this blast (its attack roll is the
        /// blade's: the blast's weapon damage modifiers are the blade's).</param>
        public static float EstimateAbilityDamage(AbilityData ability, AbilityProfile profile, UnitEntityData target, ItemEntityWeapon bladeWeapon)
        {
            float raw = EstimateAbilityDamageRaw(ability, profile, target, bladeWeapon);
            // ★ v0.3.20 (0.3.19 feedback): a kinetic blast's estimate times what its hits really dealt so far.
            return raw > 0f ? raw * DamageObserver.BlastFactor(ability?.Caster?.Unit, ability?.Blueprint) : raw;
        }

        /// <summary>★ v0.3.20: the estimate before the learned share of a blast (DamageObserver checks against it).</summary>
        internal static float EstimateAbilityDamageRaw(AbilityData ability, AbilityProfile profile, UnitEntityData target, ItemEntityWeapon bladeWeapon)
        {
            if (profile.Damage.Count == 0) return 0f;
            if (target != null && profile.Damage.All(d => profile.AppliesTo(d, ability, target) == EffectApplies.No)) return 0f;
            var ctx = CreateContext(ability, target != null ? new TargetWrapper(target) : null);
            int cl = Math.Max(1, ability.CalculateParams()?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            var parts = new List<KeyValuePair<GameAction, float>>();
            // ★ v0.3.20 (playtest 1): a swarm takes half again from area damage (SwarmAoeVulnerability).
            float swarmArea = SwarmAreaFactor(ability, target, false);
            foreach (var action in profile.Damage)
            {
                // ★ v0.3.1: only the branch that runs for this target (Inflict Wounds heals undead, Cure harms them).
                if (target != null && profile.AppliesTo(action, ability, target) == EffectApplies.No) continue;
                switch (action)
                {
                    case ContextActionDealDamage dmg:
                        float avg;
                        // ★ v0.3.18 (review): damage rolled earlier into a shared value (ContextActionDealDamage
                        // ReadPreRolledFromSharedValue) is that value's average, not the action's own (unused) dice.
                        if (dmg.ReadPreRolledFromSharedValue && ctx is Kingmaker.UnitLogic.Abilities.AbilityExecutionContext rolled)
                        {
                            AbilityProfile.SharedValueRange(rolled.Ability, ctx, dmg.PreRolledSharedValue, out _, out int pre, out _);
                            avg = pre;
                        }
                        else avg = EvaluateDice(dmg.Value, ctx, Math.Min(cl, 10), maximize, empower);
                        avg += KineticDamageBonus(ability, dmg, profile.NeedsAttackRoll || bladeWeapon != null, bladeWeapon);
                        if (dmg.Half) avg *= 0.5f;
                        avg *= ArcherBlastDecline(ability, dmg, target);
                        parts.Add(new KeyValuePair<GameAction, float>(dmg, AfterTargetReduction(target, dmg, avg) * swarmArea));
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
        /// ★ v0.3.19 (test5): what the game adds to a kinetic blast's damage that its dice do not show. Kinetic blasts were
        /// valued below a plain weapon attack ("kinetic shooter just does standard attack instead of a blast"):
        /// <list type="bullet">
        /// <item>Elemental Overflow (AddKineticistElementalOverflow, on RuleDealDamage of the kineticist's own blasts,
        /// UnitPartKineticist.Blasts): min(Bonus, class level / 3), limited by the burn accepted unless IgnoreBurn, times
        /// 2 (3 for a fire blast with Fire's Fury, +2 with Elemental Engine at full burn). Read from the component's own
        /// fields: the game adds it inside a damage rule the estimate does not run.</item>
        /// <item>The weapon's damage modifiers (ContextActionDealDamage.GetDamageInfo): a kinetic archer's blast shot
        /// through the bow adds the bow's (RuleCalculateWeaponStats.DamageModifiers) and its enhancement; a blast with
        /// UseWeaponDamageModifiers cast by a kinetic blade's hit adds the blade's. Only with an attack roll, as in the
        /// game (the modifiers come from the roll's weapon stats).</item>
        /// </list>
        /// 0 for everything that is not a kineticist's.
        /// </summary>
        private static float KineticDamageBonus(AbilityData ability, ContextActionDealDamage dmg, bool attackRoll, ItemEntityWeapon bladeWeapon)
        {
            var caster = ability?.Caster?.Unit;
            var part = caster?.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>();
            if (part == null) return 0f;
            float bonus = 0f;
            try
            {
                bool archer = Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(ability);
                var weapon = archer ? caster.Body?.PrimaryHand?.MaybeWeapon : bladeWeapon;
                if (attackRoll && weapon != null && (archer || dmg.UseWeaponDamageModifiers))
                    bonus += FrameCached($"wpn|{caster.UniqueId}|{weapon.UniqueId}|{archer}", () =>
                    {
                        var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(caster, weapon));
                        return stats.DamageModifiers.TotalValue + (archer ? stats.Enhancement.TotalValue : 0);
                    });

                bonus += ElementalOverflowDamage(caster, ability.Blueprint);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] kinetic damage bonus of {ability.Name}: {ex.Message}");
            }
            return bonus;
        }

        /// <summary>
        /// ★ v0.3.20 (playtest 1): what <paramref name="target"/> keeps of a kinetic archer's physical blast. The blast is shot
        /// through the bow, which is the damage's weapon, so a swarm declines it as weapon damage (SwarmDamageResistance
        /// exempts only kinetic-blast weapons: an earth blast keeps all of it, being bludgeoning; a metal blast against a
        /// Tiny swarm half). Read from the game's damage rule with the bow and the blast's damage type, as the weapon
        /// estimate reads a hit (ReadHitOnTarget); only the decline, the rest is the blast estimate's. 1 otherwise.
        /// </summary>
        private static float ArcherBlastDecline(AbilityData ability, ContextActionDealDamage dmg, UnitEntityData target)
        {
            if (target == null || dmg?.DamageType == null || dmg.DamageType.Type != Kingmaker.RuleSystem.Rules.Damage.DamageType.Physical) return 1f;
            var caster = ability?.Caster?.Unit;
            try
            {
                if (caster == null || !Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(ability)) return 1f;
                var bow = caster.Body?.PrimaryHand?.MaybeWeapon;
                if (bow == null) return 1f;
                var form = dmg.DamageType.Physical?.Form;
                return FrameCached($"archer|{caster.UniqueId}|{target.UniqueId}|{bow.UniqueId}|{form}", () =>
                {
                    var damage = dmg.DamageType.CreateDamage(new DiceFormula(1, DiceType.D6), 0);
                    var deal = new Kingmaker.RuleSystem.Rules.Damage.RuleDealDamage(caster, target,
                        new Kingmaker.RuleSystem.Rules.Damage.DamageBundle(bow, damage)) { IsFake = true, DisableBattleLogSelf = true };
                    Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage rule;
                    using (ContextData<Kingmaker.UI.Models.Log.GameLogDisabled>.Request())
                        rule = Rulebook.Trigger(new Kingmaker.RuleSystem.Rules.Damage.RuleCalculateDamage(deal));
                    var source = rule.CalculatedDamage.Count > 0 ? rule.CalculatedDamage[0].Source : null;
                    float factor = source == null ? 1f : source.Immune ? 0f : DeclineFactor(source.Decline.Value);
                    return (int)Math.Round(factor * 1000f);
                }) / 1000f;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] archer blast decline of {ability?.Name}: {ex.Message}");
                return 1f;
            }
        }

        private static readonly Dictionary<string, KeyValuePair<int, int>> FrameValues = new Dictionary<string, KeyValuePair<int, int>>();

        /// <summary>★ v0.3.19 (test5): a value read once per frame (a decision asks for every ability and enemy).</summary>
        private static int FrameCached(string key, Func<int> read)
        {
            int frame = Time.frameCount;
            if (FrameValues.TryGetValue(key, out var cached) && cached.Key == frame) return cached.Value;
            if (FrameValues.Count > 256) FrameValues.Clear();
            int value = read();
            FrameValues[key] = new KeyValuePair<int, int>(frame, value);
            return value;
        }

        private static System.Reflection.MethodInfo _willGatherPower;
        private static bool _willGatherPowerSought;

        /// <summary>
        /// ★ v0.3.19 (test5): how many times the game gathers power before <paramref name="ability"/> - the Gather Power
        /// it puts in front of the blast (KineticistController.TryRunGatherPowerAction: ExecuteBefore) when the gather
        /// mode is set and the blast costs burn the gathered power would lower. The game's own rule
        /// (KineticistController.WillGatherPower, private: called by reflection); in turn-based combat each gather
        /// takes the Move action, then the Standard (TurnController.CalculateKineticistPrediction). 0 when it cannot say.
        /// </summary>
        public static int GatherPowerCount(UnitEntityData unit, AbilityData ability)
        {
            var part = unit?.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>();
            if (part == null || ability == null || part.GatherPowerAbility == null) return 0;
            try
            {
                if (!_willGatherPowerSought)
                {
                    _willGatherPowerSought = true;
                    _willGatherPower = HarmonyLib.AccessTools.Method(typeof(Kingmaker.UnitLogic.Class.Kineticist.KineticistController), "WillGatherPower",
                        new[] { typeof(Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist), typeof(AbilityData),
                                typeof(int).MakeByRefType(), typeof(int).MakeByRefType() });
                    if (_willGatherPower == null) Main.Log("[Math] KineticistController.WillGatherPower not found: Gather Power not foreseen");
                }
                if (_willGatherPower == null) return 0;
                var args = new object[] { part, ability, 0, 0 };
                return (bool)_willGatherPower.Invoke(null, args) ? (int)args[3] : 0;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] gather power of {ability.Name}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>★ v0.3.19 (test5): Elemental Overflow's damage on one damage roll of <paramref name="ability"/> (see
        /// KineticDamageBonus); 0 when it is not one of the caster's own blasts.</summary>
        /// <param name="extraBurn">★ v0.3.19 (test6): with this much more burn accepted (what accepting it would add).</param>
        public static int ElementalOverflowDamage(UnitEntityData caster, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility ability,
                                                  int extraBurn = 0)
        {
            var part = caster?.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>();
            var blast = ability?.Parent ?? ability;
            if (part?.Blasts == null || blast == null || !part.Blasts.Contains(blast)) return 0;
            return FrameCached($"ovf|{caster.UniqueId}|{blast.AssetGuid}|{extraBurn}", () => OverflowNow(caster, part, blast, extraBurn));
        }

        private static int OverflowNow(UnitEntityData caster, Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist part,
                                       Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility blast, int extraBurn)
        {
            int accepted = Math.Min(part.MaxBurn, part.AcceptedBurn + Math.Max(0, extraBurn));
            int bonus = 0;
            foreach (var fact in caster.Facts.List)
            {
                if (fact?.Blueprint?.ComponentsArray == null || !fact.Active) continue;
                foreach (var component in fact.Blueprint.ComponentsArray)
                {
                    if (!(component is Kingmaker.UnitLogic.Class.Kineticist.AddKineticistElementalOverflow overflow)) continue;
                    int ranks = Math.Min(overflow.Bonus?.Calculate(fact.MaybeContext) ?? 0, part.ClassLevel / 3);
                    bool engine = overflow.ElementalEngine && part.MaxBurn == accepted;
                    int per = (blast.SpellDescriptor & Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.Fire) != 0 &&
                              overflow.FiresFury != null && caster.Descriptor.HasFact(overflow.FiresFury) ? 3 : 2;
                    if (engine) per += 2;
                    if (!overflow.IgnoreBurn) ranks = Math.Min(ranks, accepted);
                    bonus += Math.Max(0, ranks) * per;
                }
            }
            return bonus;
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

        /// <summary>
        /// ★ v0.3.20 (playtest 1): 1.5 when <paramref name="target"/> is a swarm (SwarmAoeVulnerability in its facts) and the
        /// damage comes from an area the way the game tests it - a projectile that is not a single one (line, cone...),
        /// an ability that targets around a point (AbilityTargetsAround), or the map itself (<paramref name="fromArea"/>: the
        /// damage's context belongs to a BlueprintArea, a location hazard; a spell's own area effect does not count);
        /// 1 otherwise.
        /// </summary>
        public static float SwarmAreaFactor(AbilityData ability, UnitEntityData target, bool fromArea)
        {
            if (target == null) return 1f;
            try
            {
                bool swarm = FrameCached($"swarmaoe|{target.UniqueId}", () =>
                {
                    bool found = false;
                    Kingmaker.Utility.UnitDescription.UnitDescriptionHelper.VisitComponents<Kingmaker.UnitLogic.FactLogic.SwarmAoeVulnerability>(
                        target, (c, f) => found = true);
                    return found ? 1 : 0;
                }) == 1;
                if (!swarm) return 1f;
                if (fromArea) return 1.5f;
                if (ability == null) return 1f;
                var projectile = ability.GetDeliverProjectileFromDeliverBlueprint(Kingmaker.Armies.TacticalCombat.TacticalCombatHelper.IsActive);
                bool area = (projectile != null && projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Simple) ||
                            Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityTargetsAround>(ability.Blueprint) != null;
                return area ? 1.5f : 1f;
            }
            catch
            {
                return 1f;
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
                    // ★ v0.3.18 (review): read from the rule the action builds, not triggered - a fake RuleHealDamage still
                    // runs handlers that ignore IsFake (RemovedByHeal removes its buff, SufferFromHealing deals real
                    // damage, heal triggers run actions). Its average dice and bonus, before the target's modifiers.
                    var rule = provider.GetHealRule(caster, target);
                    if (rule == null) return 0f;
                    return Math.Max(0f, Average(rule.HealFormula.ModifiedValue) + rule.Bonus);
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
            float p = SpellResistancePass(caster, enemy, ability) * ShadowShare(ability, enemy);
            if (profile.NeedsAttackRoll) p *= SpellAttackHitChance(caster, enemy, ability);
            if (profile.Save != SavingThrowType.Unknown)
            {
                float fail = SaveFailChance(ability, profile, enemy);
                if (!damage) p *= fail;
                else if (!profile.HalfOnSave) p *= fail;
                else
                {
                    // ★ v0.3.18 (review): Evasion takes nothing on a passed save, Improved Evasion half on a failed one
                    // (ContextActionDealDamage with the save's Evasion / ImprovedEvasion, set by the target's facts).
                    EvasionOf(enemy, profile.Save, out bool evasion, out bool improved);
                    p *= fail * (improved ? 0.5f : 1f) + (1f - fail) * (evasion ? 0f : 0.5f);
                }
            }
            return p;
        }

        /// <summary>
        /// ★ v0.3.18 (AA-4): Demoralize by the game's rule (Demoralize.RunAction): d20 + Intimidate + the action's bonus
        /// against 10 + the target's character level + its Wisdom bonus, the DC adjusted by difficulty like every skill
        /// check (RuleSkillCheck.AdjustDCByDifficulty). On a success the buff lasts 1 + (result - DC) / 5 rounds, one more
        /// with Frightening Thug: <paramref name="rounds"/> is that average over the successful rolls.
        /// </summary>
        public static void IntimidateOdds(UnitEntityData caster, UnitEntityData target, Demoralize action, out float chance, out float rounds) =>
            IntimidateOdds(caster, target, action, out chance, out rounds, out _);

        /// <remarks>★ v0.3.18 (test10): <paramref name="fourPlus"/> - the share of the successful rolls whose duration is 4
        /// rounds or more (Frightening Thug then adds the greater buff for a round). Swordlord's Dazzling Display adds +1
        /// per Dueling Sword feature to the check, as the game does for that action.</remarks>
        public static void IntimidateOdds(UnitEntityData caster, UnitEntityData target, Demoralize action, out float chance, out float rounds,
                                          out float fourPlus)
        {
            chance = 0.5f;
            rounds = 1f;
            fourPlus = 0f;
            if (caster == null || target == null || action == null) return;
            try
            {
                // ★ v0.3.18 (review): success against the difficulty-adjusted DC, duration from the DC before it.
                int rawDc = 10 + target.Descriptor.Progression.CharacterLevel + target.Stats.Wisdom.Bonus;
                int dc = RuleSkillCheck.AdjustDCByDifficulty(rawDc, out _);
                int bonus = caster.Stats.CheckIntimidate.ModifiedValue + action.Bonus;
                if (action.DazzlingDisplay && caster.Descriptor.State.Features.SwordlordWeaponProwess)
                    bonus += caster.Descriptor.Progression.Features.Enumerable
                                   .Count(f => f.Param?.WeaponCategory == Kingmaker.Enums.WeaponCategory.DuelingSword);
                int thug = caster.Descriptor.State.Features.FrighteningThug ? 1 : 0;
                int successes = 0, long4 = 0;
                float sum = 0f;
                for (int d20 = 1; d20 <= 20; d20++)
                {
                    int result = d20 + bonus;
                    if (result < dc) continue;
                    successes++;
                    int duration = 1 + (result - rawDc) / 5 + thug;
                    sum += duration;
                    if (duration >= 4) long4++;
                }
                chance = successes / 20f;
                rounds = successes > 0 ? sum / successes : 1f;
                fourPlus = successes > 0 ? (float)long4 / successes : 0f;
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
                // ★ v0.3.18 (review): as RuleCombatManeuver does, the attack bonuses that are not a stat (flanking, the
                // difficulty bonus, what handlers add to the attack rule) of an empty-hand attack go into the CMB.
                var hand = caster.Body?.EmptyHandWeapon;
                if (hand != null)
                {
                    var attackRule = Rulebook.Trigger(new RuleCalculateAttackBonus(caster, target, hand, 0));
                    foreach (var bonus in attackRule.AllBonuses)
                        if (bonus.Stat == StatType.Unknown) cmbRule.AddModifierCopy(bonus);
                }
                int cmb = Rulebook.Trigger(cmbRule).Result;
                // ★ v0.3.18 (review): an enemy that strikes back at this maneuver does so from inside the CMD rule
                // (CounterAttackOfOpportunityOnCombatManeuver forces a real attack of opportunity): its CMD is asked for no
                // particular maneuver then, losing only its bonuses against this one.
                var cmdType = CountersManeuver(target, maneuver.Type) ? CombatManeuver.None : maneuver.Type;
                int cmd = Rulebook.Trigger(new RuleCalculateCMD(caster, target, cmdType)).Result;
                bool alwaysChance = caster.Descriptor.State.Features.AlwaysChance;
                int successes = 0;
                float sum = 0f;
                for (int d20 = 1; d20 <= 20; d20++)
                {
                    bool success = d20 == 20 || ((d20 != 1 || alwaysChance) && d20 + cmb >= cmd);
                    // An overrun by less than 5 is a partial success: the target is not knocked down.
                    if (maneuver.Type == CombatManeuver.Overrun && d20 + cmb - cmd < 5) success = false;
                    if (!success) continue;
                    successes++;
                    sum += Math.Max(0, (d20 + cmb - cmd) / 5 + 1);
                }
                chance = successes / 20f;
                rounds = successes > 0 ? sum / successes : 1f;
                // ★ v0.3.18 (review): concealment is checked first unless the action ignores it (RuleCombatManeuver).
                if (!(maneuver.Action is ContextActionCombatManeuver cm && cm.IgnoreConcealment))
                {
                    var concealment = Kingmaker.UnitLogic.Parts.UnitPartConcealment.Calculate(caster, target);
                    if (concealment == Kingmaker.Enums.Concealment.Total) chance *= 0.5f;
                    else if (concealment == Kingmaker.Enums.Concealment.Partial) chance *= 0.8f;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] maneuver {caster.CharacterName}->{target.CharacterName}: {ex.Message}");
            }
        }

        private static readonly Dictionary<string, bool> TripTriggers = new Dictionary<string, bool>();

        /// <summary>
        /// ★ v0.3.20 (test5): the chance an attack of opportunity by <paramref name="enemy"/> that hits also knocks
        /// <paramref name="unit"/> down: a weapon trigger on a hit (its facts or its weapon's enchantments, as wolves'
        /// bites) that trips (ContextActionCombatManeuver Trip by CMB against the unit's CMD) or knocks down. Only then
        /// does a hit stop a walk: the game lets a moving unit walk on after an attack of opportunity (UnitAttackOfOpportunity
        /// does nothing to its target's move; UnitMoveController interrupts a walk only when the unit can no longer move).
        /// 0 when it has no such trigger or the unit cannot be knocked down.
        /// </summary>
        public static float TripOnHitChance(UnitEntityData enemy, UnitEntityData unit)
        {
            if (enemy == null || unit == null) return 0f;
            try
            {
                if (!unit.CanBeKnockedOff()) return 0f;
                var weapon = enemy.GetThreatHand()?.Weapon;
                string key = $"{enemy.UniqueId}|{enemy.Facts.List.Count}|{weapon?.UniqueId}";
                if (!TripTriggers.TryGetValue(key, out bool trips))
                {
                    var components = enemy.Facts.List.Where(f => f != null && f.Active && f.Blueprint?.ComponentsArray != null)
                                          .SelectMany(f => f.Blueprint.ComponentsArray);
                    if (weapon != null)
                        components = components.Concat(weapon.Enchantments.Where(e => e?.Blueprint?.ComponentsArray != null)
                                                             .SelectMany(e => e.Blueprint.ComponentsArray));
                    trips = components.OfType<Kingmaker.UnitLogic.Mechanics.Components.AbstractWeaponTrigger>().Any(t =>
                        !t.ActionsOnInitiator && !t.OnMiss && !t.OnlyOnFullAttack && !t.OnlyNatural20 && !t.CriticalHit && !t.OnCharge &&
                        AbilityProfile.ContainsAction(t.Action?.Actions, a =>
                            (a is ContextActionCombatManeuver m && m.Type == CombatManeuver.Trip) || a is ContextActionKnockdownTarget));
                    if (TripTriggers.Count > 256) TripTriggers.Clear();
                    TripTriggers[key] = trips;
                }
                if (!trips) return 0f;
                if (unit.Descriptor.State.HasCondition(UnitCondition.ImmuneToCombatManeuvers)) return 0f;
                int margin = FrameCached($"trip|{enemy.UniqueId}|{unit.UniqueId}", () =>
                    Rulebook.Trigger(new RuleCalculateCMB(enemy, unit, CombatManeuver.Trip)).Result -
                    Rulebook.Trigger(new RuleCalculateCMD(enemy, unit, CombatManeuver.Trip)).Result);
                // d20 + CMB >= CMD; a natural 20 always succeeds, a natural 1 always fails.
                return Mathf.Clamp((21 + margin) / 20f, 0.05f, 0.95f);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] trip on hit {enemy.CharacterName}->{unit.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>★ v0.3.18 (review): <paramref name="target"/> has a fact that answers this maneuver with an attack of
        /// opportunity (read from its components; the type is a private field).</summary>
        private static bool CountersManeuver(UnitEntityData target, CombatManeuver type)
        {
            try
            {
                foreach (var fact in target.Facts.List)
                {
                    var components = fact?.Blueprint?.ComponentsArray;
                    if (components == null) continue;
                    foreach (var c in components)
                        if (c is Kingmaker.Designers.Mechanics.Facts.CounterAttackOfOpportunityOnCombatManeuver counter &&
                            HarmonyLib.Traverse.Create(counter).Field("m_ManeuverType").GetValue<CombatManeuver>() == type)
                            return true;
                }
            }
            catch
            {
                return true;   // unreadable: ask without the type, never risk the real attack
            }
            return false;
        }

        /// <summary>
        /// ★ v0.3.18 (AA-4): the expected damage Vital Strike adds to one attack (AbilityCustomVitalStrike.VitalStrikePart):
        /// (VitalStrikeMod - 1) more rolls of the weapon's damage dice, and with the mythic feat as many more times the
        /// weapon damage's bonus, added after the critical multiplier - so only the hit chance counts.
        /// </summary>
        public static float VitalStrikeExtra(UnitEntityData unit, UnitEntityData target, WeaponAttackEstimate est,
                                             Kingmaker.UnitLogic.Abilities.Components.AbilityCustomVitalStrike vitalStrike)
        {
            int times = Math.Max(0, (vitalStrike?.VitalStrikeMod ?? 0) - 1);
            if (times == 0 || est?.Weapon == null) return 0f;
            try
            {
                // ★ v0.3.18 (review): the game's Vital Strike attacks with the first weapon and adds dice to physical
                // weapon damage only.
                var weapon = unit.GetFirstWeapon() ?? est.Weapon;
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                var weaponDamage = stats.DamageDescription.FirstOrDefault();
                if (weaponDamage == null || weaponDamage.TypeDescription.Type != Kingmaker.RuleSystem.Rules.Damage.DamageType.Physical) return 0f;
                float extra = times * Average(weaponDamage.Dice);
                if (vitalStrike.MythicBlueprint != null && unit.Descriptor.HasFact(vitalStrike.MythicBlueprint))
                    extra += times * Math.Max(0, weaponDamage.Bonus);
                // ★ v0.3.18 (test10): Rowdy - (2 x sneak attack dice)d6 precision damage on the Vital Strike
                // (VitalStrikePart.OnEventDidTrigger(RuleCalculateWeaponStats), physical weapon damage only), nothing
                // against a target immune to precision damage (AddImmunityToPrecisionDamage declines it).
                int sneak = SafeStat(unit.Stats.SneakAttack);
                if (sneak > 0 && vitalStrike.RowdyFeature != null && unit.Descriptor.HasFact(vitalStrike.RowdyFeature) && !ImmuneToPrecision(target))
                    extra += 2 * sneak * 3.5f;
                // ★ v0.3.20 (playtest 1): physical weapon damage, so what the target keeps of it (a swarm halves it).
                return est.FirstHitChance * extra * est.FirstPhysicalFactor;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] vital strike {unit.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// ★ v0.3.18 (test10 review): the unit's weapon attacks add its Strength bonus to damage, by the game's own weapon
        /// rule (melee and unarmed always; a ranged weapon only through a stat-replacement enchantment such as a composite
        /// bow's). True when unsure.
        /// </summary>
        public static bool StrengthAddsWeaponDamage(UnitEntityData unit)
        {
            try
            {
                var weapon = unit?.Body?.PrimaryHand?.MaybeWeapon;
                if (weapon == null || !weapon.Blueprint.IsRanged) return true;
                var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon));
                return stats.DamageBonusStat == StatType.Strength;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>★ v0.3.18 (test10): the target declines precision damage (AddImmunityToPrecisionDamage in its facts).</summary>
        public static bool ImmuneToPrecision(UnitEntityData target)
        {
            if (target == null) return false;
            try
            {
                foreach (var fact in target.Facts.List)
                {
                    var components = fact?.Blueprint?.ComponentsArray;
                    if (components == null) continue;
                    foreach (var c in components)
                        // Incorporeal creatures too (the rule drops precision damage of non-ghost-touch weapons; a
                        // ghost-touch weapon is rare enough to count them as immune).
                        if (c is Kingmaker.UnitLogic.FactLogic.AddImmunityToPrecisionDamage ||
                            c is Kingmaker.UnitLogic.FactLogic.GhostCriticalAndPrecisionImmunity) return true;
                }
            }
            catch { }
            return false;
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

        /// <summary>★ v0.3.18 (review): the target's Evasion / Improved Evasion for this save (Evasion, ImprovedEvasion facts).</summary>
        private static void EvasionOf(UnitEntityData target, SavingThrowType save, out bool evasion, out bool improved)
        {
            evasion = improved = false;
            if (target == null || save == SavingThrowType.Unknown) return;
            try
            {
                foreach (var fact in target.Facts.List)
                {
                    var components = fact?.Blueprint?.ComponentsArray;
                    if (components == null) continue;
                    foreach (var c in components)
                    {
                        if (c is Kingmaker.Designers.Mechanics.Facts.Evasion e && e.SavingThrow == save) evasion = true;
                        if (c is Kingmaker.Designers.Mechanics.Facts.ImprovedEvasion ie && ie.SavingThrow == save) improved = true;
                    }
                }
            }
            catch { }
        }

        /// <summary>Probability the target fails its save (1 when the ability has no save).</summary>
        public static float SaveFailChance(AbilityData ability, AbilityProfile profile, UnitEntityData target) =>
            SaveFailChance(ability, profile.Save, target);

        /// <summary>★ v0.3.21: the same for a save the ability's own profile does not carry (an aura's: Archon's Aura).</summary>
        public static float SaveFailChance(AbilityData ability, SavingThrowType save, UnitEntityData target)
        {
            if (save == SavingThrowType.Unknown || target == null) return 1f;
            try
            {
                int dc = ability.CalculateParams()?.DC ?? 10;
                int bonus;
                switch (save)
                {
                    case SavingThrowType.Fortitude: bonus = target.Stats.SaveFortitude.ModifiedValue; break;
                    case SavingThrowType.Reflex: bonus = target.Stats.SaveReflex.ModifiedValue; break;
                    case SavingThrowType.Will: bonus = target.Stats.SaveWill.ModifiedValue; break;
                    default: return 1f;
                }
                // ★ v0.3.15: contextual save bonuses the game applied the last time (SaveObserver).
                var seen = SaveObserver.Lookup(target, save, ability.Blueprint);
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

        /// <summary>
        /// ★ v0.3.21: the hit points <paramref name="buff"/>'s poison (BuffPoisonStatDamage) is expected to take from
        /// <paramref name="target"/> through Constitution damage within <paramref name="maxRounds"/>: the dice once when it
        /// lands (OnActivate), then each round a save against the ability's DC, damage on a failure, until it was saved
        /// SuccesfullSaves times or Ticks rounds passed (ITickEachRound.OnNewRound). Two points of Constitution are one hit
        /// point per Hit Die. 0 for any other stat (those stay a plain debuff).
        /// </summary>
        public static float PoisonHpLoss(AbilityData ability, Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff buff, UnitEntityData target,
                                         float maxRounds)
        {
            if (buff == null || target == null) return 0f;
            float points = 0f;
            try
            {
                int dc = ability?.CalculateParams()?.DC ?? 10;
                foreach (var poison in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.Designers.Mechanics.Buffs.BuffPoisonStatDamage>(buff))
                {
                    if (poison.Stat != StatType.Constitution) continue;
                    float each = Average(poison.Value) + poison.Bonus;
                    if (each <= 0f) continue;
                    float fail = 1f;
                    switch (poison.SaveType)
                    {
                        case SavingThrowType.Fortitude: fail = Mathf.Clamp((dc - target.Stats.SaveFortitude.ModifiedValue - 1) / 20f, 0.05f, 0.95f); break;
                        case SavingThrowType.Reflex: fail = Mathf.Clamp((dc - target.Stats.SaveReflex.ModifiedValue - 1) / 20f, 0.05f, 0.95f); break;
                        case SavingThrowType.Will: fail = Mathf.Clamp((dc - target.Stats.SaveWill.ModifiedValue - 1) / 20f, 0.05f, 0.95f); break;
                    }
                    int later = Math.Min(poison.Ticks - 1, Mathf.FloorToInt(Math.Max(0f, maxRounds - 1f)));
                    points += each * ValueModel.PoisonDoses(fail, Math.Max(0, later), poison.SuccesfullSaves);
                }
                if (points <= 0f) return 0f;
                int hitDice = Math.Max(1, target.Descriptor.Progression.CharacterLevel);
                return Math.Min(points / 2f * hitDice, Math.Max(0f, target.HPLeft));
            }
            catch
            {
                return 0f;
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
                if (caster != null && target != null && DeliveryWeapon(caster, ability, out var weapon, out var stat, out var against))
                {
                    int ab;
                    using (stat.HasValue ? ContextData<AttackBonusStatReplacement>.Request().Setup(stat.Value) : null)
                        ab = Rulebook.Trigger(new RuleCalculateAttackBonus(caster, target, weapon, 0)).Result;
                    int ac = Rulebook.Trigger(new RuleCalculateAC(caster, target, against ?? weapon.Blueprint.AttackType)).Result;
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
                // ★ v0.3.18 (review): the buff's own descriptor too - the game asks with the buff's context, whose
                // descriptor is its parent's (the ability's) and the buff's SpellDescriptorComponent (MechanicsContext):
                // Demoralize's Shaken carries Fear only on the buff.
                var descriptor = ability.SpellDescriptor |
                                 (Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.Blueprints.Classes.Spells.SpellDescriptorComponent>(buff)?.Descriptor.Value
                                  ?? Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None);
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
        private static bool DeliveryWeapon(UnitEntityData caster, AbilityData ability, out ItemEntityWeapon weapon, out StatType? stat) =>
            DeliveryWeapon(caster, ability, out weapon, out stat, out _);

        /// <remarks>★ v0.3.18 (review): <paramref name="attackType"/> - the AC the roll is made against: a kinetic archer's
        /// blast shoots the bow but against touch AC when the blast's own weapon is a ranged touch one
        /// (AbilityDeliverProjectile: AttackTypeReplacement = Touch).</remarks>
        private static bool DeliveryWeapon(UnitEntityData caster, AbilityData ability, out ItemEntityWeapon weapon, out StatType? stat,
                                           out Kingmaker.RuleSystem.AttackType? attackType)
        {
            weapon = null;
            stat = null;
            attackType = null;
            var bp = ability?.GetDeliverBlueprint() ?? ability?.Blueprint;
            if (bp == null) return false;
            var projectile = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Abilities.Components.AbilityDeliverProjectile>(bp);
            if (projectile != null && projectile.NeedAttackRoll && !projectile.IsHandOfTheApprentice &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Line &&
                projectile.Type != Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Cone)
            {
                if (Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.IsArcherBlast(ability) &&
                    projectile.Type == Kingmaker.UnitLogic.Abilities.Components.AbilityProjectileType.Simple)
                {
                    weapon = caster.Body?.PrimaryHand?.MaybeWeapon;
                    if (projectile.Weapon?.Type?.AttackType == Kingmaker.RuleSystem.AttackType.RangedTouch)
                        attackType = Kingmaker.RuleSystem.AttackType.Touch;
                }
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
        /// ★ v0.3.21 (gaps): how much of a shadow spell (a Shadow Evocation / Conjuration copy: AbilityData.ShadowSpellSettings)
        /// is real for <paramref name="target"/>. It disbelieves on a passed Will save against the spell's DC; then damage is cut
        /// to the shadow factor and any other effect lands with that chance (MechanicsContext.TryAffectByShadow,
        /// ShadowSpellLogicExtension). 1 for any other ability; with no target (a shadow summon's worth) an even save:
        /// half real, half at the factor.
        /// </summary>
        public static float ShadowShare(AbilityData ability, UnitEntityData target)
        {
            try
            {
                var shadow = ability?.ShadowSpellSettings;
                if (shadow?.Factor == null) return 1f;
                float factor = Mathf.Clamp01(shadow.Factor.GetInt(ability.Caster?.Unit) / 100f);
                if (factor <= 0f) return 1f;
                float fail = target != null ? SaveFailChance(ability, SavingThrowType.Will, target) : 0.5f;
                return fail + (1f - fail) * factor;
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>★ v0.3.21 (gaps): the unit regenerates (AddEffectRegeneration: UnitPartRegeneration) - at 0 HP it only
        /// falls unconscious while its regeneration runs, and gets up again (UnitLifeController).</summary>
        public static bool Regenerates(UnitEntityData unit)
        {
            try { return (unit?.Get<Kingmaker.UnitLogic.Parts.UnitPartRegeneration>()?.Items.Count ?? 0) > 0; }
            catch { return false; }
        }

        /// <summary>
        /// ★ v0.3.21 (gaps): damage of these kinds stops <paramref name="target"/>'s regeneration for a round - the game's
        /// test (AddEffectRegeneration.OnEventDidTrigger): an energy, alignment or material it names, or a magic weapon when it
        /// says so; never an unremovable one. Then 0 HP is death.
        /// </summary>
        public static bool EndsRegenerationOf(UnitEntityData target, IEnumerable<Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription> types,
                                              bool magicWeapon, IEnumerable<Kingmaker.Enums.Damage.DamageAlignment> alignments)
        {
            try
            {
                var part = target?.Get<Kingmaker.UnitLogic.Parts.UnitPartRegeneration>();
                if (part == null || part.Items.Count == 0) return false;
                var list = types?.Where(t => t != null).ToList() ?? new List<Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription>();
                var aligned = (alignments ?? Enumerable.Empty<Kingmaker.Enums.Damage.DamageAlignment>()).ToList();
                foreach (var item in part.Items)
                {
                    var c = item.Component;
                    if (c == null || c.Unremovable) continue;
                    if (c.CancelByMagicWeapon && magicWeapon) return true;
                    foreach (var t in list)
                    {
                        if (t.Type == Kingmaker.RuleSystem.Rules.Damage.DamageType.Energy && c.CancelDamageEnergyTypes.Contains(t.Energy)) return true;
                        // Materials and alignments are flags (cold iron and silver at once): any one of them counts.
                        if (t.Type == Kingmaker.RuleSystem.Rules.Damage.DamageType.Physical &&
                            c.CancelDamageMaterials.Any(m => (t.Physical.Material & m) != 0)) return true;
                        if (c.CancelDamageAlignmentTypes.Any(a => (t.Common.Alignment & a) != 0)) return true;
                    }
                    if (aligned.Any(a => c.CancelDamageAlignmentTypes.Any(x => (a & x) != 0))) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>★ v0.3.21: the alignments a weapon's enchantments add to its damage (WeaponAlignment: holy, unholy...).</summary>
        private static IEnumerable<Kingmaker.Enums.Damage.DamageAlignment> WeaponAlignments(ItemEntityWeapon weapon)
        {
            var result = new List<Kingmaker.Enums.Damage.DamageAlignment>();
            try
            {
                foreach (var e in weapon.Enchantments)
                    foreach (var c in e.Blueprint.ComponentsArray ?? new Kingmaker.Blueprints.BlueprintComponent[0])
                        if (c is Kingmaker.Designers.Mechanics.Facts.WeaponAlignment wa) result.Add(wa.Alignment);
            }
            catch { }
            return result;
        }

        /// <summary>★ v0.3.21 (gaps): <paramref name="profile"/>'s damage stops <paramref name="target"/>'s regeneration
        /// (Fire Ball or Acid Arrow on a troll).</summary>
        public static bool AbilityEndsRegeneration(AbilityProfile profile, UnitEntityData target) =>
            profile != null && Regenerates(target) &&
            EndsRegenerationOf(target, profile.Damage.OfType<ContextActionDealDamage>().Select(d => d.DamageType), false, null);

        /// <summary>★ v0.3.9: cannot act right now (asleep, paralysed, stunned, helpless...).</summary>
        public static bool IsDisabled(UnitEntityData unit)
        {
            var state = unit?.Descriptor?.State;
            if (state == null) return false;
            return state.HasCondition(UnitCondition.Sleeping) || state.HasCondition(UnitCondition.Paralyzed) ||
                   state.HasCondition(UnitCondition.Stunned) || state.HasCondition(UnitCondition.Helpless) ||
                   state.HasCondition(UnitCondition.CantAct) || state.IsUnconscious;
        }

        /// <summary>★ v0.3.19 (test2): no enemy in the decision being made can act (set by TacticalEngine.BuildContext).</summary>
        internal static bool OnlyDisabledEnemiesLeft;

        /// <summary>
        /// Rough damage-per-round an enemy is worth removing. CR-based so it is stable and cheap; adjusted for enemies
        /// that are already disabled and for enemies currently attacking the party.
        /// </summary>
        public static float Threat(UnitEntityData enemy)
        {
            if (enemy == null) return 0f;
            float cr = 1f;
            try { cr = Math.Max(1, enemy.Blueprint?.CR ?? 1); } catch { }
            float threat = ValueModel.PerRound(cr);   // ★ v0.3.18: the shared worth per round (ValueModel)
            // ★ v0.3.20 (test4): the enemy's share of the fight by what it can do and did (EnemyThreat), 0.5..2.
            threat *= EnemyThreat.Share(enemy);

            if (IsDisabled(enemy)) threat *= 0.25f;

            try { if (enemy.CombatState.EngagedUnits.Count > 0) threat *= 1.2f; } catch { }
            return threat;
        }

        /// <summary>
        /// ★ v0.3.19 (test2): what taking this enemy down is worth: its <see cref="Threat"/>, but in full while every enemy
        /// left is disabled - the fight cannot end without them. A sleeping, entangled Dretch was the last enemy for four
        /// rounds while Camellia and Seelah found nothing worth doing. Only for the worth of damage and kills: the danger an
        /// enemy poses (incoming damage, attacks of opportunity) keeps the disabled discount.
        /// </summary>
        public static float TargetWorth(UnitEntityData enemy)
        {
            float threat = ControlWorth(enemy);
            return OnlyDisabledEnemiesLeft && IsDisabled(enemy) ? threat * 4f : threat;
        }

        /// <summary>
        /// ★ v0.3.20 (test4): what holding this enemy (control, a debuff, a maneuver) or taking it down is worth: its
        /// <see cref="Threat"/>, more while the ally it attacks may drop to it this round (EnemyThreat.Urgency, 1..1.5). Not
        /// for the danger it poses (incoming damage): the drop chance is already that danger's consequence.
        /// </summary>
        public static float ControlWorth(UnitEntityData enemy) => Threat(enemy) * EnemyThreat.Urgency(enemy);

        #endregion
    }
}
