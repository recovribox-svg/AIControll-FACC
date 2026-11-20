// ★ Fusion coverage C2 (docs/FUSION_COVERAGE_PROGRAMME.md): Hideous Laughter's Best Jokes recast on a random enemy. Native
// selector contract, the actual graph, arcan's Explosive Runes / Hideous Laughter, synthetic refusals and compiled routes.
// Actor-free: no Game/Player, Rulebook, cast or random roll.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace ProfileAudit
{
    internal static class FusionRecastCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        private static readonly MethodInfo ChildLists = typeof(AbilityProfile).GetMethod("ChildActionLists", Hidden);
        private static readonly MethodInfo EnemyOnly = typeof(AbilityProfile).GetMethod("IsEnemyOnlyRecast", Hidden);
        private static bool IsEnemyOnlyRecast(ContextActionOnRandomTargetsAround r) => (bool)EnemyOnly.Invoke(null, new object[] { r });

        private static IEnumerable<GameAction> Flatten(IEnumerable<GameAction> actions)
        {
            foreach (var action in actions ?? Enumerable.Empty<GameAction>())
            {
                if (action == null) continue;
                yield return action;
                foreach (var child in (IEnumerable<GameAction[]>)ChildLists.Invoke(null, new object[] { action }))
                    foreach (var inner in Flatten(child)) yield return inner;
            }
        }

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-RECAST FAILED: " + why); } }
            try
            {
                // --- native contract ---
                var run = Body(typeof(ContextActionOnRandomTargetsAround), "RunAction");
                Check(run.Any(m => m.Name == "IsEnemy") && run.Any(m => m.Name == "RemoveAll") && run.Any(m => m.Name == "get_MaybeCaster"),
                    "native: with OnEnemies the random selector removes every unit that is not an enemy of the caster");
                Check(typeof(ContextActionCastSpell).GetProperty("Spell")?.PropertyType == typeof(BlueprintAbility),
                    "native: the recast names its spell");

                // --- the actual graph ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.First(b => b.name == name);
                var laughter = Spell("HideousLaughter");
                var actions = Flatten(laughter.GetComponent<AbilityEffectRunAction>()?.Actions?.Actions).ToList();
                var selector = actions.OfType<ContextActionOnRandomTargetsAround>().SingleOrDefault();
                var gate = actions.OfType<Conditional>().FirstOrDefault(c => c.IfTrue?.Actions?.Contains(selector) == true);
                Check(selector != null && selector.OnEnemies && selector.NumberOfTargets == 1 &&
                      selector.Actions.Actions.OfType<ContextActionCastSpell>().Single().Spell == laughter,
                    "actual Hideous Laughter recasts itself on one random enemy");
                Check(gate?.ConditionsChecker?.Conditions?.OfType<ContextConditionCasterHasFact>().Any(c =>
                          c.Fact?.AssetGuid.ToString() == "ec739ff2292290f43b20689ff32de112") == true,
                    "actual recast is gated on the caster fact ec739ff2 (Best Jokes)");
                Check(IsEnemyOnlyRecast(selector), "the actual selector is an enemy-only recast");
                // Synthetic refusals: allies possible, or an effect of its own besides the cast.
                var allies = new ContextActionOnRandomTargetsAround { OnEnemies = false, Actions = selector.Actions };
                var extra = new ContextActionOnRandomTargetsAround { OnEnemies = true, Actions = new ActionList {
                    Actions = selector.Actions.Actions.Concat(new GameAction[] { new ContextActionDealDamage() }).ToArray() } };
                Check(!IsEnemyOnlyRecast(allies) && !IsEnemyOnlyRecast(extra) &&
                      !IsEnemyOnlyRecast(null),
                    "a selector that can reach allies, or that adds its own effect, is not exempt");

                // --- arcan's Explosive Runes / Hideous Laughter ---
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Enchantment, SpellLevel = 4 };
                    return data;
                }
                var withheld = typeof(TacticalEngine).GetMethod("FusionWithheld", Hidden);
                var model = typeof(TacticalEngine).GetMethod("FusionOutcomeModel", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                var reason = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                var recastAt = typeof(AbilityProfile).GetProperty("FusionEnemyRecastLocation", Hidden);
                var laughterBuff = loaded.OfType<BlueprintBuff>().Single(b => b.AssetGuid.ToString() == "4b1f07a71a982824988d7f48cd49f3f8");
                foreach (var (a, b, save) in new[] { ("ExplosiveRunes", "HideousLaughter", SavingThrowType.Will),
                                                    ("ExplosiveRunes", "HideousLaughter", SavingThrowType.Reflex),
                                                    ("HideousLaughter", "ExplosiveRunes", SavingThrowType.Will),
                                                    ("Fireball", "HideousLaughter", SavingThrowType.Will) })
                {
                    var fused = Fuse(a, b, save);
                    var profile = AbilityProfile.Get(fused);
                    string why = (string)withheld.Invoke(null, new object[] { fused, profile });
                    bool joint = (bool)model.Invoke(null, new object[] { fused, profile });
                    int laughs = ((IEnumerable)applications.GetValue(profile)).Cast<object>()
                        .Count(o => o.GetType().GetField("Buff").GetValue(o) == laughterBuff);
                    Check(why == null && joint && recastAt.GetValue(profile) != null && laughs == 1,
                        $"actual {a}/{b} ({save}) is offered with the joint value; the recast is read once as no credit " +
                        $"(withheld: {why ?? "no"}, reason: {reason.GetValue(profile) ?? "none"}, laughter applications {laughs})");
                }

                // --- compiled routes ---
                Check(Calls(Body(typeof(AbilityProfile), "InspectFusionPaths"), typeof(AbilityProfile), "IsEnemyOnlyRecast") &&
                      Calls(Body(typeof(AbilityProfile), "Walk"), typeof(AbilityProfile), "IsEnemyOnlyRecast"),
                    "the fused path reader admits the recast and the profile walk skips it");
                // Ordinary Hideous Laughter keeps reading its own branch as before (the skip is for fused profiles).
                var ordinary = AbilityProfile.Get(CasterPolicyCheck.Data(laughter));
                Check(ordinary != null && ordinary.TargetBuffs.Contains(laughterBuff), "ordinary Hideous Laughter still reads its buff");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"FUSION-RECAST {checks} checks, {bad} failures");
            return lines;
        }
    }
}
