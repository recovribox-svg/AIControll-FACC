// ★ Fusion coverage C4 (docs/FUSION_COVERAGE_REST.md): native line delivery - its contract, the actual line spells, the flat
// pre-filter against the game's own WouldTargetPointLine on the actual Lightning Bolt component, and the compiled routes.
// Actor-free: no Game/Player, unit, context, projectile, Rulebook or cast. The native method is called with plain vectors.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Controllers.Projectiles;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using UnityEngine;

namespace ProfileAudit
{
    internal static class LineCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>A method's IL references, with its lambdas and, for an iterator, its state machine's.</summary>
        private static List<MemberInfo> Body(Type type, string name)
        {
            var members = new List<MemberInfo>();
            foreach (var m in type.GetMethods(Hidden).Where(m => m.Name == name && m.GetMethodBody() != null))
                members.AddRange(SaveBoostCheck.ReferencedMembers(m));
            string prefix = "<" + name + ">";
            foreach (var holder in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)))
            {
                bool machine = holder.Name.StartsWith(prefix, StringComparison.Ordinal);
                foreach (var m in holder.GetMethods(Hidden | BindingFlags.DeclaredOnly))
                    if ((machine || m.Name.StartsWith(prefix, StringComparison.Ordinal)) && m.GetMethodBody() != null)
                        members.AddRange(SaveBoostCheck.ReferencedMembers(m));
            }
            return members;
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        // The engine's line types are internal: reached by name.
        private static Type Internal(string name) => typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics." + name, true);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>(); string info = "";
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("LINE FAILED: " + why); } }
            try
            {
                var deliver = typeof(AbilityDeliverProjectile);
                var lineType = Internal("ProjectileLine"); var geometry = Internal("LineGeometry");
                var area = Internal("ProjectileArea"); var coneType = Internal("ProjectileCone");
                var supported = lineType.GetMethod("Supported", Hidden);
                bool Supported(AbilityDeliverProjectile c, out string reason)
                {
                    var args = new object[] { c, null };
                    bool ok = (bool)supported.Invoke(null, args);
                    reason = (string)args[1];
                    return ok;
                }
                var catches = geometry.GetMethod("Catches", Hidden);
                var travelOf = lineType.GetMethod("Travel", Hidden);
                float Travel(float l, float r) => (float)travelOf.Invoke(null, new object[] { l, r });
                // --- native contracts ---
                var step = Body(deliver, "StepBasedDeliver");
                Check(Calls(step, deliver, "WouldTargetUnitLine") && step.Any(m => m.Name == "PassedDistance") &&
                      step.Any(m => m.Name == "get_IsHit"),
                    "native: a line hands every unit it passes over to the payload by WouldTargetUnitLine, as far as the projectile went");
                var unitLine = Body(deliver, "WouldTargetUnitLine");
                Check(Calls(unitLine, deliver, "CanTargetUnit") && Calls(unitLine, deliver, "WouldTargetPointLine"),
                    "native: WouldTargetUnitLine = the target rule (Selective) and the point test");
                var pointLine = Body(deliver, "WouldTargetPointLine");
                var pointCone = Body(deliver, "WouldTargetPointCone");
                Check(Calls(pointLine, deliver, "get_LineWidth") && !pointLine.Any(m => m.Name == "HasObstacle") &&
                      pointCone.Any(m => m.Name == "HasObstacle"),
                    "native: a line's point test reads its width and no sight (a cone's casts a sight ray)");
                var launch = Body(deliver, "Deliver");
                Check(launch.Any(m => m.Name == "get_Length") && Calls(launch, deliver, "GetProjectileRange") &&
                      launch.Any(m => m.Name == "set_MaxRange") && launch.Any(m => m.Name == "get_Corpulence") &&
                      launch.Any(m => m.Name == "get_EyePosition"),
                    "native: a line leaves the eye, moved by the body, towards Length, with the projectile range as its MaxRange");
                var projectileStops = typeof(Projectile).GetMethods(Hidden | BindingFlags.DeclaredOnly).Where(m => m.GetMethodBody() != null)
                    .Any(m => { var b = SaveBoostCheck.ReferencedMembers(m).ToList(); return b.Any(x => x.Name == "get_MaxRange") && b.Any(x => x.Name == "PassedDistance"); });
                Check(projectileStops, "native: a projectile stops once it has passed its MaxRange");

                // --- the actual line spells ---
                var abilities = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => abilities.FirstOrDefault(a => a.name == name);
                var bolt = Spell("LightningBolt");
                var boltLine = bolt?.GetComponent<AbilityDeliverProjectile>();
                Check(boltLine != null && Supported(boltLine, out _) && Math.Abs(boltLine.Length.Value - 120f) < 0.01f &&
                      Math.Abs(boltLine.LineWidth.Value - 5f) < 0.01f,
                    "actual Lightning Bolt: an ordinary 120 ft, 5 ft wide line this reader takes");
                foreach (var name in new[] { "FireSnake", "AcidicSpray" })
                {
                    var c = Spell(name)?.GetComponent<AbilityDeliverProjectile>();
                    Check(c != null && Supported(c, out _) && Math.Abs(c.Length.Value - 50f) < 0.01f,
                        $"actual {name}: an ordinary 50 ft line this reader takes");
                }
                var boltProfile = bolt != null ? AbilityProfile.Get(CasterPolicyCheck.Data(bolt)) : null;
                Check(boltProfile != null && boltProfile.IsProjectileLine && boltProfile.IsAoE && boltProfile.SelectsTargets &&
                      !boltProfile.SelectsAround && !boltProfile.IsProjectileCone && !boltProfile.NeedsAttackRoll,
                    "an ordinary Lightning Bolt is an area now: its recipients, allies too, come from the native line query");
                var book = loaded.OfType<BlueprintSpellbook>().FirstOrDefault(b => b.name == "MagicDeceiverSpellbook")?
                    .GetComponent<MagicHackSpellbookComponent>();
                var hackLine = book != null ? (typeof(MagicHackSpellbookComponent).GetField("m_LineProjectile", Hidden)?.GetValue(book) as BlueprintProjectileReference)?.Get() : null;
                Check(book == null || hackLine != null && !hackLine.FollowTerrain,
                    "the Magic Deceiver's own line projectile (a fused line's) does not follow the terrain");
                int read = 0, unread = 0; var why = new Dictionary<string, int>();
                foreach (var a in abilities)
                {
                    var c = a.GetComponent<AbilityDeliverProjectile>();
                    if (c == null || c.Type != AbilityProjectileType.Line) continue;
                    if (Supported(c, out string reason)) read++;
                    else { unread++; why[reason] = why.TryGetValue(reason, out int n) ? n + 1 : 1; }
                }
                Check(read > 0, "the loaded line deliveries are read");
                info = $"; {read} line deliveries read, {unread} not ({string.Join(", ", why.Select(kv => kv.Key + " " + kv.Value))})";

                // --- the flat pre-filter is the native point test (actual Lightning Bolt component) ---
                var native = deliver.GetMethod("WouldTargetPointLine", Hidden, null,
                    new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector2), typeof(float) }, null);
                Check(native != null, "native WouldTargetPointLine found");
                if (native != null && boltLine != null)
                {
                    var random = new System.Random(20261121);
                    int agree = 0, samples = 0;
                    float half = boltLine.LineWidth.Meters * 0.5f;
                    for (int i = 0; i < 4000; i++)
                    {
                        float angle = (float)(random.NextDouble() * Math.PI * 2);
                        var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        var from = new Vector3((float)random.NextDouble() * 20f - 10f, (float)random.NextDouble() * 4f, (float)random.NextDouble() * 20f - 10f);
                        var at = from + new Vector3((float)random.NextDouble() * 80f - 40f, (float)random.NextDouble() * 6f - 3f, (float)random.NextDouble() * 80f - 40f);
                        float radius = (float)random.NextDouble() * 1.5f, travel = 5f + (float)random.NextDouble() * 40f;
                        bool game = (bool)native.Invoke(boltLine, new object[] { at, radius, from, dir, travel });
                        bool ours = (bool)catches.Invoke(null, new object[] { from.x, from.z, dir.x, dir.y, travel, half, at.x, at.z, radius });
                        samples++;
                        if (game == ours) agree++;
                    }
                    Check(agree == samples, $"the flat pre-filter agrees with the game's WouldTargetPointLine on {agree} of {samples} samples");
                }
                Check(Travel(36.6f, 0f) == 36.6f && Travel(36.6f, 20f) == 20f && Travel(15f, 40f) == 15f,
                    "a line travels its length, cut at a set MaxRange (0 = none)");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                Check(Calls(Body(engine, "ScoreAbility"), area, "TryCreate") && !Calls(Body(engine, "ScoreAbility"), coneType, "TryCreate"),
                    "scoring reads a line's (and a cone's) recipients from the native projectile query");
                var evaluate = Body(engine, "EvaluateAbility");
                Check(Calls(evaluate, engine, "LineAimAlong") && Calls(evaluate, engine, "LineUpFor") &&
                      Calls(evaluate, typeof(ValueModel), "WalkedNet") && Calls(evaluate, typeof(ValueModel), "LineUpWorthIt"),
                    "a line is re-aimed along enemies and past allies, and a lined-up walk must beat casting from here");
                Check(Calls(Body(engine, "Execute"), engine, "NoteLineUpWalk"), "one lined-up walk a round: the walk is noted when issued");
                var plan = Body(engine, "PlanLineUps");
                Check(new[] { "EnteringDanger", "IsPositionValid", "StruckBySwarm", "MoveRisk", "IsUnitCloseEnough", "TakenAt" }.All(n => plan.Any(m => m.Name == n)),
                    "a lined-up spot is checked like an approach spot: free, walkable, no area or swarm, in range and sight, priced");
                Check((int)engine.GetField("LineUpChecks", Hidden).GetRawConstantValue() == 8 && plan.Any(m => m.Name == "Take"),
                    "native lined-up spot validations are bounded to eight, even when every proposed spot is rejected");
                Check(Calls(Body(engine, "LineAimAlong"), lineType, "TryCreate") && Calls(Body(engine, "LineCount"), geometry, "Catches"),
                    "aims are pre-filtered by the native formula, then valued through the native query");
                Check(Calls(Body(typeof(ProgressiveGate), "SuccessChance"), area, "TryCreate"),
                    "Progressive viability reads a line's recipients the same way");
                var keeps = Body(engine, "ProjectileKeepsRecipients");
                Check(Calls(keeps, area, "TryCreate"), "a step before a line cast must keep its counted enemies and add no ally");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"LINE {checks} checks, {bad} failures{info}");
            return lines;
        }
    }
}
