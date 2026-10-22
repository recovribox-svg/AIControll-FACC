// ★ v0.3.18 (docs/ACTIVE_ABILITY_PLAN.md AA-2, design E1 (a), E3): activatable abilities whose activation is a game
// command - bardic and Azata performances, the Rage family, shaman spirits (Battle Spirit), judgments - as candidates
// that compete with attacks and spells for the action they cost.
//
//   - Which: ActivateWithUnitCommand, on the action bar, off, available (the game's resource and restriction checks).
//     Not: attack modes and movement modifiers (AA-3: Mobility's AcrobaticMovement), mounting (ShareFeaturesWithPet),
//     kineticist infusions and Gather Power (modifiers of a blast), toggles that need a target, preferences (they are
//     not command-activated), and a toggle whose group already has one on (switching would replace it).
//   - Slot: UnitActivateAbility.GetCommandType, the game's own rule (Quicken Performance included).
//   - Value: the buff it puts on its recipients, valued like any buff (BuffEffectAnalyzer, stacking): the caster for a
//     self buff (Rage), the allies in its area for a performance (the area's AbilityAreaEffectBuff), enemies in it for
//     a harmful one; over the rounds the resource lasts, discounted in easy fights like spell slots; minus what it
//     forbids (ForbidSpellCasting: the best spell the caster could cast instead, for the rounds it lasts).
//   - Issued as the player's action bar does it: IsOn = true, and the game queues its UnitActivateAbility (or starts
//     it at once). Not through the dispatcher: there is no path to give it.
//   - Stopped by the AI only when it started it and nobody is left in its area (a performance with every ally out of
//     range spends a round of its resource for nothing). Rage and the rest end by the game's own rules.
//   - At most one switch per toggle and round (CombatMemory), no thrashing.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>What a toggle's buff does, read once per blueprint.</summary>
        private sealed class ToggleProfile
        {
            public BlueprintBuff SelfBuff;
            public BlueprintAbilityAreaEffect Area;
            public float Radius;
            public readonly List<BlueprintBuff> AreaBuffs = new List<BlueprintBuff>();
            public bool AreaAllies, AreaEnemies;
            public bool ForbidsSpells, ForbidsItems;
            public BlueprintUnitFactRefs ForbidIgnore;
            public string Excluded;
        }

        private sealed class BlueprintUnitFactRefs
        {
            public Kingmaker.Blueprints.Classes.BlueprintFeature Feature;
        }

        private static readonly Dictionary<BlueprintActivatableAbility, ToggleProfile> ToggleProfiles =
            new Dictionary<BlueprintActivatableAbility, ToggleProfile>();

        private static readonly ActivatableAbilityGroup[] ModifierGroups =
        {
            ActivatableAbilityGroup.FormInfusion, ActivatableAbilityGroup.SubstanceInfusion, ActivatableAbilityGroup.GatherPower
        };

        private static ToggleProfile ProfileOf(BlueprintActivatableAbility bp)
        {
            if (ToggleProfiles.TryGetValue(bp, out var cached)) return cached;
            var p = new ToggleProfile();
            try
            {
                var buff = bp.Buff;
                if (!bp.ActivateWithUnitCommand) p.Excluded = "not activated by a command";
                // ★ v0.3.18 (review): ActivatableAbilityUnitCommand spends that action every round, and the game switches
                // the toggle off when the unit runs another command of that type: the AI's next attack or swift ability
                // would end it, and it would start it again next round.
                else if (bp.GetComponent<ActivatableAbilityUnitCommand>() is ActivatableAbilityUnitCommand perRound)
                    p.Excluded = $"spends a {perRound.Type} action every round";
                else if (bp.IsTargeted) p.Excluded = "needs a target";
                else if (ModifierGroups.Contains(bp.Group)) p.Excluded = "a modifier of another action (AA-4)";
                else if (buff == null) p.Excluded = "no buff";
                else if (buff.GetComponent<Kingmaker.UnitLogic.FactLogic.AcrobaticMovement>() != null) p.Excluded = "movement modifier (AA-3)";
                else if (buff.GetComponent<Kingmaker.Designers.Mechanics.Facts.ShareFeaturesWithPet>() != null) p.Excluded = "mounting";
                if (buff != null)
                {
                    var forbid = buff.GetComponent<Kingmaker.UnitLogic.FactLogic.ForbidSpellCasting>();
                    if (forbid != null)
                    {
                        p.ForbidsSpells = true;
                        p.ForbidsItems = forbid.ForbidMagicItems;
                        try { p.ForbidIgnore = new BlueprintUnitFactRefs { Feature = forbid.IgnoreFeature }; } catch { }
                    }
                    var areaComponent = buff.GetComponent<Kingmaker.UnitLogic.Buffs.Components.AddAreaEffect>();
                    var area = areaComponent?.AreaEffect;
                    if (area != null)
                    {
                        p.Area = area;
                        p.Radius = area.Size.Meters;
                        p.AreaAllies = area.CanTargetAllies;
                        p.AreaEnemies = area.CanTargetEnemies;
                        foreach (var areaBuff in area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff>())
                            if (areaBuff.Buff != null) p.AreaBuffs.Add(areaBuff.Buff);
                        if (p.AreaBuffs.Count == 0 && p.Excluded == null) p.Excluded = "its area applies nothing the AI can read";
                    }
                    else p.SelfBuff = buff;
                }
            }
            catch (Exception ex)
            {
                p.Excluded = "unreadable: " + ex.Message;
            }
            ToggleProfiles[bp] = p;
            return p;
        }

        /// <summary>
        /// Why the AI does not start this toggle by itself (null: it may). For the coverage report and the candidates.
        /// </summary>
        internal static string ToggleExclusion(UnitEntityData unit, ActivatableAbility toggle)
        {
            if (toggle?.Blueprint == null) return "no blueprint";
            if (!(ModSettings.Instance?.AiStartsToggles ?? true)) return "switched off in the settings";
            if (toggle.Blueprint.HiddenInUI) return "hidden";
            return ProfileOf(toggle.Blueprint).Excluded;
        }

        private static ActionSlot ToggleSlot(ActivatableAbility toggle)
        {
            switch (UnitActivateAbility.GetCommandType(toggle))
            {
                case UnitCommand.CommandType.Free: return ActionSlot.Free;
                case UnitCommand.CommandType.Swift: return ActionSlot.Swift;
                case UnitCommand.CommandType.Move: return ActionSlot.Move;
                default: return ActionSlot.Standard;
            }
        }

        /// <summary>Allies (or enemies) the toggle's area covers around the unit now.</summary>
        private static List<UnitEntityData> ToggleRecipients(TacticalContext ctx, ToggleProfile p, bool enemies)
        {
            var units = enemies ? ctx.Enemies : ctx.Allies;
            if (p.Area == null) return enemies ? new List<UnitEntityData>() : new List<UnitEntityData> { ctx.Unit };
            if (enemies ? !p.AreaEnemies : !p.AreaAllies) return new List<UnitEntityData>();
            return UnitsAround(units, ctx.Unit.Position, p.Radius);
        }

        /// <summary>
        /// The toggle's worth if switched on now (damage-equivalent points), and a short note for the label. 0 when it
        /// gives nothing (every recipient already has its buff, nobody in range, or what it forbids is worth more).
        /// </summary>
        private static float ToggleValue(TacticalContext ctx, ActivatableAbility toggle, ToggleProfile p, List<Candidate> others,
                                         out string note)
        {
            note = "";
            var unit = ctx.Unit;
            int level = Math.Max(1, unit.Descriptor?.Progression?.CharacterLevel ?? 1);
            // A toggle has no spell level: worth per round like a spell of half the character's level.
            float perRound = ValueModel.BuffPerRound(Math.Max(1, level / 2));
            int? resource = null;
            try { resource = toggle.ResourceCount; } catch { }
            float rounds = ValueModel.Horizon(resource.HasValue ? Math.Min(ctx.ExpectedRounds, resource.Value) : ctx.ExpectedRounds, 3f);

            float value = 0f;
            int helped = 0;
            var buffs = p.Area == null ? new List<BlueprintBuff> { p.SelfBuff } : p.AreaBuffs;
            foreach (var ally in ToggleRecipients(ctx, p, enemies: false))
            {
                float best = 0f;
                foreach (var buff in buffs)
                {
                    if (buff == null || buff.Harmful || ally.Buffs.HasFact(buff)) continue;
                    if (!BuffStackingAnalyzer.Analyze(buff, ally).IsEffective) continue;
                    var analysis = BuffEffectAnalyzer.Analyze(buff);
                    float q = analysis.Recognized ? analysis.CombatValue : analysis.OnlyUnrecognized ? 0.3f : 0f;
                    best = Math.Max(best, q);
                }
                if (best <= 0f) continue;
                bool fighting = (ally.CombatState?.IsEngaged ?? false) || ally == unit;
                value += perRound * rounds * best * (fighting ? 1f : 0.6f) * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);
                helped++;
            }
            // A harmful area on enemies (a dirge): like a debuff, a quarter of each enemy's worth per round.
            int hindered = 0;
            foreach (var enemy in ToggleRecipients(ctx, p, enemies: true))
            {
                if (!buffs.Any(b => b != null && b.Harmful && !enemy.Buffs.HasFact(b) && !CombatMath.BuffImmune(enemy, b, null))) continue;
                value += 0.25f * CombatMath.Threat(enemy) * rounds;
                hindered++;
            }
            if (value <= 0f) return 0f;
            value *= RoleWeight(ctx, "buff");
            // A resource spent per round is a daily resource, like a spell slot: worth less in easy fights.
            if (resource.HasValue) value *= Mathf.Clamp(0.55f + 0.45f * ctx.Difficulty, 0.55f, 1f);

            // What it forbids while on: casting (and items) - the best such option the unit has now, for those rounds.
            if (p.ForbidsSpells && !(p.ForbidIgnore?.Feature != null && unit.Descriptor.HasFact(p.ForbidIgnore.Feature)))
            {
                float bestSpell = others.Where(c => c.Kind == CandidateKind.Ability && c.Ability != null &&
                                                    (c.Ability.Blueprint.IsSpell || (p.ForbidsItems && c.Ability.SourceItem != null)))
                                        .Select(c => c.Value).DefaultIfEmpty(0f).Max();
                float penalty = bestSpell * rounds * 0.5f;
                value -= penalty;
                if (penalty > 0f) note += $", forbids casting (-{penalty:F1})";
            }
            note = (helped > 0 ? $"{helped} {(p.Area == null ? "self" : "in range")}" : "") +
                   (hindered > 0 ? $"{(helped > 0 ? ", " : "")}{hindered} enemies" : "") +
                   (resource.HasValue ? $", {resource.Value} left" : "") + note;
            return Math.Max(0f, value);
        }

        private static void AddToggleCandidates(TacticalContext ctx, List<Candidate> list, bool preview)
        {
            if (!(ModSettings.Instance?.AiStartsToggles ?? true)) return;
            var unit = ctx.Unit;
            var toggles = unit.ActivatableAbilities?.Enumerable?.Where(t => t?.Blueprint != null).ToList();
            if (toggles == null || toggles.Count == 0) return;
            var others = list.ToList();
            foreach (var toggle in toggles)
            {
                if (ToggleExclusion(unit, toggle) != null) continue;
                var p = ProfileOf(toggle.Blueprint);
                if (CombatMemory.ToggleSwitchedThisRound(unit, toggle)) continue;
                // ★ v0.3.19: switched by the player during this fight.
                if (CombatMemory.PlayerOwnsToggle(unit, toggle)) continue;
                if (toggle.IsOn || toggle.IsStarted)
                {
                    if (!preview) StopIfPointless(ctx, toggle, p);
                    continue;
                }
                if (!toggle.IsAvailable) continue;
                // The game's group rule (ActivatableAbility.TryStart): the weights of the group's toggles that are on,
                // this one included, may not exceed the group's size. Otherwise it would not start, or replace another.
                if (toggle.Blueprint.Group != ActivatableAbilityGroup.None)
                {
                    int weight = toggle.Blueprint.WeightInGroup + toggles.Where(t => t != toggle && t.Blueprint.Group == toggle.Blueprint.Group &&
                                                                                    (t.IsOn || t.IsStarted)).Sum(t => t.Blueprint.WeightInGroup);
                    int size = unit.Descriptor.Get<Kingmaker.UnitLogic.Parts.UnitPartActivatableAbility>()?.GetGroupSize(toggle.Blueprint.Group) ?? 1;
                    if (weight > size) continue;
                }
                float value = ToggleValue(ctx, toggle, p, others, out string note);
                if (value <= 0f) continue;
                list.Add(new Candidate
                {
                    Kind = CandidateKind.Toggle,
                    Slot = ToggleSlot(toggle),
                    Toggle = toggle,
                    Value = value,
                    Label = $"Start {toggle.Name} ({note})",
                    Key = $"tgl:{toggle.Blueprint.AssetGuid}"
                });
            }
        }

        /// <summary>
        /// A performance whose area covers no ally any more, nor an enemy it hinders: switched off (free), so its resource
        /// is not spent for nothing. Self buffs (Rage) are left alone. ★ v0.3.19: whoever started it, unless the player
        /// switched it during this fight.
        /// </summary>
        private static void StopIfPointless(TacticalContext ctx, ActivatableAbility toggle, ToggleProfile p)
        {
            var unit = ctx.Unit;
            // ★ v0.3.19: the player's own pre-fight choice too (the resource is spent for nothing either way); the caller
            // skips a toggle the player switched during this fight.
            if (p.Area == null) return;
            int? resource = null;
            try { resource = toggle.ResourceCount; } catch { }
            if (!resource.HasValue) return;   // costs nothing to keep
            // ★ v0.3.18 (review): the performer itself does not count - it is always in its own area, so the rule never
            // fired for a helpful performance and the bard spent its rounds on itself after every ally had left.
            bool anyone = ToggleRecipients(ctx, p, enemies: false).Any(a => a != unit && p.AreaBuffs.Any(b => !b.Harmful)) ||
                          ToggleRecipients(ctx, p, enemies: true).Count > 0;
            if (anyone) return;
            try
            {
                bool wasOn = toggle.IsOn;
                toggle.IsOn = false;
                CombatMemory.RecordToggle(unit, toggle, false, wasOn);
                Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} off - nobody left in its area ({resource.Value} rounds left)");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Toggle] {unit.CharacterName}: {toggle.Name} off failed: {ex.Message}");
            }
        }

        /// <summary>Switch the toggle on the way the action bar does; the game queues its activation command.</summary>
        private static bool ExecuteToggle(UnitEntityData unit, Candidate c, out UnitCommand command, out string failure)
        {
            command = null;
            failure = null;
            var toggle = c.Toggle;
            if (toggle == null) { failure = "no toggle"; return false; }
            try
            {
                toggle.IsOn = true;
                if (!toggle.IsOn) { failure = "the game did not switch it on"; return false; }
                command = unit.Commands.Raw.FirstOrDefault(x => x is UnitActivateAbility a && a.Ability == toggle);
                CombatMemory.RecordToggle(unit, toggle, true, false);
                CastOutcome.Forget(unit);
                Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} on - {c.Label} = {c.Value:F1}" +
                         (command == null ? " (started without a command)" : ""));
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }
        }
    }
}
