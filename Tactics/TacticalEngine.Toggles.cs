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
using Kingmaker.ElementsSystem;
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
            /// <summary>★ v0.3.19 (class audit B1): each buff the area gives, with the conditions it is given under.</summary>
            public readonly List<AreaGrant> Grants = new List<AreaGrant>();
            public bool AreaAllies, AreaEnemies;
            public bool ForbidsSpells, ForbidsItems;
            public BlueprintUnitFactRefs ForbidIgnore;
            public string Excluded;
        }

        /// <summary>A buff an area gives and the conditions (checker, required result) on the way to it.</summary>
        private sealed class AreaGrant
        {
            public BlueprintBuff Buff;
            public (ConditionsChecker Checker, bool Branch)[] Guards;
            /// <summary>★ v0.3.21: given only when this save fails (Unknown: no save).</summary>
            public Kingmaker.EntitySystem.Stats.SavingThrowType Save = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown;
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
                // ★ v0.3.19 (class audit): Rage and Bloodrage are such toggles (Immediately, DeactivateIfCombatEnded): while
                // on - the default, or the player's choice - the game itself starts them when a fight begins.
                if (!bp.ActivateWithUnitCommand)
                    p.Excluded = bp.ActivateOnCombatStarts ? "the game starts it when a fight begins while it is on (player's choice)"
                                                           : "not activated by a command";
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
                        ReadAreaGrants(area, p.Grants, followSaves: false);
                        foreach (var g in p.Grants)
                            if (!p.AreaBuffs.Contains(g.Buff)) p.AreaBuffs.Add(g.Buff);
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
        /// The buffs an area gives (AbilityAreaEffectBuff, or its enter and every-round actions through Conditionals), each
        /// with the conditions it is given under. ★ v0.3.21: shared by toggles and auras on buffs (TacticalEngine.Auras.cs);
        /// <paramref name="followSaves"/>: also the buffs given on a failed save (ContextActionSavingThrow →
        /// ContextActionConditionalSaved.Failed: Archon's Aura), with that save.
        /// </summary>
        private static void ReadAreaGrants(BlueprintAbilityAreaEffect area, List<AreaGrant> grants, bool followSaves)
        {
            foreach (var areaBuff in area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectBuff>())
                if (areaBuff.Buff != null)
                    grants.Add(new AreaGrant
                    {
                        Buff = areaBuff.Buff,
                        Guards = areaBuff.Condition?.Conditions?.Length > 0
                            ? new[] { (areaBuff.Condition, true) } : new (ConditionsChecker, bool)[0]
                    });
            // ★ v0.3.19 (class audit B1, F1): an area may give its buff by actions - on entering and every round, behind
            // Conditionals (Inspired Rage: only to allies who accept it; Battle Spirit; the Aeon gazes). Before, those areas
            // were "applies nothing the AI can read" and never offered. UnitExit is not read: it undoes the enter actions.
            foreach (var run in area.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction>())
            {
                CollectGrants(run.UnitEnter?.Actions, new List<(ConditionsChecker, bool)>(), grants, 0, followSaves, Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown);
                CollectGrants(run.Round?.Actions, new List<(ConditionsChecker, bool)>(), grants, 0, followSaves, Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown);
            }
        }

        /// <summary>Buffs applied by an area's actions, through Conditionals; the same buff once per branch path.</summary>
        private static void CollectGrants(Kingmaker.ElementsSystem.GameAction[] actions, List<(ConditionsChecker, bool)> guards,
                                          List<AreaGrant> grants, int depth, bool followSaves,
                                          Kingmaker.EntitySystem.Stats.SavingThrowType save)
        {
            if (actions == null || depth > 4) return;
            foreach (var action in actions)
            {
                switch (action)
                {
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff apply when apply.Buff != null && !apply.ToCaster:
                        if (!grants.Any(g => g.Buff == apply.Buff && g.Guards.Length == guards.Count))
                            grants.Add(new AreaGrant { Buff = apply.Buff, Guards = guards.ToArray(), Save = save });
                        break;
                    case Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional conditional:
                        CollectGrants(conditional.IfTrue?.Actions, new List<(ConditionsChecker, bool)>(guards) { (conditional.ConditionsChecker, true) }, grants, depth + 1, followSaves, save);
                        CollectGrants(conditional.IfFalse?.Actions, new List<(ConditionsChecker, bool)>(guards) { (conditional.ConditionsChecker, false) }, grants, depth + 1, followSaves, save);
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSavingThrow saving when followSaves:
                        CollectGrants(saving.Actions?.Actions, guards, grants, depth + 1, followSaves, saving.Type);
                        break;
                    case Kingmaker.UnitLogic.Mechanics.Actions.ContextActionConditionalSaved saved when followSaves:
                        CollectGrants(saved.Failed?.Actions, guards, grants, depth + 1, followSaves, save);
                        break;
                }
            }
        }

        /// <summary>
        /// ★ v0.3.19 (class audit B1, F3): does <paramref name="grant"/> reach <paramref name="unit"/>? 1 yes, 0 no, 0.5 when a
        /// condition cannot be evaluated here. The game's own condition check under the performer's context: an ally who
        /// declined Inspired Rage (its acceptance toggle off) does not count - the AI reasons from that choice, never
        /// switches it.
        /// </summary>
        private static float GrantReaches(AreaGrant grant, Kingmaker.UnitLogic.Mechanics.MechanicsContext context, UnitEntityData unit)
        {
            bool unknown = false;
            foreach (var (checker, branch) in grant.Guards)
            {
                bool? result = AbilityProfile.CheckSafe(checker, context, unit);
                if (!result.HasValue) { unknown = true; continue; }
                if (result.Value != branch) return 0f;
            }
            return unknown ? 0.5f : 1f;
        }

        /// <summary>What a toggle's area gives, for the coverage report ("" when it is not an area).</summary>
        internal static string ToggleReading(ActivatableAbility toggle)
        {
            if (toggle?.Blueprint == null) return "";
            var p = ProfileOf(toggle.Blueprint);
            if (p.Area == null || p.Grants.Count == 0) return "";
            return $" (area {p.Radius:0.#} m gives " +
                   string.Join(", ", p.Grants.Select(g => g.Buff.name + (g.Guards.Length > 0
                       ? " if " + string.Join("+", g.Guards.SelectMany(x => (x.Checker?.Conditions ?? new Condition[0]).Select(c => (x.Branch ? "" : "not ") + c.GetType().Name.Replace("ContextCondition", ""))))
                       : ""))) + ")";
        }

        /// <summary>
        /// Why the AI does not start this toggle by itself (null: it may). For the coverage report and the candidates.
        /// </summary>
        internal static string ToggleExclusion(UnitEntityData unit, ActivatableAbility toggle)
        {
            if (toggle?.Blueprint == null) return "no blueprint";
            if (!(ModSettings.Instance?.AiManagesToggles ?? true)) return "switched off in the settings";
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
        /// ★ v0.3.19 (class audit B1, F2): each recipient's share is the test11 appraisal (AppraiseToggleBuffs: its own
        /// attacks, what comes at it, the stacking increment, ranks with the performer's numbers) over the rounds the
        /// resource and the fight last - the same scale as a spell buff. Effects with no readable part keep the old
        /// formula, at 60 % when the toggle takes a main action (as unread spell buffs). <paramref name="helpfulOnly"/>:
        /// it only helps allies (a buff for the restraint rule).
        /// </summary>
        private static float ToggleValue(TacticalContext ctx, ActivatableAbility toggle, ToggleProfile p, List<Candidate> others,
                                         out string note, out bool helpfulOnly)
        {
            note = "";
            helpfulOnly = false;
            var unit = ctx.Unit;
            int level = Math.Max(1, unit.Descriptor?.Progression?.CharacterLevel ?? 1);
            // A toggle has no spell level: worth per round like a spell of half the character's level (unread effects).
            float perRound = ValueModel.BuffPerRound(Math.Max(1, level / 2));
            int? resource = null;
            try { resource = toggle.ResourceCount; } catch { }
            float rounds = ValueModel.Horizon(resource.HasValue ? Math.Min(ctx.ExpectedRounds, resource.Value) : ctx.ExpectedRounds, 3f);
            // The appraisal's window: the rounds the resource lasts, at most the rest of the fight (ValueModel.UsefulRounds).
            float usefulRounds = ValueModel.UsefulRounds(resource.HasValue ? resource.Value : -1f, ctx.ExpectedRounds, ctx.ExpectedRounds);
            bool mainSlot = ToggleSlot(toggle) != ActionSlot.Free && ToggleSlot(toggle) != ActionSlot.Swift;
            string key = $"tgl:{toggle.Blueprint.AssetGuid}";

            Kingmaker.UnitLogic.Mechanics.MechanicsContext toggleCtx = null;
            try
            {
                toggleCtx = new Kingmaker.UnitLogic.Mechanics.MechanicsContext(unit, unit.Descriptor, (BlueprintScriptableObject)p.SelfBuff ?? toggle.Blueprint.Buff);
                toggleCtx.RecalculateRanks();
            }
            catch { }

            float modelled = 0f, legacy = 0f;
            int helped = 0;
            var details = new List<string>();
            foreach (var ally in ToggleRecipients(ctx, p, enemies: false))
            {
                // What reaches this ally: a self buff, or the area's grants whose conditions hold for it.
                var reaching = new List<(BlueprintBuff Buff, float Weight)>();
                if (p.Area == null) reaching.Add((p.SelfBuff, 1f));
                else
                    foreach (var grant in p.Grants)
                    {
                        float w = GrantReaches(grant, toggleCtx, ally);
                        if (w > 0f) reaching.Add((grant.Buff, w));
                    }
                // ★ v0.3.21 (audit review): harmful by what it does too (AbilityProfile.IsHarmfulBuff), not only the flag.
                var useful = reaching.Where(r => r.Buff != null && !AbilityProfile.IsHarmfulBuff(r.Buff) && !ally.Buffs.HasFact(r.Buff) &&
                                                 BuffStackingAnalyzer.Analyze(r.Buff, ally).IsEffective).ToList();
                if (useful.Count == 0) continue;
                float weight = useful.Max(r => r.Weight);
                var appraisal = toggleCtx != null
                    ? AppraiseToggleBuffs(ctx, toggleCtx, key, useful.Select(r => r.Buff).Distinct().ToList(), ally, usefulRounds)
                    : new BuffAppraisal();
                if (appraisal.Modelled)
                {
                    modelled += appraisal.Value * weight;
                    if (appraisal.Value > 0f) helped++;
                    if (details.Count < 3) details.Add($"{ally.CharacterName} {appraisal.Value * weight:0.#} ({appraisal.Detail})");
                    continue;
                }
                float best = 0f;
                foreach (var r in useful)
                {
                    var analysis = BuffEffectAnalyzer.Analyze(r.Buff);
                    float q = analysis.Recognized ? analysis.CombatValue : analysis.OnlyUnrecognized ? 0.3f : 0f;
                    best = Math.Max(best, q * r.Weight);
                }
                if (best <= 0f) continue;
                bool fighting = (ally.CombatState?.IsEngaged ?? false) || ally == unit;
                legacy += perRound * rounds * best * (fighting ? 1f : 0.6f) * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);
                helped++;
                if (details.Count < 3) details.Add($"{ally.CharacterName}: effect not read (quality {best:0.##})");
            }
            // The appraisal already carries the role weight (AppraiseParts); the old formula gets it here.
            legacy *= RoleWeight(ctx, "buff") * (mainSlot ? 0.6f : 1f);
            float value = modelled + legacy;
            // A harmful area on enemies (a dirge): like a debuff, a quarter of each enemy's worth per round.
            int hindered = 0;
            foreach (var enemy in ToggleRecipients(ctx, p, enemies: true))
            {
                bool reaches = p.Area == null
                    ? p.SelfBuff != null && AbilityProfile.IsHarmfulBuff(p.SelfBuff)
                    // ★ v0.3.21 (audit review): Dirge of Doom's Shaken buff carries no harmful flag - it was never valued.
                    : p.Grants.Any(g => HarmfulGrant(g) && !enemy.Buffs.HasFact(g.Buff) && !CombatMath.BuffImmune(enemy, g.Buff, null) &&
                                        GrantReaches(g, toggleCtx, enemy) > 0f);
                if (!reaches) continue;
                value += 0.25f * CombatMath.ControlWorth(enemy) * rounds * RoleWeight(ctx, "buff");
                hindered++;
            }
            if (value <= 0f) return 0f;
            helpfulOnly = helped > 0 && hindered == 0;
            // A resource spent per round is a daily resource, like a spell slot: worth less in easy fights.
            if (resource.HasValue) value *= Mathf.Clamp(0.55f + 0.45f * ctx.Difficulty, 0.55f, 1f);

            // What it forbids while on: casting (and items) - the best such option the unit has now, for those rounds.
            string forbidNote = "";
            if (p.ForbidsSpells && !(p.ForbidIgnore?.Feature != null && unit.Descriptor.HasFact(p.ForbidIgnore.Feature)))
            {
                float bestSpell = others.Where(c => c.Kind == CandidateKind.Ability && c.Ability != null &&
                                                    (c.Ability.Blueprint.IsSpell || (p.ForbidsItems && c.Ability.SourceItem != null)))
                                        .Select(c => c.Value).DefaultIfEmpty(0f).Max();
                float penalty = bestSpell * rounds * 0.5f;
                value -= penalty;
                if (penalty > 0f) forbidNote = $", forbids casting (-{penalty:F1})";
            }
            note = (helped > 0 ? $"{helped} {(p.Area == null ? "self" : "in range")}" : "") +
                   (hindered > 0 ? $"{(helped > 0 ? ", " : "")}{hindered} enemies" : "") +
                   (resource.HasValue ? $", {resource.Value} left" : "") + forbidNote;
            NoteToggleAppraisal(unit, key, $"{Math.Max(0f, value):0.#}: over {usefulRounds:0.#} rd" +
                                           (details.Count > 0 ? " - " + string.Join("; ", details) : "") + forbidNote);
            return Math.Max(0f, value);
        }

        private static void AddToggleCandidates(TacticalContext ctx, List<Candidate> list, bool preview)
        {
            if (!(ModSettings.Instance?.AiManagesToggles ?? true)) return;
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
                float value = ToggleValue(ctx, toggle, p, others, out string note, out bool helpfulOnly);
                if (value <= 0f) continue;
                list.Add(new Candidate
                {
                    Kind = CandidateKind.Toggle,
                    Slot = ToggleSlot(toggle),
                    Toggle = toggle,
                    Value = value,
                    // ★ v0.3.19 (class audit B1): a toggle that only helps allies is a buff for the restraint rule (Decide).
                    PureBuff = helpfulOnly,
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
            // ★ v0.3.19 (class audit B1): an ally counts only when a helpful grant reaches it (its conditions: acceptance).
            Kingmaker.UnitLogic.Mechanics.MechanicsContext toggleCtx = null;
            try { toggleCtx = new Kingmaker.UnitLogic.Mechanics.MechanicsContext(unit, unit.Descriptor, toggle.Blueprint.Buff); }
            catch { }
            bool anyone = ToggleRecipients(ctx, p, enemies: false)
                              .Any(a => a != unit && p.Grants.Any(g => !HarmfulGrant(g) && GrantReaches(g, toggleCtx, a) > 0f)) ||
                          ToggleRecipients(ctx, p, enemies: true)
                              .Any(e => p.Grants.Any(g => HarmfulGrant(g) && GrantReaches(g, toggleCtx, e) > 0f));
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
            // ★ v0.3.19 (test6): an infusion switched off (TacticalEngine.Kinetic.cs) - at once, as the action bar does.
            if (c.ToggleOff)
            {
                try
                {
                    bool wasOn = toggle.IsOn;
                    toggle.IsOn = false;
                    if (toggle.IsOn) { failure = "the game did not switch it off"; return false; }
                    CombatMemory.RecordToggle(unit, toggle, false, wasOn);
                    Main.Log($"[Toggle] {unit.CharacterName}: {toggle.Name} off - {c.Label} = {c.Value:F1}");
                    return true;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    return false;
                }
            }
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
