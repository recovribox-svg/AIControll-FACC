// ★ v0.3.23 (Carnage task, docs/TASK_BOMBS_CARNAGE_QUIVER.md §2): Dreadful Carnage's free Dazzling Display.
//
//   The feat (vanilla, unchanged by Tabletop Tweaks Base 2.6.18) is an AddOutgoingDamageTrigger with
//   TargetKilledByThisDamage and no weapon, ability, descriptor or energy check: any damage of its owner that takes a living,
//   non-regenerating target to 0 HP runs ContextActionOnContextCaster → ContextActionCastSpell(DazzlingDisplayAction) - cast
//   by the owner at no action cost (no full-round or standard flag, only CanTarget is checked, so no Weapon Focus weapon is
//   needed): a Demoralize of every enemy within 30 ft of the owner with the owner's own Intimidate check. No attack option
//   counted it, so a full-round manual Display could outscore a full attack that would most likely kill and fire the same
//   Display for nothing ("the AI loves to spam Dazzling Display ... when a character has Dreadful Carnage too").
//
//   The trigger is recognised by that shape (cached per feature blueprint), never by name. Each weapon-attack option (full
//   attack, single attack, Move+Attack) gains: its kill chance on its target (ValueModel.KillChance, as its own damage value
//   uses; none against a regenerating target, as the native check) x the free Display's worth on the other enemies within its
//   radius of where the attack is made (ValueModel.KillTriggerCredit). That worth per enemy is the manual Display's own terms
//   - the Intimidate odds, the enemy's worth, Shaken's share, the rounds, the Demoralize upgrades - computed once per decision
//   for the owner only; none for an enemy already shaken or immune. Credited once per option, never per swing. Spells, bombs
//   and other ability damage trigger it too but are not credited (they do not compete with the Display in the report).
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private sealed class KillDisplayShape
        {
            public BlueprintAbility Spell;
            public bool CheckWeapon;
            public BlueprintWeaponType WeaponType;
            public float Radius;
        }

        /// <summary>The free Display a kill fires for this unit in this decision, and its worth on each enemy.</summary>
        private sealed class KillDisplay
        {
            public KillDisplayShape Shape;
            public readonly Dictionary<UnitEntityData, float> Worth = new Dictionary<UnitEntityData, float>();
            public string Name;
        }

        /// <summary>Feature blueprint → its kill-fired Demoralize area, or null (cached: blueprints do not change).</summary>
        private static readonly Dictionary<BlueprintFeature, KillDisplayShape> s_KillDisplayShapes = new Dictionary<BlueprintFeature, KillDisplayShape>();

        private static KillDisplayShape ShapeOf(BlueprintFeature feature)
        {
            if (feature == null) return null;
            if (s_KillDisplayShapes.TryGetValue(feature, out var cached)) return cached;
            KillDisplayShape shape = null;
            try
            {
                foreach (var t in feature.ComponentsArray?.OfType<AddOutgoingDamageTrigger>() ?? Enumerable.Empty<AddOutgoingDamageTrigger>())
                {
                    if (!t.TargetKilledByThisDamage || t.CheckAbilityType || t.CheckSpellDescriptor || t.CheckSpellParent ||
                        t.CheckEnergyDamageType || t.CheckDamageDealt) continue;
                    var actions = t.Actions?.Actions;
                    if (actions == null || actions.Length != 1 || !(actions[0] is ContextActionOnContextCaster onCaster)) continue;
                    var inner = onCaster.Actions?.Actions;
                    if (inner == null || inner.Length != 1 || !(inner[0] is ContextActionCastSpell cast) || cast.CastByTarget) continue;
                    var spell = cast.Spell;
                    var around = spell?.GetComponent<AbilityTargetsAround>();
                    var run = spell?.GetComponent<AbilityEffectRunAction>();
                    if (around == null || run?.Actions?.Actions == null || !run.Actions.Actions.OfType<Demoralize>().Any()) continue;
                    shape = new KillDisplayShape
                    {
                        Spell = spell, CheckWeapon = t.CheckWeaponType, WeaponType = t.CheckWeaponType ? t.WeaponType : null,
                        Radius = around.AoERadius.Meters
                    };
                    break;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Carnage] {feature.name}: {ex.Message}");
            }
            s_KillDisplayShapes[feature] = shape;
            return shape;
        }

        /// <summary>The unit's kill-fired Display now, or null (most units: one cached lookup per feature).</summary>
        private static KillDisplay KillDisplayFor(TacticalContext ctx)
        {
            var unit = ctx.Unit;
            KillDisplayShape shape = null;
            try
            {
                foreach (var feature in unit.Descriptor.Progression.Features.Enumerable)
                    if ((shape = ShapeOf(feature?.Blueprint)) != null) break;
            }
            catch { return null; }
            if (shape == null) return null;
            try
            {
                var display = new AbilityData(shape.Spell, unit.Descriptor);   // as ContextActionCastSpell builds it
                var profile = AbilityProfile.Get(display);
                if (profile?.Intimidation == null) return null;
                var result = new KillDisplay { Shape = shape, Name = display.Name };
                foreach (var enemy in ctx.Enemies)
                {
                    float worth = FreeDemoralizeWorth(ctx, display, profile, enemy);
                    if (worth > 0f) result.Worth[enemy] = worth;
                }
                return result;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Carnage] {unit.CharacterName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// What the fired Display does to <paramref name="enemy"/>: the manual Display's terms in ScoreAbility (the
        /// Intimidate check's odds and duration, worth x Shaken's share x rounds, the Demoralize upgrades, x0.4 on an enemy
        /// that cannot act) without its repeat factor (a proc is not a repeated manual use). 0 when the enemy is already
        /// shaken (the game would lengthen it: not counted), immune, or outside the area's own filter.
        /// </summary>
        private static float FreeDemoralizeWorth(TacticalContext ctx, AbilityData display, AbilityProfile profile, UnitEntityData enemy)
        {
            var unit = ctx.Unit;
            var buffs = profile.TargetBuffs.Where(b => profile.BuffAppliesTo(b, display, enemy) != EffectApplies.No &&
                                                       !CombatMath.BuffImmune(enemy, b, display)).ToList();
            if (!buffs.Contains(profile.Intimidation.Buff) || buffs.Any(b => enemy.Buffs.HasFact(b))) return 0f;
            if (profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(enemy, profile.Control)) return 0f;
            if (!profile.AreaReaches(display, enemy)) return 0f;
            float p = CombatMath.EffectChance(unit, enemy, display, profile, damage: false);
            CombatMath.IntimidateOdds(unit, enemy, profile.Intimidation, out float intimidated, out float own);
            p *= intimidated;
            if (p <= 0f) return 0f;
            float rounds = Math.Min(Math.Min(ctx.ExpectedRounds, 3f), Math.Max(1f, own));
            float share = ControlShare(profile.ControlStrength, false, false);
            float worth = CombatMath.ControlWorth(enemy);
            float value = p * worth * share * rounds + p * worth * DemoralizeUpgrades(ctx, display, profile.Intimidation, enemy, share, rounds);
            if (CombatMath.IsDisabled(enemy)) value *= 0.4f;
            return Math.Max(0f, value);
        }

        /// <summary>
        /// The kill-fired Display's worth on an attack of <paramref name="est"/> on <paramref name="target"/> made from
        /// <paramref name="from"/> (ValueModel.KillTriggerCredit), with its decision-log tag.
        /// </summary>
        private static float KillDisplayCredit(KillDisplay killDisplay, UnitEntityData target, WeaponAttackEstimate est, Vector3 from,
                                               out string tag)
        {
            tag = "";
            if (killDisplay == null || target == null || est == null || est.AnyHitChance <= 0f || est.ExpectedDamage <= 0f) return 0f;
            try
            {
                if (killDisplay.Shape.CheckWeapon && est.Weapon?.Blueprint?.Type != killDisplay.Shape.WeaponType) return 0f;
                // TargetKilledByThisDamage: the target was alive and is at 0 HP or below, not regenerating.
                if (CombatMath.IsDisabled(target) || RegenerationRuns(target) || killDisplay.Worth.Count == 0) return 0f;
                float kill = ValueModel.KillChance(est.AnyHitChance, est.ExpectedDamage / est.AnyHitChance, Math.Max(1, target.HPLeft));
                var distances = new List<float>(killDisplay.Worth.Count);
                var worths = new List<float>(killDisplay.Worth.Count);
                int killed = -1;
                foreach (var pair in killDisplay.Worth)
                {
                    if (pair.Key == target) killed = distances.Count;
                    distances.Add(Vector3.Distance(from, pair.Key.Position) - pair.Key.Corpulence);
                    worths.Add(pair.Value);
                }
                float credit = ValueModel.KillTriggerCredit(kill, distances, worths, killed, killDisplay.Shape.Radius);
                if (credit >= 0.05f) tag = $" [{killDisplay.Name} on a kill {kill:P0}: +{credit:0.#}]";
                return credit;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Carnage] {target.CharacterName}: {ex.Message}");
                return 0f;
            }
        }
    }
}
