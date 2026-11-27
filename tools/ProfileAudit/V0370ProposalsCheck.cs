using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace ProfileAudit
{
    /// <summary>
    /// ★ v0.3.70 P1-P3 (author-approved): escape and casting-spot teleports, item teleports, and a driven summon declining a
    /// rage. The routes the changes rely on, and the stock facts behind them (Inspired Rage, Evading Steps).
    /// </summary>
    internal static class V0370ProposalsCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("V0370-PROPOSALS " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            bool Calls(Type owner, string method, string name) => owner.GetMethods(all).Where(m => m.Name == method)
                .SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).Any(member => member.Name == name);
            var engine = typeof(TacticalEngine);
            var byName = blueprints.OfType<SimpleBlueprint>().ToLookup(b => b.name, StringComparer.Ordinal);
            T One<T>(string name) where T : SimpleBlueprint => byName[name].OfType<T>().FirstOrDefault();

            // --- Inspired Rage's prohibition is read from the stock blueprints ---
            var effect = One<BlueprintBuff>("InspiredRageEffectBuff");
            var before = One<BlueprintBuff>("InspiredRageEffectBuffBeforeMasterSkald");
            if (effect == null || before == null)
                Check(false, "InspiredRageEffectBuff / InspiredRageEffectBuffBeforeMasterSkald not in the inputs");
            else
            {
                var bans = BuffPartsReader.Read(effect).Parts.Where(p => p.Kind == BuffPartKind.ForbidsSpells).ToList();
                var guarded = bans.FirstOrDefault(p => p.Owner == before && p.Guards != null);
                bool masterSkaldGate = guarded?.Guards.Any(g => g.Branch && g.Checker?.Conditions != null &&
                    g.Checker.Conditions.OfType<ContextConditionCasterHasFact>().Any(c => c.Not && c.Fact?.name == "MasterSkald")) == true;
                Check(guarded != null && masterSkaldGate && guarded.ForbidIgnore?.name == "InspiredRage",
                      $"InspiredRageEffectBuff gives a spell prohibition behind its caster-fact branch ({bans.Count} part(s); " +
                      $"Master Skald lifts it: {masterSkaldGate}; ignored by {guarded?.ForbidIgnore?.name ?? "none"})");
                Check(Calls(engine, "AppraiseParts", "BanReaches") && Calls(engine, "BanReaches", "CheckSafe"),
                      "a recipient is priced for the prohibition only when the branch is taken under the performer's context");
                var apply = AllActions(effect).OfType<ContextActionApplyBuff>().FirstOrDefault(a => a.Buff == before);
                Check(apply != null && apply.AsChild, "the prohibition is the effect's child (removed with it)");
            }

            // --- P2: the accept switch ---
            var area = One<BlueprintAbilityAreaEffect>("InspiredRageArea");
            var toggle = One<BlueprintActivatableAbility>("InspiredRageAllyToggleSwitch");
            var switchBuff = toggle?.Buff;
            if (area == null || toggle == null || switchBuff == null || effect == null)
                Check(false, "InspiredRageArea / InspiredRageAllyToggleSwitch not in the inputs");
            else
            {
                var grants = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(engine.GetNestedType("AreaGrant", all)));
                engine.GetMethod("ReadAreaGrants", all).Invoke(null, new object[] { area, grants, false });
                int effectGrants = 0, switched = 0;
                foreach (var grant in grants)
                {
                    var buff = grant.GetType().GetField("Buff", all).GetValue(grant) as BlueprintBuff;
                    if (buff != effect) continue;
                    effectGrants++;
                    var guards = ((Kingmaker.ElementsSystem.ConditionsChecker Checker, bool Branch)[])grant.GetType().GetField("Guards", all).GetValue(grant);
                    if (guards.Any(g => g.Branch && g.Checker?.Conditions != null &&
                                        g.Checker.Conditions.OfType<ContextConditionHasFact>().Any(c => !c.Not && c.Fact == switchBuff)))
                        switched++;
                }
                Check(effectGrants > 0 && switched == effectGrants,
                      $"every grant of InspiredRageEffectBuff ({effectGrants}) needs the recipient's {switchBuff.name} ({switched})");
                var removes = Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<AddFactContextActions>(switchBuff)
                    .SelectMany(c => c.Deactivated?.Actions ?? new Kingmaker.ElementsSystem.GameAction[0])
                    .OfType<ContextActionRemoveBuff>().Any(r => r.Buff == effect);
                Check(removes && !toggle.ActivateWithUnitCommand,
                      "switching the accept toggle off removes the effect at once and needs no command");
            }
            Check(Calls(engine, "Decide", "SwitchCostlyAccept") && Calls(engine, "SwitchCostlyAccept", "Decline") &&
                  Calls(engine, "SwitchCostlyAccept", "Reaccept") && Calls(engine, "Decline", "AcceptSwitchFor") &&
                  Calls(engine, "Decline", "HeldGrantPerRound") && Calls(engine, "Decline", "DeclinesGrant") &&
                  Calls(engine, "Decline", "PlayerOwnsToggle") && Calls(engine, "Decline", "RecordToggle") &&
                  Calls(engine, "Reaccept", "ReacceptsGrant") && Calls(engine, "ToggleValue", "DrivenSummonDeclines") &&
                  Calls(engine, "DrivenSummonDeclines", "ShouldDrive"),
                  "a driven summon declines an effect it loses by and takes it back when it gives more; the performer counts it as declined");

            // --- P3: item teleports ---
            var steps = One<BlueprintItemEquipmentFeet>("EvadingStepsItem");
            var mass = steps?.Ability;
            Check(steps != null && steps.SpendCharges && steps.Charges == 1 && steps.RestoreChargesOnRest &&
                  mass != null && mass.CanTargetPoint &&
                  Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<AbilityCustomDimensionDoor>(mass) != null,
                  $"Evading Steps: {mass?.name ?? "no ability"}, one charge a day, aimed at a point, native Dimension Door delivery");
            Check(Calls(engine, "TeleportGate", "IsAllowed") && Calls(engine, "TeleportGate", "ConsumableDeliveryChance") &&
                  Calls(engine, "TeleportGate", "HasAvailableBookCopy") && Calls(engine, "AddAbilityCandidates", "TeleportGate") &&
                  Calls(engine, "CostsNothing", "SpendsItemCharge") && Calls(engine, "SpendsItemCharge", "get_IsSpendCharges") &&
                  Calls(engine, "AddTeleportApproach", "TeleportReserve") && Calls(engine, "AddTeleportApproach", "BelowItemBar"),
                  "an item teleport passes the item settings, the UMD chance and slots-first, and pays for its charge");

            // --- P1: escapes and casting spots ---
            Check(Calls(engine, "Decide", "AddTeleportEscape") && Calls(engine, "AddTeleportEscape", "QuoteTeleportLanding") &&
                  Calls(engine, "AddTeleportEscape", "TeleportEscapeWorth") && Calls(engine, "AddTeleportEscape", "ProvokeLoss") &&
                  Calls(engine, "AddTeleportEscape", "SpotDrop") && Calls(engine, "AddTeleportEscape", "LeavesAlliesCost") &&
                  Calls(engine, "AddMovementCandidates", "NoteEscape") && Calls(engine, "AddLeaveAreaCandidate", "NoteEscape") &&
                  Calls(engine, "AddOutOfReachCandidate", "NoteEscape") && Calls(engine, "AddOutOfSwarmCandidate", "NoteEscape"),
                  "an escape walk (withdraw, area exit, reach, swarm) can be replaced by a teleport to its spot, the cast's provoke priced");
            Check(Calls(engine, "EvaluateAbility", "TeleportKeeps") && Calls(engine, "EvaluateAbility", "TeleportReach") &&
                  Calls(engine, "TeleportLandingSafe", "Keeps") && Calls(engine, "ValidateTeleportIssue", "AnchorEnemy"),
                  "a cast that walks to its casting spot can be reached by a teleport; a cone keeps its recipients at the landing");
            Check(!Calls(engine, "TeleportGate", "PureMageWeapon"),
                  "a Pure Mage's teleport is offered (escapes and casting spots; it never builds weapon approaches)");

            failures = failed;
            lines.Add($"V0370-PROPOSALS {lines.Count} checks, {failed} failures");
            return lines;
        }

        private static IEnumerable<Kingmaker.ElementsSystem.GameAction> AllActions(BlueprintBuff buff)
        {
            var stack = new Stack<Kingmaker.ElementsSystem.GameAction>();
            foreach (var c in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<AddFactContextActions>(buff))
                foreach (var a in c.Activated?.Actions ?? new Kingmaker.ElementsSystem.GameAction[0]) stack.Push(a);
            while (stack.Count > 0)
            {
                var a = stack.Pop();
                if (a == null) continue;
                yield return a;
                if (a is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional branch)
                {
                    foreach (var x in branch.IfTrue?.Actions ?? new Kingmaker.ElementsSystem.GameAction[0]) stack.Push(x);
                    foreach (var x in branch.IfFalse?.Actions ?? new Kingmaker.ElementsSystem.GameAction[0]) stack.Push(x);
                }
            }
        }
    }
}
