using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;

namespace ProfileAudit
{
    /// <summary>★ v0.3.70 playtest review: the routes its three corrections rely on, and the stock facts behind them.</summary>
    internal static class V0370ReviewCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-REVIEW " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);
            var outcome = typeof(CastOutcome);

            // 1. Spell prohibitions are priced by the recipient's measured spell edge, learnt bans included.
            Check(Calls(engine, "Decide", "NoteSpellEdge") && Calls(engine, "NoteSpellEdge", "LearnSpellBan") &&
                  Calls(engine, "AppraiseParts", "TrySpellEdge") && Calls(engine, "AppraiseParts", "Loss") &&
                  Calls(engine, "AppraiseToggleBuffs", "ToggleBansSpells"),
                  "a spell prohibition costs the recipient's measured edge; a run-time ban is learnt from the banning buff's context chain");

            // 2. Fusion failure keys carry the fusion's identity (all fusions share the game's holder blueprints).
            Check(Calls(outcome, "Begin", "FusionOf") && Calls(outcome, "IsBlocked", "FusionOf") &&
                  Calls(outcome, "AreaSkips", "FusionOf") && Calls(outcome, "FusionOf", "FusionIdentityOf"),
                  "a fusion's cast failures and area misses are its own, not every fusion's on that target");

            // 3. The teleport crowd bound no longer withholds a large party.
            var cap = engine.GetField("MaxTeleportCombatants", all);
            Check(cap != null && cap.IsLiteral && (int)cap.GetRawConstantValue() >= 64,
                  $"teleport combatant bound {(cap == null ? "missing" : cap.GetRawConstantValue().ToString())} (17-24 party units and 8-13 enemies played)");

            // Stock facts: the game forbids spells and spell-like abilities alike (AbilityData.CanBeCastByCaster), and both
            // teleports summons carry are of those types; the pre-Master-Skald rage effect carries the ban.
            var byName = blueprints.OfType<SimpleBlueprint>().ToLookup(b => b.name, StringComparer.Ordinal);
            T One<T>(string name) where T : SimpleBlueprint => byName[name].OfType<T>().FirstOrDefault();
            var vrock = One<BlueprintAbility>("Vrock_Ability_GreaterTeleport");
            var hellDoor = One<BlueprintAbility>("DimensionDoorHell");
            Check(vrock?.Type == AbilityType.SpellLike && hellDoor?.Type == AbilityType.Spell,
                  $"Greater Teleport {vrock?.Type.ToString() ?? "missing"}, Dimension Door (Hell) {hellDoor?.Type.ToString() ?? "missing"}: both unavailable while spellcasting is forbidden");
            var before = One<BlueprintBuff>("InspiredRageEffectBuffBeforeMasterSkald");
            var forbid = before == null ? null : Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<ForbidSpellCasting>(before);
            Check(before == null || forbid != null && forbid.IgnoreFeature?.name == "InspiredRage",
                  before == null ? "InspiredRageEffectBuffBeforeMasterSkald not in the inputs (nothing to check)"
                                 : "InspiredRageEffectBuffBeforeMasterSkald forbids spells, ignored only by the skald (InspiredRage)");

            failures = failed;
            lines.Add($"V0370-REVIEW {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
