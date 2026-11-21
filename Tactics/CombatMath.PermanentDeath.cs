// ★ coverage3 review / Fusion coverage C3 (21 November 2026, author's policy; docs/COVERAGE3_REVIEW.md): the chance a cast kills
// a unit for good - Disintegrate's dust, a kill action, a buff that kills on activation. Read once per blueprint from its action
// tree; priced per recipient with the exact dice (PermanentDeathRules). Read only: no actor, command or rule is changed.
// Native contracts (the supplied assembly): ContextConditionCompareTargetHP compares Target.HPLeft after the damage that runs
// before it; ContextActionConditionalSaved runs Succeed on a passed save, Failed otherwise, and neither without a rolled save;
// ContextActionKill sets State.MarkedForDeath; temporary HP absorb damage first (CombatMath.HpBeforeDown).
using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.RuleSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Designers.EventConditionActionSystem.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        /// <summary>Which outcome of the cast's saving throw an effect needs.</summary>
        internal enum DeathPath { Any, Failed, Passed }

        /// <summary>What a blueprint's action tree can do that kills for good, and the hit-point damage before it.</summary>
        internal sealed class DeathScan
        {
            internal readonly List<KeyValuePair<DeathPath, ContextActionDealDamage>> Damage = new List<KeyValuePair<DeathPath, ContextActionDealDamage>>();
            /// <summary>Dust gates: an HP comparison (Less <c>Below</c>) that applies a buff killing on activation.</summary>
            internal readonly List<KeyValuePair<DeathPath, int?>> Gates = new List<KeyValuePair<DeathPath, int?>>();
            /// <summary>Direct kills: ContextActionKill, or a buff that kills on activation outside a dust gate.</summary>
            internal readonly List<DeathPath> Kills = new List<DeathPath>();
            internal bool SavePaths;
            internal SavingThrowType Save = SavingThrowType.Unknown;
            internal bool Empty => Gates.Count == 0 && Kills.Count == 0;
        }

        private static readonly Dictionary<BlueprintAbility, DeathScan> DeathScans = new Dictionary<BlueprintAbility, DeathScan>();
        private static readonly Dictionary<KeyValuePair<BlueprintAbility, BlueprintAbility>, DeathScan> FusedDeathScans =
            new Dictionary<KeyValuePair<BlueprintAbility, BlueprintAbility>, DeathScan>();
        private static readonly System.Reflection.FieldInfo CompareTypeField =
            HarmonyLib.AccessTools.Field(typeof(ContextConditionCompareTargetHP), "m_CompareType");

        /// <summary>The scan of <paramref name="ability"/>: its own blueprint, or both payloads of a fused cast.</summary>
        internal static DeathScan DeathScanOf(AbilityData ability)
        {
            var hack = ability?.MagicHackData;
            if (hack?.Spell1 != null && hack.Spell2 != null)
            {
                var key = new KeyValuePair<BlueprintAbility, BlueprintAbility>(hack.Spell1, hack.Spell2);
                if (FusedDeathScans.TryGetValue(key, out var fused)) return fused;
                var one = DeathScanOf(hack.Spell1);
                var two = DeathScanOf(hack.Spell2);
                fused = new DeathScan { SavePaths = one.SavePaths || two.SavePaths, Save = one.Save != SavingThrowType.Unknown ? one.Save : two.Save };
                fused.Damage.AddRange(one.Damage); fused.Damage.AddRange(two.Damage);
                fused.Gates.AddRange(one.Gates); fused.Gates.AddRange(two.Gates);
                fused.Kills.AddRange(one.Kills); fused.Kills.AddRange(two.Kills);
                if (FusedDeathScans.Count > 1024) FusedDeathScans.Clear();
                FusedDeathScans[key] = fused;
                return fused;
            }
            return ability?.Blueprint != null ? DeathScanOf(ability.Blueprint) : null;
        }

        internal static DeathScan DeathScanOf(BlueprintAbility blueprint)
        {
            if (blueprint == null) return new DeathScan();
            if (DeathScans.TryGetValue(blueprint, out var scan)) return scan;
            scan = new DeathScan();
            try
            {
                foreach (var run in blueprint.GetComponents<AbilityEffectRunAction>())
                {
                    if (run.SavingThrowType != SavingThrowType.Unknown && scan.Save == SavingThrowType.Unknown) scan.Save = run.SavingThrowType;
                    ScanDeath(run.Actions?.Actions, DeathPath.Any, scan, 0);
                }
                var touch = blueprint.StickyTouch?.TouchDeliveryAbility;
                if (touch != null && touch != blueprint)
                    foreach (var run in touch.GetComponents<AbilityEffectRunAction>())
                    {
                        if (run.SavingThrowType != SavingThrowType.Unknown && scan.Save == SavingThrowType.Unknown) scan.Save = run.SavingThrowType;
                        ScanDeath(run.Actions?.Actions, DeathPath.Any, scan, 0);
                    }
            }
            catch (Exception ex)
            {
                // An unreadable tree cannot prove a cast safe: it counts as able to kill (a direct kill on any outcome).
                Main.Verbose($"[Math] permanent-death scan {blueprint.name}: {ex.Message}");
                scan.Kills.Add(DeathPath.Any);
            }
            if (DeathScans.Count > 4096) DeathScans.Clear();
            DeathScans[blueprint] = scan;
            return scan;
        }

        private static void ScanDeath(GameAction[] actions, DeathPath path, DeathScan scan, int depth)
        {
            if (actions == null || depth > 10) return;
            foreach (var action in actions)
            {
                switch (action)
                {
                    case null:
                        continue;
                    // Run on the caster, or on random enemies only (Hideous Laughter's recast): not on an ally.
                    case ContextActionOnContextCaster _:
                        continue;
                    case ContextActionOnRandomTargetsAround random when AbilityProfile.IsEnemyOnlyRecast(random):
                        continue;
                    case ContextActionDealDamage damage:
                        if (IsHitPointDamage(damage)) scan.Damage.Add(new KeyValuePair<DeathPath, ContextActionDealDamage>(path, damage));
                        continue;
                    case ContextActionKill _:
                        scan.Kills.Add(path);
                        continue;
                    case ContextActionApplyBuff apply when apply.Buff != null && !apply.ToCaster:
                        if (AbilityProfile.KillsOnStart(apply.Buff)) scan.Kills.Add(path);
                        continue;
                    case ContextActionConditionalSaved saved:
                        scan.SavePaths = true;
                        ScanDeath(saved.Failed?.Actions, DeathPath.Failed, scan, depth + 1);
                        ScanDeath(saved.Succeed?.Actions, DeathPath.Passed, scan, depth + 1);
                        continue;
                    case Conditional conditional when DustGate(conditional, out int? below):
                        scan.Gates.Add(new KeyValuePair<DeathPath, int?>(path, below));
                        // The dust buff itself is the gate; anything else in its branches is read as usual.
                        ScanDeathExceptDust(conditional.IfTrue?.Actions, path, scan, depth + 1);
                        ScanDeath(conditional.IfFalse?.Actions, path, scan, depth + 1);
                        continue;
                }
                foreach (var child in AbilityProfile.ChildActionLists(action))
                    ScanDeath(child, path, scan, depth + 1);
            }
        }

        private static void ScanDeathExceptDust(GameAction[] actions, DeathPath path, DeathScan scan, int depth)
        {
            if (actions == null) return;
            foreach (var action in actions)
            {
                if (action is ContextActionApplyBuff apply && apply.Buff != null && AbilityProfile.KillsOnStart(apply.Buff)) continue;
                ScanDeath(new[] { action }, path, scan, depth);
            }
        }

        /// <summary>
        /// A dust gate: the HP left compared Less than a value, applying (to the target) a buff that kills on activation
        /// (Disintegrate's f7a6a7d2). <paramref name="below"/> is the compared value, null when it is not a plain number.
        /// </summary>
        internal static bool DustGate(Conditional conditional, out int? below)
        {
            below = null;
            var conditions = conditional?.ConditionsChecker?.Conditions;
            if (conditions == null) return false;
            ContextConditionCompareTargetHP compare = null;
            foreach (var c in conditions)
                if (c is ContextConditionCompareTargetHP hp && !hp.Not) { compare = hp; break; }
            if (compare == null || !AppliesDust(conditional.IfTrue?.Actions)) return false;
            string type = CompareTypeField?.GetValue(compare)?.ToString();
            if (type != "Less") return true;   // another comparison still gates a kill: no number to price it by
            if (compare.Value != null && compare.Value.ValueType == ContextValueType.Simple) below = compare.Value.Value;
            return true;
        }

        private static bool AppliesDust(GameAction[] actions)
        {
            if (actions == null) return false;
            foreach (var action in actions)
                if (action is ContextActionApplyBuff apply && apply.Buff != null && !apply.ToCaster && AbilityProfile.KillsOnStart(apply.Buff))
                    return true;
            return false;
        }

        /// <summary>
        /// The chance <paramref name="ability"/> kills <paramref name="unit"/> for good if it is caught: it is reached (spell
        /// resistance, immunity, the attack roll: EffectChance), then on each outcome of the save a direct kill, or the dust when
        /// the damage before it takes the HP left below its gate. 0 when the cast can kill nobody for good.
        /// </summary>
        internal static float PermanentDeathRisk(AbilityData ability, AbilityProfile profile, UnitEntityData unit)
        {
            if (ability == null || profile == null || unit?.Descriptor?.State == null || unit.Descriptor.State.IsDead) return 0f;
            var scan = DeathScanOf(ability);
            if (scan == null || scan.Empty) return 0f;
            try
            {
                float reach;
                try { reach = EffectChance(ability.Caster?.Unit, unit, ability, profile, damage: true); }
                catch { reach = 1f; }
                if (!(reach > 0f)) return 0f;
                var save = profile.Fusion != null ? profile.Fusion.SavingThrowType : profile.Save != SavingThrowType.Unknown ? profile.Save : scan.Save;
                float hp = HpBeforeDown(unit);
                float risk;
                if (!scan.SavePaths) risk = OutcomeDeath(ability, profile, unit, scan, DeathPath.Any, hp);
                // Native: with no save rolled, ConditionalSaved runs neither branch - only the effects outside it.
                else if (save == SavingThrowType.Unknown) risk = OutcomeDeath(ability, profile, unit, scan, DeathPath.Any, hp, branches: false);
                else
                {
                    float fail = SaveFailChance(ability, save, unit);
                    risk = fail * OutcomeDeath(ability, profile, unit, scan, DeathPath.Failed, hp) +
                           (1f - fail) * OutcomeDeath(ability, profile, unit, scan, DeathPath.Passed, hp);
                }
                return Math.Min(1f, Math.Max(0f, reach * risk));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Math] permanent-death risk {ability.Name} -> {unit.CharacterName}: {ex.Message}");
                return 1f;   // unknown is not safe
            }
        }

        private static bool OnPath(DeathPath effect, DeathPath outcome, bool branches) =>
            effect == DeathPath.Any || branches && effect == outcome;

        private static float OutcomeDeath(AbilityData ability, AbilityProfile profile, UnitEntityData unit, DeathScan scan,
                                          DeathPath outcome, float hp, bool branches = true)
        {
            foreach (var kill in scan.Kills) if (OnPath(kill, outcome, branches)) return 1f;
            int? below = null;
            bool gated = false;
            foreach (var gate in scan.Gates)
            {
                if (!OnPath(gate.Key, outcome, branches)) continue;
                if (!gate.Value.HasValue) return 1f;   // a gate the scan cannot price counts as killing
                below = gated ? Math.Max(below.Value, gate.Value.Value) : gate.Value.Value;
                gated = true;
            }
            if (!gated) return 0f;
            int need = PermanentDeathRules.DamageToTrigger(hp, below.Value);
            if (need <= 0) return 1f;
            var groups = new List<PermanentDeathRules.DiceGroup>(scan.Damage.Count);
            var ctx = DamageContext(ability, profile, unit);
            int cl = Math.Max(1, ParamsOf(ability)?.CasterLevel ?? 1);
            DamageMetamagic(ability, out bool maximize, out bool empower);
            foreach (var part in scan.Damage)
            {
                if (!OnPath(part.Key, outcome, branches)) continue;
                var dmg = part.Value;
                float factor = (empower ? 1.5f : 1f) * (dmg.Half ? 0.5f : 1f);
                // Half on a passed save; evasion is left out, so the risk is never lower than the game's.
                if (dmg.HalfIfSaved && outcome == DeathPath.Passed) factor *= 0.5f;
                if (dmg.ReadPreRolledFromSharedValue && ctx is AbilityExecutionContext rolled)
                {
                    AbilityProfile.SharedValueRange(rolled.Ability, ctx, dmg.PreRolledSharedValue, out _, out _, out int most);
                    groups.Add(new PermanentDeathRules.DiceGroup { Count = 0, Sides = 0, Bonus = most, Factor = factor });
                    continue;
                }
                var value = dmg.Value;
                if (value == null) continue;
                int count = FusedDiceCount(ability, EvaluateValue(value.DiceCountValue, ctx, Math.Min(cl, 10)));
                groups.Add(new PermanentDeathRules.DiceGroup
                {
                    Count = count, Sides = value.DiceType.Sides(), Bonus = EvaluateValue(value.BonusValue, ctx, 0),
                    Maximize = maximize, Factor = factor
                });
            }
            return PermanentDeathRules.ChanceAtLeast(groups, need);
        }

        /// <summary>Whether the cast can kill anyone for good at all (cheap: the cached scan).</summary>
        internal static bool CanKillPermanently(AbilityData ability) => !(DeathScanOf(ability)?.Empty ?? true);
    }
}
