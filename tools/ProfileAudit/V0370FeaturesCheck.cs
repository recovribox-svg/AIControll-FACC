using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 F1-F4 (author requests): end-of-turn repositioning, weapon-set switching, summon choice and escape teleports
    /// beyond a walk. The routes the changes rely on, and the stock facts behind them.
    /// </summary>
    internal static class V0370FeaturesCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-FEATURES " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);
            var math = typeof(CombatMath);
            var abilities = blueprints.OfType<BlueprintAbility>().ToLookup(a => a.name, StringComparer.Ordinal);

            // --- F1: end-of-turn reposition ---
            Check(Calls(engine, "AddMovementCandidates", "AddRepositionCandidate") &&
                  Calls(engine, "AddRepositionCandidate", "FindRetreatPoint") && Calls(engine, "AddRepositionCandidate", "SpotDrop") &&
                  Calls(engine, "AddRepositionCandidate", "IncomingDamage") && Calls(engine, "AddRepositionCandidate", "MoveRisk") &&
                  Calls(engine, "AddRepositionCandidate", "RepositionGain") && Calls(engine, "AddRepositionCandidate", "KeepRange") &&
                  Calls(engine, "AddRepositionCandidate", "get_IsRealTime"),
                  "a ranged unit's leftover move action takes a safer spot that keeps an enemy in range and sight, priced by drop chance, damage and the walk");

            // --- F2: weapon sets ---
            var command = new UnitSwitchHandEquipmentSet(1);
            Check(command.Type == UnitCommand.CommandType.Move, $"the native weapon-set switch is a {command.Type} action");
            Check(Calls(engine, "Decide", "AddWeaponSetCandidate") && Calls(engine, "AddWeaponSetCandidate", "EstimateWithWeapon") &&
                  Calls(engine, "AddWeaponSetCandidate", "WeaponSetSwitchWorth") && Calls(engine, "AddWeaponSetCandidate", "AttackRollProvokeRisk") &&
                  Calls(engine, "AddWeaponSetCandidate", "CanChangeWeaponSet") && Calls(engine, "AddWeaponSetCandidate", "get_IsPolymorphed") &&
                  Calls(engine, "Execute", "ValidateWeaponSetIssue") && Calls(engine, "ValidateWeaponSetIssue", "CanChangeWeaponSet"),
                  "a Mixed-range unit's other weapon set is priced against its current weapon and switched by the native command");
            Check(Calls(math, "EstimateWithWeapon", "InactiveEnhancement") && Calls(math, "EstimateWithWeapon", "IterativeAttacks") &&
                  Calls(math, "EstimateWithWeapon", "ReadHitOnTarget") && Calls(math, "EstimateWithWeapon", "GetApproachRadius") &&
                  Calls(math, "InactiveEnhancement", "get_HoldingSlot"),
                  "the inactive set is estimated by the game's attack rules, its switched-off enhancement added back");

            // --- F3: summon choice ---
            Check(Calls(engine, "ScoreAbility", "SummonWorth") && Calls(engine, "SummonWorth", "SummonCount") &&
                  Calls(engine, "SummonWorth", "SummonPrior") && Calls(engine, "SummonWorth", "SummonPerRound") &&
                  Calls(engine, "SummonWorth", "SummonBodyShare") && Calls(engine, "SummonWorth", "TryPerRound") &&
                  Calls(engine, "SummonCount", "RaiseEvent") && Calls(engine, "Decide", "NoteSummonRounds") &&
                  Calls(typeof(DamageObserver), "OnEventDidTrigger", "NoteSummonDamage"),
                  "a summon is priced by its creatures' count, duration, CR against the enemies and their measured damage per round");
            int read = 0;
            var notes = new List<string>();
            foreach (string name in new[] { "SummonMonsterVSingle", "SummonMonsterVd3", "SummonMonsterVd4plus1" })
            {
                var bp = abilities[name].FirstOrDefault();
                var profile = bp == null ? null : AbilityProfile.Get(bp);
                var spawns = profile == null ? null : typeof(AbilityProfile).GetField("SpawnActions", all)?.GetValue(profile)
                    as System.Collections.Generic.List<Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSpawnMonster>;
                var spawn = spawns?.FirstOrDefault();
                if (spawn?.Blueprint == null) continue;
                read++;
                notes.Add($"{name}: {spawn.Blueprint.name} CR{spawn.Blueprint.CR} x {spawn.CountValue}");
            }
            Check(read == 3, $"Summon Monster V's three variants read their creature and count ({string.Join("; ", notes)})");

            // --- F4: far escapes ---
            Check(Calls(engine, "AddTeleportEscape", "AddFarEscapes") && Calls(engine, "AddFarEscapes", "FindRetreatPoint") &&
                  Calls(engine, "AddFarEscapes", "GetApproachDistance") && Calls(engine, "AddFarEscapes", "SpotDrop") &&
                  Calls(engine, "AddFarEscapes", "FightReach"),
                  "an escape teleport also tries safe spots up to its own range, each kept within reach of the fight and priced by its drop chance");

            failures = failed;
            lines.Add($"V0370-FEATURES {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
