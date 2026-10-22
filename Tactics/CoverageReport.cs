// ★ v0.3.17: what the AI can and cannot do with each character's abilities - diagnostics only (docs/ACTIVE_ABILITY_PLAN.md,
// step AA-0). Nothing here changes a decision.
//
// Once per character per game session, and again when the character's abilities change (level up, new items), at the
// character's first decision of a fight:
//   [Coverage] abilities: the action-bar abilities the engine considers, grouped by what it makes of them - understood
//              (with the effect kinds it reads), not available now (resources, restrictions), blacklisted, held back by
//              the item settings, or effect not understood (with the blueprint's component types, for the next step).
//   [Coverage] toggle: every activatable ability with what the game says about it - on/started, activation type and
//              the action it costs (UnitActivateAbility.GetCommandType, the game's own rule), group, combat-end and
//              default flags, resource spending and what is left, and the components of its buff. The AI switches only
//              command-activated toggles (AA-2, TacticalEngine.Toggles.cs); these lines answer, from the player's own game and mods, what the plan needs to know
//              (when Fighting Defensively's AC applies, the default of the Inspire Rage "accept" toggle, rage
//              restrictions). ★ v0.3.18 (AA-2): and whether the AI may start it itself (command-activated toggles).
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Commands;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class CoverageReport
    {
        /// <summary>Per character: the ability set last reported (its hash), so a changed set is reported again.</summary>
        private static readonly Dictionary<string, int> Reported = new Dictionary<string, int>();
        /// <summary>Characters already checked in this fight (the check itself gathers the abilities once).</summary>
        private static readonly HashSet<string> CheckedThisFight = new HashSet<string>();

        /// <summary>The party's fight started: check each character again at its first decision.</summary>
        public static void NewFight() => CheckedThisFight.Clear();

        /// <summary>Called at a character's decision; reports when its ability set is new or changed.</summary>
        public static void ReportIfChanged(UnitEntityData unit)
        {
            if (unit == null || !CheckedThisFight.Add(unit.UniqueId)) return;
            try
            {
                var abilities = AbilityDiscovery.Gather(unit);
                var toggles = unit.ActivatableAbilities?.Enumerable?.Where(t => t?.Blueprint != null).ToList()
                              ?? new List<ActivatableAbility>();
                int hash = 17;
                foreach (var a in abilities) hash = hash * 31 + AbilityDiscovery.Key(a).GetHashCode();
                foreach (var t in toggles) hash = hash * 31 + t.Blueprint.AssetGuid.GetHashCode();
                if (Reported.TryGetValue(unit.UniqueId, out int last) && last == hash) return;
                Reported[unit.UniqueId] = hash;
                ReportAbilities(unit, abilities);
                foreach (var t in toggles) ReportToggle(unit, t);
                ReportKineticist(unit, abilities);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Coverage] {unit.CharacterName}: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.3.19 (test5): a kineticist's state as the game holds it (UnitPartKineticist) and each blast's burn
        /// (AbilityKineticist.CalculateAbilityBurnCost, with Gather Power and infusions), so a log shows what the AI
        /// saw: archer blasts through the bow, the kinetic blade, the gather mode, Elemental Overflow's damage now.
        /// </summary>
        private static void ReportKineticist(UnitEntityData unit, List<AbilityData> abilities)
        {
            var part = unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>();
            if (part == null) return;
            var blasts = new List<string>();
            foreach (var ability in abilities)
            {
                var cost = Kingmaker.UnitLogic.Class.Kineticist.AbilityKineticist.CalculateAbilityBurnCost(ability);
                if (!cost.HasValue) continue;
                int overflow = CombatMath.ElementalOverflowDamage(unit, ability.Blueprint);
                blasts.Add($"{ability.Name} burn {cost.Value.Total}" + (overflow > 0 ? $" overflow +{overflow}" : "") +
                           (ability.IsAvailable ? "" : " (not available now)"));
            }
            var weapon = unit.Body?.PrimaryHand?.MaybeWeapon;
            var bladeComponent = weapon == null ? null
                : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<Kingmaker.UnitLogic.Class.Kineticist.WeaponKineticBlade>(weapon.Blueprint);
            bool blade = bladeComponent != null;
            string activation = bladeComponent?.GetActivationAbility(unit.Descriptor)?.RuntimeActionType.ToString();
            string gather = part.GatherPowerAbility?.Data?.RuntimeActionType.ToString();
            Main.Log($"[Coverage] {unit.CharacterName} kineticist: level {part.ClassLevel}, burn {part.AcceptedBurn}/{part.MaxBurn} " +
                     $"(this round {part.LeftBurnThisRound} of {part.MaxBurnPerRound} left), archer blasts {part.CanUseArcherBlast()}, " +
                     $"weapon {(weapon?.Name ?? "none")}{(blade ? $" (kinetic blade, activation {activation ?? "?"}, activated {part.IsBladeActivated})" : "")}, " +
                     $"gather mode {part.GatherPowerMode} (gather action {gather ?? "none"}); " +
                     $"{blasts.Count} burn abilities: {string.Join("; ", blasts)}");
        }

        private static void ReportAbilities(UnitEntityData unit, List<AbilityData> abilities)
        {
            var understood = new List<string>();
            var unavailable = new List<string>();
            var blacklisted = new List<string>();
            var itemPolicy = new List<string>();
            var unread = new List<string>();
            foreach (var ability in abilities)
            {
                string name = ability.Name + AbilityDiscovery.MetamagicTag(ability);
                if (!ConsumablePolicy.IsAllowed(ability)) { itemPolicy.Add(name); continue; }
                if (AbilityClassifier.IsBlacklistedForCombat(ability, valuedByEffect: true)) { blacklisted.Add(name); continue; }
                var profile = AbilityProfile.Get(ability);
                // Charge and Coup de Grace have no effect components; the engine values them by the game's rules.
                if (profile != null && (profile.IsCharge || TacticalEngine.IsCoupDeGrace(ability)))
                {
                    if (ability.IsAvailable) understood.Add($"{name} ({(profile.IsCharge ? "charge" : "coup de grace")}, by the rules)");
                    else unavailable.Add(name);
                    continue;
                }
                if (profile == null || !profile.HasDetectableEffect)
                {
                    string components = string.Join(",", ability.Blueprint.ComponentsArray?
                        .Where(c => c != null).Select(c => c.GetType().Name).Distinct() ?? Enumerable.Empty<string>());
                    unread.Add($"{name} [{components}]");
                    continue;
                }
                if (!ability.IsAvailable) { unavailable.Add(name); continue; }
                understood.Add($"{name} ({Kinds(profile)})");
            }
            Main.Log($"[Coverage] {unit.CharacterName} abilities: {abilities.Count} on the action bar; " +
                     $"understood {understood.Count}: {string.Join("; ", understood)}");
            if (unavailable.Count > 0) Main.Log($"[Coverage] {unit.CharacterName} not available now: {string.Join("; ", unavailable)}");
            if (blacklisted.Count > 0) Main.Log($"[Coverage] {unit.CharacterName} blacklisted: {string.Join("; ", blacklisted)}");
            if (itemPolicy.Count > 0) Main.Log($"[Coverage] {unit.CharacterName} held back by the item settings: {string.Join("; ", itemPolicy)}");
            if (unread.Count > 0)
                Main.Log($"[Coverage] {unit.CharacterName} effect not understood (not used: ★ v0.3.18 no 'utility' value any more): {string.Join("; ", unread)}");
        }

        private static string Kinds(AbilityProfile p)
        {
            var kinds = new List<string>();
            if (p.DealsDamage) kinds.Add("damage");
            if (p.Heals.Count > 0 || p.CasterHeals.Count > 0) kinds.Add("heal");
            if (p.TargetBuffs.Count > 0) kinds.Add(p.HarmfulEffects ? "debuff" : "buff");
            if (p.Intimidation != null) kinds.Add("intimidate check");   // ★ v0.3.18 (AA-4)
            foreach (var m in p.Maneuvers) kinds.Add($"maneuver {m.Type}");
            if (p.CasterBuffs.Count > 0) kinds.Add("self buff");
            if (p.Summons) kinds.Add("summon");
            if (p.Dispels) kinds.Add("dispel");
            if (p.RemovesBuffs) kinds.Add("removal");
            if (p.Enchants) kinds.Add("enchant");
            if (p.IsCharge) kinds.Add("charge");
            if (p.IsAoE) kinds.Add("area");
            return string.Join("+", kinds);
        }

        /// <summary>★ v0.3.18 (AA-2): whether the engine may start this toggle by itself, and why not.</summary>
        private static string AiNote(UnitEntityData unit, ActivatableAbility toggle)
        {
            try
            {
                string why = TacticalEngine.ToggleExclusion(unit, toggle);
                if (why == null) return "may start it when worth it";
                // ★ v0.3.18 (AA-3): attack modes and Mobility are decided with the attack or the move.
                string mode = TacticalEngine.ModeExclusion(toggle);
                if (mode == null)
                {
                    // ★ v0.3.19: what its buff removes when it ends (Fighting Defensively), to check it is harmless.
                    string removes = TacticalEngine.ModeRemovesOnEnd(toggle);
                    string values = TacticalEngine.ModeValuesNote(unit, toggle);
                    return "may switch it with attacks and moves" + (values != null ? $" ({values})" : "") +
                           (removes != null ? $" (when it ends, its buff removes: {removes})" : "");
                }
                // ★ v0.3.19 (test6): an infusion with a burn cost - switched off when its burn blocks every blast.
                if (TacticalEngine.IsBlastModifier(toggle.Blueprint) && toggle.Blueprint.Buff != null &&
                    Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<Kingmaker.UnitLogic.Class.Kineticist.AddKineticistBurnModifier>(toggle.Blueprint.Buff).Any(m => m.Value > 0))
                    return (ModSettings.Instance?.AiManagesToggles ?? true)
                        ? "leaves it, except: switches it off when its burn blocks every blast (back on after the fight)"
                        : "leaves it (switched off in the settings)";
                return $"leaves it ({why}; as a combat mode: {mode})";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void ReportToggle(UnitEntityData unit, ActivatableAbility toggle)
        {
            var bp = toggle.Blueprint;
            string command;
            try { command = bp.ActivateWithUnitCommand ? UnitActivateAbility.GetCommandType(toggle).ToString() : "none"; }
            catch { command = "?"; }
            string resource = "";
            try
            {
                var logic = Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<ActivatableAbilityResourceLogic>(bp);
                if (logic != null)
                    resource = $", spends {logic.SpendType}" +
                               (logic.RequiredResource != null ? $" of {logic.RequiredResource.name} ({unit.Resources.GetResourceAmount(logic.RequiredResource)} left)" : "");
            }
            catch { }
            string buff = "";
            try
            {
                var b = bp.Buff;
                if (b != null)
                    buff = $", buff {b.name} [{string.Join(",", b.ComponentsArray?.Where(c => c != null).Select(c => c.GetType().Name).Distinct() ?? Enumerable.Empty<string>())}]";
            }
            catch { }
            Main.Log($"[Coverage] {unit.CharacterName} toggle: {toggle.Name} - on={toggle.IsOn} started={toggle.IsStarted} " +
                     $"available={toggle.IsAvailable}; activation {bp.ActivationType}, action {command}" +
                     (bp.Group != ActivatableAbilityGroup.None ? $", group {bp.Group}" : "") +
                     $", ends with combat={bp.DeactivateIfCombatEnded}, off at once={bp.DeactivateImmediately}, " +
                     $"on by default={bp.IsOnByDefault}{(bp.HiddenInUI ? ", hidden" : "")}{resource}{buff}; " +
                     $"AI: {AiNote(unit, toggle)}");
        }
    }
}
