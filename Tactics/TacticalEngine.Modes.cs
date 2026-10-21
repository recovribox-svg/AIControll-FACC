// ★ v0.3.18 (docs/ACTIVE_ABILITY_PLAN.md AA-3, design E1 (b)/(c), E2, E3): attack modes decided with each attack, and
// Mobility decided with the move whose attacks of opportunity it changes. Setting: "AI may switch combat modes" (on).
//
//   Attack modes (Power Attack, Deadly Aim, Piranha Strike, Combat Expertise, Fighting Defensively): toggles whose buff
//   only changes attack, damage or AC, through the game's own components - WeaponParametersAttackBonus /
//   WeaponParametersDamageBonus (their weapon, two-handed, Greater / mythic Power Attack and base-attack scaling) and
//   stat bonuses to the attack bonus or AC (AddStatBonus, AddContextStatBonus, AddStatBonusAbilityValue). A stat
//   bonus's value comes from the game itself: a mechanics context for the unit and the buff works out its ranks
//   (ContextRankConfig, custom properties such as Fighting Defensively's +2/+3 AC and its attack penalty). A buff with
//   anything else, or a component setting the AI does not model, is "not modelled" and left as it is (coverage report).
//   Each weapon option is valued with every mode set (one mode per group, the game's group rule): the attack routine
//   with the modes' attack and damage changes, plus for AC the damage avoided until the unit's next turn (the
//   drop-chance model). The candidate keeps the best set when it beats the current one by 10 % of the option's value
//   (at least 0.5), and Execute switches the modes the way the action bar does just before the attack.
//
//   Mobility (Acrobatics): while on, an enemy's attack of opportunity on a unit leaving its reach is cancelled when the
//   unit passes a Mobility check against that enemy's CMD (UnitCombatState.AttackOfOpportunity), and speed is halved.
//   Moves are priced with that chance whenever the unit has it on (any source). The AI switches it on (a free action)
//   before a move that would provoke when the attacks avoided are worth more than the move it can no longer make at
//   half speed, and off again when no enemy threatens the unit.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        internal enum ModeKind { Attack, Mobility }

        private sealed class ModeProfile
        {
            public ModeKind Kind;
            public string Excluded;
            public readonly List<WeaponParametersAttackBonus> AttackParts = new List<WeaponParametersAttackBonus>();
            public readonly List<WeaponParametersDamageBonus> DamageParts = new List<WeaponParametersDamageBonus>();
            public readonly List<BlueprintComponent> StatParts = new List<BlueprintComponent>();
        }

        private static readonly Dictionary<BlueprintActivatableAbility, ModeProfile> ModeProfiles =
            new Dictionary<BlueprintActivatableAbility, ModeProfile>();

        private static readonly HashSet<Type> ModeHelperComponents = new HashSet<Type>
        {
            typeof(ContextRankConfig), typeof(RecalculateOnStatChange), typeof(RecalculateOnFactsChange)
        };

        private static bool PrivateFlag(object component, string field)
        {
            try { return HarmonyLib.Traverse.Create(component).Field(field).GetValue<bool>(); }
            catch { return true; }   // unreadable: treat as a setting the AI does not model
        }

        private static ModeProfile ModeOf(BlueprintActivatableAbility bp)
        {
            if (ModeProfiles.TryGetValue(bp, out var cached)) return cached;
            var p = new ModeProfile();
            try
            {
                var buff = bp.Buff;
                if (buff == null) p.Excluded = "no buff";
                else
                {
                    bool mobility = false;
                    foreach (var c in buff.ComponentsArray ?? new BlueprintComponent[0])
                    {
                        if (c == null || ModeHelperComponents.Contains(c.GetType())) continue;
                        switch (c)
                        {
                            case AcrobaticMovement _:
                                mobility = true;
                                break;
                            case WeaponParametersAttackBonus a:
                                if (a.SpecificWeaponBlueprint || a.MultiplierFact != null || PrivateFlag(a, "m_checkGripStyle"))
                                    p.Excluded = "an attack bonus the AI does not model";
                                p.AttackParts.Add(a);
                                break;
                            case WeaponParametersDamageBonus d:
                                if (d.OnlySpecificItemBlueprint || d.DualWielding || d.OnlyToOffHandBonus || PrivateFlag(d, "m_checkGripStyle"))
                                    p.Excluded = "a damage bonus the AI does not model";
                                p.DamageParts.Add(d);
                                break;
                            case AddStatBonus s when IsModeStat(s.Stat):
                            case AddContextStatBonus cs when IsModeStat(cs.Stat):
                            case AddStatBonusAbilityValue sv when IsModeStat(sv.Stat):
                                p.StatParts.Add(c);
                                break;
                            default:
                                p.Excluded = p.Excluded ?? $"its buff does more than the AI models ({c.GetType().Name})";
                                break;
                        }
                    }
                    // ★ v0.3.18 (review): an action spent every round (ActivatableAbilityUnitCommand) or a resource per round is
                    // not part of the attack's value: such a mode is left as the player set it.
                    if (bp.GetComponent<ActivatableAbilityUnitCommand>() != null)
                        p.Excluded = p.Excluded ?? "spends an action every round";
                    else if (bp.GetComponent<ActivatableAbilityResourceLogic>() != null)
                        p.Excluded = p.Excluded ?? "spends a resource";
                    if (mobility)
                    {
                        p.Kind = ModeKind.Mobility;
                        if (!bp.ActivateWithUnitCommand) p.Excluded = p.Excluded ?? "not switched by a command";
                    }
                    else
                    {
                        p.Kind = ModeKind.Attack;
                        if (p.AttackParts.Count == 0 && p.DamageParts.Count == 0 && p.StatParts.Count == 0)
                            p.Excluded = p.Excluded ?? "not an attack mode";
                        else if (bp.ActivateWithUnitCommand)
                            p.Excluded = p.Excluded ?? "switched by a command";
                    }
                }
            }
            catch (Exception ex)
            {
                p.Excluded = "unreadable: " + ex.Message;
            }
            ModeProfiles[bp] = p;
            return p;
        }

        private static bool IsModeStat(StatType stat) => stat == StatType.AC || stat == StatType.AdditionalAttackBonus;

        /// <summary>Why the AI does not switch this mode (null: it may). For the coverage report.</summary>
        internal static string ModeExclusion(ActivatableAbility toggle)
        {
            if (toggle?.Blueprint == null) return "no blueprint";
            if (!(ModSettings.Instance?.AiSwitchesCombatModes ?? true)) return "switched off in the settings";
            if (toggle.Blueprint.HiddenInUI) return "hidden";
            return ModeOf(toggle.Blueprint).Excluded;
        }

        /// <summary>A mode the unit has, with what switching it changes, read once per decision.</summary>
        internal sealed class UnitMode
        {
            public ActivatableAbility Toggle;
            /// <summary>Switched on (or its buff still running): what a switch changes.</summary>
            public bool On;
            /// <summary>★ v0.3.18 (review): its buff is in the game's rules now (started), so the attack routine read
            /// from the rules already has it. A Fighting Defensively that is on between attacks is not: it starts again
            /// on the next attack (ActivateOnUnitAction), its buff gone at the round's start.</summary>
            public bool Active;
            /// <summary>★ v0.3.18 (review): it is started and the game keeps its buff until the round ends when it is
            /// switched off (TurnOffImmediatelyWithUnitCommand costs an action): an "off" would not change this attack.</summary>
            public bool StaysUntilRoundEnd;
            public int AC;
            public int AttackStat;
            /// <summary>Attack change the rules apply on any attack (the probe's routine has it once the buff runs).</summary>
            public Func<ItemEntityWeapon, bool, int> Attack;
            /// <summary>★ v0.3.18 (review): attack bonus only for a full attack (WeaponParametersAttackBonus
            /// OnlyForFullAttack): only a real full attack has it, never the probe's routine.</summary>
            public Func<ItemEntityWeapon, bool, int> FullAttackOnly;
            public Func<ItemEntityWeapon, float> Damage;
            public string Name => Toggle.Name;
            public ActivatableAbilityGroup Group => Toggle.Blueprint.Group;
        }

        private static List<UnitMode> AttackModes(TacticalContext ctx)
        {
            if (ctx.AttackModes != null) return ctx.AttackModes;
            var modes = new List<UnitMode>();
            ctx.AttackModes = modes;
            if (!(ModSettings.Instance?.AiSwitchesCombatModes ?? true)) return modes;
            var unit = ctx.Unit;
            foreach (var toggle in unit.ActivatableAbilities?.Enumerable ?? Enumerable.Empty<ActivatableAbility>())
            {
                if (toggle?.Blueprint == null || ModeExclusion(toggle) != null) continue;
                var p = ModeOf(toggle.Blueprint);
                if (p.Kind != ModeKind.Attack) continue;
                if (!toggle.IsOn && !toggle.IsAvailable) continue;
                try { modes.Add(ReadMode(unit, toggle, p)); }
                catch (Exception ex) { Main.Verbose($"[Modes] {unit.CharacterName} {toggle.Name}: {ex.Message}"); }
            }
            return modes;
        }

        private static UnitMode ReadMode(UnitEntityData unit, ActivatableAbility toggle, ModeProfile p)
        {
            var buff = toggle.Blueprint.Buff;
            // The game works the buff's ranks out itself (ContextRankConfig, custom properties), for this unit.
            var mctx = new MechanicsContext(unit, unit.Descriptor, buff);
            try { mctx.Recalculate(); } catch { }
            var bp = toggle.Blueprint;
            var mode = new UnitMode
            {
                Toggle = toggle,
                On = toggle.IsOn || toggle.IsStarted,
                Active = toggle.IsStarted,
                StaysUntilRoundEnd = toggle.IsStarted && !bp.DeactivateImmediately &&
                                     bp.GetComponent<TurnOffImmediatelyWithUnitCommand>() != null
            };
            foreach (var c in p.StatParts)
            {
                StatType stat;
                int value;
                switch (c)
                {
                    case AddStatBonus s:
                        stat = s.Stat;
                        value = s.Value * (s.ScaleByBasicAttackBonus ? 1 + unit.Stats.BaseAttackBonus.ModifiedValue / 4 : 1);
                        break;
                    case AddContextStatBonus cs:
                        stat = cs.Stat;
                        value = cs.Value.Calculate(mctx) * cs.Multiplier;
                        break;
                    case AddStatBonusAbilityValue sv:
                        stat = sv.Stat;
                        value = sv.Value.Calculate(mctx);
                        break;
                    default:
                        continue;
                }
                if (stat == StatType.AC) mode.AC += value;
                else mode.AttackStat += value;
            }
            int bab = unit.Stats.BaseAttackBonus.ModifiedValue;
            var attackParts = p.AttackParts.ToList();
            var damageParts = p.DamageParts.ToList();
            int attackStat = mode.AttackStat;
            mode.Attack = (weapon, full) =>
            {
                int total = attackStat;
                foreach (var a in attackParts)
                {
                    if (a.OnlyForFullAttack || !WeaponFits(weapon, a.Ranged, a.OnlyTwoHanded, a.OnlyFinessable, unit)) continue;
                    int n = a.UseContextIstead ? a.ContextAttackBonus.Calculate(mctx) : a.AttackBonus;
                    if (a.ScaleByBasicAttackBonus) n *= 1 + bab / 4;
                    total += n;
                }
                return total;
            };
            mode.FullAttackOnly = (weapon, full) =>
            {
                int total = 0;
                if (!full) return 0;
                foreach (var a in attackParts)
                {
                    if (!a.OnlyForFullAttack || !WeaponFits(weapon, a.Ranged, a.OnlyTwoHanded, a.OnlyFinessable, unit)) continue;
                    int n = a.UseContextIstead ? a.ContextAttackBonus.Calculate(mctx) : a.AttackBonus;
                    if (a.ScaleByBasicAttackBonus) n *= 1 + bab / 4;
                    total += n;
                }
                return total;
            };
            mode.Damage = weapon =>
            {
                float total = 0f;
                foreach (var d in damageParts)
                {
                    if (!WeaponFits(weapon, d.Ranged, d.OnlyTwoHanded, d.OnlyFinessable, unit)) continue;
                    int n = d.UseContextIstead ? d.ContextDamageBonus.Calculate(mctx) : d.DamageBonus;
                    if (d.MythicBlueprint != null && unit.Descriptor.HasFact(d.MythicBlueprint)) n += 1;
                    if (d.ScaleByBasicAttackBonus) n *= 1 + bab / 4;
                    // WeaponParametersDamageBonus.CalculatePowerAttackScaling (secondary natural attacks are not told apart)
                    if (d.PowerAttackScaling && weapon.HoldInTwoHands)
                    {
                        if (d.GreaterPowerAttackBlueprint != null && unit.Descriptor.HasFact(d.GreaterPowerAttackBlueprint)) n *= 2;
                        else if (d.MythicBlueprint != null && unit.Descriptor.HasFact(d.MythicBlueprint)) n = n * 5 / 3;
                        else n = n * 3 / 2;
                    }
                    total += n;
                }
                return total;
            };
            return mode;
        }

        private static bool WeaponFits(ItemEntityWeapon weapon, bool ranged, bool onlyTwoHanded, bool onlyFinessable, UnitEntityData unit)
        {
            if (weapon?.Blueprint == null) return false;
            if (ranged != weapon.Blueprint.IsRanged) return false;
            if (onlyTwoHanded && !weapon.Blueprint.IsTwoHanded) return false;
            if (onlyFinessable && !weapon.Blueprint.Type.Category.HasSubCategory(WeaponSubCategory.Finessable)) return false;
            return true;
        }

        /// <summary>
        /// Damage avoided (or taken, for a negative change) until the unit's next turn by <paramref name="acDelta"/> more AC
        /// at <paramref name="at"/>: the enemies' expected damage there falls by 5 % of a d20 per point, relative to their
        /// chance to hit, priced like a heal (the chance to drop, and the HP itself).
        /// </summary>
        private static float DefenseValue(TacticalContext ctx, Vector3 at, int acDelta)
        {
            if (acDelta == 0) return 0f;
            var unit = ctx.Unit;
            float danger = IncomingDamage(ctx, unit, at);
            if (danger <= 0f) return 0f;
            // ★ v0.3.18 (review): per enemy, by its own attack roll against the AC with and without the change (the d20's
            // limits included), weighted by its part of the danger; an enemy without a weapon attack (a caster) gains
            // nothing from AC. One average hit chance for all, from the enemies within 4 m only, credited AC against
            // spells, and +2 AC against an enemy that hits on a 20 only counted as 90 % of the damage avoided.
            var parts = IncomingParts(ctx, unit, at);
            float all = parts.Sum(p => p.Value), avoided = 0f;
            foreach (var part in parts)
            {
                var est = CombatMath.EstimateWeaponAttack(part.Key, unit, full: false);
                if (est == null) continue;
                float before = CombatMath.HitProbability(est.FirstAttackBonus, est.FirstArmorClass);
                float after = CombatMath.HitProbability(est.FirstAttackBonus, est.FirstArmorClass + acDelta);
                avoided += part.Value * (before - after) / Math.Max(0.05f, before);
            }
            float share = all > 0f ? Mathf.Clamp(avoided / all, -1f, 0.95f) : 0f;
            if (Math.Abs(share) < 1e-4f) return 0f;
            float hp = Math.Max(1f, unit.HPLeft);
            float dropBefore = RiskMath.RoundDropChance(hp, danger);
            float dropAfter = RiskMath.RoundDropChance(hp, danger * (1f - share));
            return ((dropBefore - dropAfter) * AllyWorth(ctx, unit) + danger * share * 0.3f) * RiskTolerance(ctx);
        }

        /// <summary>
        /// The best set of attack modes for a weapon option, by the value it adds to that option (attack and damage by the
        /// game's own routine, AC by the damage avoided). Sets the candidate's modes and raises its value when the best set
        /// beats the current state by the hysteresis margin.
        /// </summary>
        private static void ChooseAttackModes(TacticalContext ctx, Candidate c)
        {
            var modes = AttackModes(ctx);
            if (modes.Count == 0 || c.Kind != CandidateKind.WeaponAttack || c.Target == null || c.ModeFactor <= 0f) return;
            var unit = ctx.Unit;
            bool full = c.FullAttack;
            Vector3 at = c.ModeAt ?? unit.Position;

            // ★ v0.3.18 (review): every set is valued against the game's rules as they stand (the attack routine, the AC
            // the enemies roll against) - a mode's changes count by whether it will apply at this attack and whether it
            // applies now. "Keep everything as it is" is valued the same way: a Fighting Defensively that is on but not
            // started (it restarts on the attack) was missing its -4 there.
            Func<HashSet<UnitMode>, KeyValuePair<float, float>> valueOf = set =>
            {
                var estimate = CombatMath.EstimateWeaponAttack(unit, c.Target, full, (weapon, index) =>
                {
                    int ab = 0;
                    float dmg = 0f;
                    foreach (var m in modes)
                    {
                        int sign = (set.Contains(m) ? 1 : 0) - (m.Active ? 1 : 0);
                        if (set.Contains(m)) ab += m.FullAttackOnly(weapon, full);
                        if (sign == 0) continue;
                        ab += sign * m.Attack(weapon, full);
                        dmg += sign * m.Damage(weapon);
                    }
                    return new KeyValuePair<int, float>(ab, dmg);
                });
                int ac = modes.Sum(m => ((set.Contains(m) ? 1 : 0) - (m.Active ? 1 : 0)) * m.AC);
                return new KeyValuePair<float, float>(estimate == null ? -1f : DamageValue(c.Target, estimate), DefenseValue(ctx, at, ac));
            };
            var current = new HashSet<UnitMode>(modes.Where(m => m.On));
            var baseline = valueOf(current);
            if (baseline.Key < 0f) return;

            // One option per group (none on, or one of its modes), each ungrouped mode on or off. ★ v0.3.18 (review): past
            // 32 sets, the groups left out keep their current state (they were left out as "off").
            var groups = modes.GroupBy(m => m.Group == ActivatableAbilityGroup.None ? (object)m : m.Group).ToList();
            var combos = new List<List<UnitMode>> { new List<UnitMode>() };   // each combo: the modes on
            int used = 0;
            foreach (var group in groups)
            {
                if (combos.Count * (group.Count() + 1) > 32) break;
                var options = new List<UnitMode> { null };
                options.AddRange(group);
                combos = combos.SelectMany(combo => options.Select(o => o == null ? combo : combo.Concat(new[] { o }).ToList())).ToList();
                used++;
            }
            var kept = groups.Skip(used).SelectMany(g => g).Where(m => m.On).ToList();

            float bestGain = 0f;
            HashSet<UnitMode> best = null;
            foreach (var combo in combos)
            {
                var set = new HashSet<UnitMode>(combo.Concat(kept));
                var changes = modes.Where(m => set.Contains(m) != m.On).ToList();
                if (changes.Count == 0) continue;
                // A mode that cannot start now cannot be chosen on; one whose buff stays until the round ends cannot be
                // taken off this attack.
                if (changes.Any(m => !m.On && !m.Toggle.IsAvailable)) continue;
                if (changes.Any(m => m.On && m.StaysUntilRoundEnd)) continue;
                if (changes.Any(m => CombatMemory.ToggleSwitchedThisRound(unit, m.Toggle))) continue;
                var v = valueOf(set);
                if (v.Key < 0f) continue;
                float gain = (v.Key - baseline.Key) * c.ModeFactor + (v.Value - baseline.Value);
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = set;
                }
            }
            if (best == null) return;
            // E3 hysteresis: a change must clearly pay.
            if (bestGain < Math.Max(0.5f, 0.1f * Math.Abs(c.Value))) return;
            c.Modes = modes.Where(m => best.Contains(m) != m.On).Select(m => new KeyValuePair<ActivatableAbility, bool>(m.Toggle, !m.On)).ToList();
            c.Value += bestGain;
            c.Label += " [" + string.Join(", ", c.Modes.Select(m => $"{m.Key.Name} {(m.Value ? "on" : "off")}")) + $" +{bestGain:F1}]";
        }

        /// <summary>Switch the candidate's attack modes the way the action bar does, just before its attack.</summary>
        /// <remarks>★ v0.3.18 (review): off with the game's TurnOffImmediately (its buff removed now, as the value assumed):
        /// switching IsOn off alone leaves a started mode's buff until the round ends, unless the blueprint deactivates
        /// immediately. Modes whose immediate turn-off costs an action are never chosen off (ChooseAttackModes).</remarks>
        private static void ApplyModes(UnitEntityData unit, Candidate c)
        {
            if (c.Modes == null) return;
            foreach (var change in c.Modes)
            {
                try
                {
                    var toggle = change.Key;
                    if (change.Value)
                    {
                        if (toggle.IsOn) continue;
                        toggle.IsOn = true;
                    }
                    else
                    {
                        if (!toggle.IsOn && !toggle.IsStarted) continue;
                        if (toggle.Blueprint.GetComponent<TurnOffImmediatelyWithUnitCommand>() == null) toggle.TurnOffImmediately();
                        else toggle.IsOn = false;
                    }
                    CombatMemory.RecordToggle(unit, toggle, change.Value);
                    Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} {(change.Value ? "on" : "off")} for '{c.Label}'");
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Toggle] {unit.CharacterName}: {change.Key.Name}: {ex.Message}");
                }
            }
        }

        // --- Mobility ---

        private static ActivatableAbility MobilityToggle(UnitEntityData unit) =>
            unit.ActivatableAbilities?.Enumerable?.FirstOrDefault(t => t?.Blueprint != null && ModeExclusion(t) == null &&
                                                                       ModeOf(t.Blueprint).Kind == ModeKind.Mobility);

        /// <summary>Movement speed with Mobility on (UnitEntityData: x0.5, x1.5 with Trickster's fast movement).</summary>
        private static float MobilitySpeedFactor(UnitEntityData unit)
        {
            try { return unit.Descriptor.State.Features.TricksterMobilityFastMovement ? 1.5f : 0.5f; }
            catch { return 0.5f; }
        }

        /// <summary>The unit leaves reach with Mobility on (any source): its check can cancel each attack of opportunity.</summary>
        private static bool UsesMobility(UnitEntityData unit)
        {
            try { return unit.Descriptor.State.HasCondition(UnitCondition.UseMobilityToNegateAttackOfOpportunity); }
            catch { return false; }
        }

        /// <summary>
        /// Chance a Mobility check (d20 + Mobility) beats <paramref name="enemy"/>'s CMD against <paramref name="unit"/>
        /// (UnitCombatState.AttackOfOpportunity: RuleCalculateCMD, then a skill check against it).
        /// </summary>
        private static float MobilityAvoids(UnitEntityData unit, UnitEntityData enemy)
        {
            try
            {
                int cmd = Rulebook.Trigger(new RuleCalculateCMD(unit, enemy, CombatManeuver.None)).Result;
                // ★ v0.3.18 (review): a skill check's DC is adjusted by difficulty (RuleSkillCheck.UpdateValues).
                cmd = RuleSkillCheck.AdjustDCByDifficulty(cmd, out _);
                int bonus = unit.Stats.GetStat(StatType.SkillMobility).ModifiedValue;
                return Mathf.Clamp01((21f + bonus - cmd) / 20f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Switched off once no enemy threatens the unit: then speed matters more. Only when the AI switched it on.</summary>
        private static void MobilityHousekeeping(UnitEntityData unit)
        {
            var toggle = MobilityToggle(unit);
            if (toggle == null || !(toggle.IsOn || toggle.IsStarted) || !CombatMemory.ToggleStartedByAi(unit, toggle)) return;
            if (CombatMemory.ToggleSwitchedThisRound(unit, toggle)) return;
            bool threatened;
            try { threatened = unit.CombatState.EngagedBy.Any(e => e != null && e.Descriptor.State.IsConscious); }
            catch { threatened = true; }
            if (threatened) return;
            toggle.IsOn = false;
            CombatMemory.RecordToggle(unit, toggle, false);
            Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} off - no enemy threatens it (full speed again)");
        }

        /// <summary>
        /// Mobility on (a free action) before the best move-and-act option that provokes, when the attacks of opportunity it
        /// would cancel are worth more than nothing and the move still fits at half speed.
        /// </summary>
        private static void AddMobilityCandidate(TacticalContext ctx, List<Candidate> list, TurnMemory memory)
        {
            if (!(ModSettings.Instance?.AiSwitchesCombatModes ?? true) || ctx.NoMovementLeft || ctx.StepTaken) return;
            var unit = ctx.Unit;
            var toggle = MobilityToggle(unit);
            if (toggle == null || toggle.IsOn || toggle.IsStarted || !toggle.IsAvailable) return;
            if (CombatMemory.ToggleSwitchedThisRound(unit, toggle)) return;
            // Priced as a free (or swift) action next to the move; one that costs the move itself is not modelled.
            var slot = ToggleSlot(toggle);
            if (slot != ActionSlot.Free && slot != ActionSlot.Swift) return;
            if (slot == ActionSlot.Swift && !ctx.HasSwift) return;
            // ★ v0.3.18 (review): only options this turn can still take (the same filter as Decide's usable list): an
            // option refused earlier this turn was "protected" with Mobility, then dropped - half speed for nothing.
            Func<Candidate, bool> takeable = c => c.Value > 0f && c.Slot != ActionSlot.Free && c.Slot != ActionSlot.Swift &&
                                                  Affordable(ctx, c) && !memory.Failed.Contains(c.Key) && !SilentlyFailing(ctx, c) &&
                                                  !(c.Target != null && ctx.Unreachable.Contains(c.Target.UniqueId));
            float speed = MobilitySpeedFactor(unit);
            var risky = list.Where(c => takeable(c) && c.Risk != null && c.Risk.Attacks > 0 && c.RiskAt.HasValue)
                            .OrderByDescending(c => c.Value).Take(3).ToList();
            Candidate best = null;
            Disengage without = null;
            float gain = 0f;
            foreach (var c in risky)
            {
                // Speed halves (x1.5 for a Trickster with fast movement): the move must still fit.
                if (Vector3.Distance(unit.Position, c.RiskAt.Value) > ctx.MoveRange * Math.Min(1f, speed) * 0.95f) continue;
                var off = DisengageRisk(ctx, c.RiskAt.Value, mobility: false);
                if (off == null) continue;
                var on = DisengageRisk(ctx, c.RiskAt.Value, mobility: true);
                float g = c.PreRiskValue * off.StopChance + off.Cost - (on == null ? 0f : c.PreRiskValue * on.StopChance + on.Cost);
                if (best == null || c.Value + g > best.Value + gain)
                {
                    best = c;
                    without = off;
                    gain = g;
                }
            }
            if (best == null || gain < MinQuickValue) return;
            // Only worth it when the move it protects then is the best thing to do.
            float other = list.Where(c => c != best && takeable(c))
                              .Select(c => c.Value).DefaultIfEmpty(0f).Max();
            if (best.Value + gain <= other) return;
            list.Add(new Candidate
            {
                Kind = CandidateKind.Toggle,
                Slot = slot,
                Toggle = toggle,
                Value = gain,
                Label = $"Start {toggle.Name} before '{best.Label}' (avoids {without.Attacks} attack(s) of opportunity)",
                Key = $"tgl:{toggle.Blueprint.AssetGuid}"
            });
        }
    }
}
