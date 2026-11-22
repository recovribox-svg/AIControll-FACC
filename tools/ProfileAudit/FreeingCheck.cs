// ★ coverage4 (author request, docs/COVERAGE4_FOLLOWUPS.md): freeing an ally under an enemy's control with a dispel or a
// removal. Native contracts, the actual Dominate and Dispel Magic graphs, the pure held-rounds rule and compiled routes.
// Actor-free: no Game/Player, Rulebook, buff instance, cast or random roll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class FreeingCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FREEING FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                // --- native contracts ---
                var run = Body(typeof(ContextActionDispelMagic), "RunAction");
                Check(run.Any(m => m.Name == "Sort") && run.Any(m => m.Name == "TryDispelBuff"),
                    "native: a dispel tries the target's buffs sorted by caster level, highest first");
                var tryBuff = SaveBoostCheck.ReferencedMembers(typeof(ContextActionDispelMagic).GetMethod("TryDispelBuff", Hidden)).ToList();
                Check(tryBuff.Any(m => m.Name == "OnlyTargetEnemyBuffs") && tryBuff.Any(m => m.Name == "get_IsFromSpell") &&
                      tryBuff.Any(m => m.Name == "get_IsNotDispelable"),
                    "native: its filters (from spells, not dispellable, enemy buffs only when asked)");
                Check(Body(typeof(ChangeFaction), "OnTurnOff").Any(m => m.Name == "Stop"),
                    "native: removing the buff stops its faction switch (the ally is back)");

                // --- the actual graphs ---
                var dominate = loaded.OfType<BlueprintBuff>().FirstOrDefault(b => b.AssetGuid.ToString() == "c0f4e1c24c9cd334ca988ed1bd9d201f");
                var save = dominate?.GetComponents<Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions>()
                    .SelectMany(c => c.NewRound?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                    .OfType<ContextActionSavingThrow>().FirstOrDefault();
                var endsOnPass = engine.GetMethod("EndsOnPass", Hidden);
                Check(dominate?.GetComponent<ChangeFaction>() != null && save != null && save.Type == SavingThrowType.Will &&
                      endsOnPass != null && (bool)endsOnPass.Invoke(null, new object[] { save }),
                    "actual Dominate Person: a faction switch its bearer ends with a passed Will save each round");
                var dispelSpell = loaded.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == "DispelMagicTarget");
                var dispel = dispelSpell?.GetComponents<Kingmaker.UnitLogic.Abilities.Components.AbilityEffectRunAction>()
                    .SelectMany(r => r.Actions?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                    .OfType<ContextActionDispelMagic>().FirstOrDefault();
                var unsupported = engine.GetMethod("DispelUnsupported", Hidden);
                Check(dispel != null && unsupported != null && unsupported.Invoke(null, new object[] { dispel }) == null &&
                      !dispel.OnlyTargetEnemyBuffs,
                    "actual Dispel Magic: read by the dispel order; it does not skip the party's own buffs on the ally");
                var profile = dispelSpell != null ? AbilityProfile.Get(CasterPolicyCheck.Data(dispelSpell)) : null;
                Check(profile != null && profile.DispelActions.Contains(dispel), "the profile carries Dispel Magic's dispel action");

                // --- the pure rule ---
                Check(Math.Abs(ValueModel.HeldRounds(3f, 0f) - 3f) < 1e-4f && Math.Abs(ValueModel.HeldRounds(3f, 0.5f) - 1.75f) < 1e-4f &&
                      Math.Abs(ValueModel.HeldRounds(6f, 1f) - 1f) < 1e-4f,
                    "held rounds: (1 - pass)^k for round k within the horizon");

                // --- compiled routes ---
                var score = Body(engine, "ScoreAbility");
                Check(Calls(score, engine, "FreeingValue"), "ScoreAbility adds the release of an ally under an enemy's control");
                var freeing = Body(engine, "FreeingValue");
                Check(new[] { "DispelValue", "FreedWorth", "HoldsUnderControl", "RemovesByNameOrDescriptor" }.All(n => Calls(freeing, engine, n)) &&
                      Calls(freeing, typeof(CombatMath), "EffectChance"),
                    "the release: a dispel in the game's order or a removal of the hold, times the chance the cast reaches the ally");
                var full = engine.GetMethods(Hidden).Where(m => m.Name == "DispelValue").OrderByDescending(m => m.GetParameters().Length).First();
                var order = SaveBoostCheck.ReferencedMembers(full).ToList();
                Check(new[] { "HoldsUnderControl", "FreedWorth", "OfPartySide", "IsAutomaticDispel", "ExpectedDispelWorth" }.All(n => order.Any(m => m.Name == n)),
                    "the dispel order prices the hold as a release and a buff the party gave the ally as a loss");
                var worth = Body(engine, "FreedWorth");
                Check(new[] { "RoundSavePass", "HeldRounds", "ControlWorth", "PerRound" }.All(n => worth.Any(m => m.Name == n)),
                    "a release is worth the enemy's lost fighter and the ally back, for the rounds the hold would last");
                Check(Body(engine, "HoldsUnderControl").Any(m => m.Name == "get_IsSuppressed") &&
                      Body(engine, "OfPartySide").Any(m => m.Name == "get_FactionOriginal"),
                    "a hold is an enabled faction switch; the party side is known by the original faction");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"FREEING {checks} checks, {bad} failures");
            return lines;
        }
    }
}
