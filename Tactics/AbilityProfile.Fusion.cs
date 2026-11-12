// ★ Magic Deceiver: one native cast, its selected delivery and the two ordered native RunAction payloads.
// Profiles contain immutable blueprint metadata only. Nothing here creates a spell, edits a blueprint or runs an action.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal MagicHackData Fusion { get; private set; }
        internal string FusionUnsupported { get; private set; }
        internal string FusionIdentity { get; private set; }
        internal bool? FusionTargetFreeContext;
        internal static string FusionIdentityOf(AbilityData ability) => ability?.MagicHackData == null ? "" : GetFusion(ability).FusionIdentity;
        internal enum FusionSavePath { Both, Failed, Passed }
        private Dictionary<GameAction, FusionSavePath> FusionDamagePaths;
        internal FusionSavePath FusionPath(GameAction action) =>
            FusionDamagePaths != null && FusionDamagePaths.TryGetValue(action, out var path) ? path : FusionSavePath.Both;

        private sealed class FusionEntry
        {
            internal Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility Holder;
            internal MagicHackData Snapshot;
            internal AbilityProfile Profile;
            internal bool Matches(AbilityData ability)
            {
                var live = ability.MagicHackData;
                return Holder == ability.Blueprint && Snapshot.Spell1 == live.Spell1 && Snapshot.Spell2 == live.Spell2 &&
                    Snapshot.DeliverBlueprint == live.DeliverBlueprint && Snapshot.AdditionalAoeBlueprint == live.AdditionalAoeBlueprint &&
                    Snapshot.SpellLevel == live.SpellLevel && Snapshot.SpellSchool == live.SpellSchool &&
                    Snapshot.SavingThrowType == live.SavingThrowType && Snapshot.SpellTargetType == live.SpellTargetType && Snapshot.IsTouch == live.IsTouch;
            }
        }

        private static ConditionalWeakTable<MagicHackData, FusionEntry> FusionCache = new ConditionalWeakTable<MagicHackData, FusionEntry>();

        private static AbilityProfile GetFusion(AbilityData ability)
        {
            var data = ability.MagicHackData;
            if (FusionCache.TryGetValue(data, out var old) && old.Matches(ability)) return old.Profile;
            var snapshot = data.Clone();
            var profile = new AbilityProfile(snapshot.DeliverBlueprint ?? ability.Blueprint) {
                Fusion = snapshot,
                FusionIdentity = $"|fusion:{snapshot.Spell1?.AssetGuid}:{snapshot.Spell2?.AssetGuid}:{snapshot.DeliverBlueprint?.AssetGuid}:" +
                    $"{snapshot.AdditionalAoeBlueprint?.AssetGuid}:{snapshot.SpellLevel}:{snapshot.SpellSchool}:{snapshot.SavingThrowType}:{snapshot.SpellTargetType}:{snapshot.IsTouch}"
            };
            try
            {
                if (snapshot.DeliverBlueprint == null || snapshot.Spell1 == null || snapshot.Spell2 == null)
                    profile.FusionUnsupported = "incomplete native fusion data";
                else
                {
                    profile.Analyze();
                    if (snapshot.IsTouch) profile.NeedsAttackRoll = true;
                    if (snapshot.DeliverBlueprint.GetComponent<AbilityDeliverProjectile>()?.Type == AbilityProjectileType.Line)
                        profile.FusionUnsupported = "line delivery geometry";
                    if (snapshot.AdditionalAoeBlueprint != null) profile.FusionUnsupported = "additional area delivery";
                    if (profile.Heals.Count > 0 || profile.CasterHeals.Count > 0 || profile.CasterBuffs.Count > 0)
                        profile.FusionUnsupported = "healing or caster-side payload";
                }
            }
            catch (Exception ex) { profile.FusionUnsupported = "profile reader " + ex.GetType().Name; }
            FusionCache.Remove(data);
            FusionCache.Add(data, new FusionEntry { Holder = ability.Blueprint, Snapshot = snapshot, Profile = profile });
            _built++;
            return profile;
        }

        /// <summary>AbilityEffectRunAction.Apply uses each spell's own RunAction, falling back to its sticky-touch
        /// delivery only when that component is absent. It does not cast the two source spells separately.</summary>
        private void ReadFusionActions(HashSet<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility> visited, int depth, EffectSubject subject)
        {
            foreach (var spell in new[] { Fusion.Spell1, Fusion.Spell2 })
            {
                if (spell.GetComponent<AdditionalAbilityEffectRunActionOnClickedTarget>() != null)
                    FusionUnsupported = "clicked-target secondary payload";
                var run = spell.GetComponent<AbilityEffectRunAction>() ??
                          spell.GetComponent<AbilityEffectStickyTouch>()?.TouchDeliveryAbility?.GetComponent<AbilityEffectRunAction>();
                if (run == null) { FusionUnsupported = "source spell without a native RunAction payload"; continue; }
                InspectFusionPaths(run.Actions?.Actions, FusionSavePath.Both, 0);
                // Separate visited sets preserve both applications when the two payloads reference a common child.
                Walk(run.Actions?.Actions, new HashSet<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>(visited),
                     depth, subject, 0, NoGuards);
            }
        }

        private void InspectFusionPaths(GameAction[] actions, FusionSavePath path, int depth)
        {
            if (actions == null) return;
            if (depth > 12) { FusionUnsupported = "payload recursion limit"; return; }
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action is ContextActionConditionalSaved saved)
                {
                    if (path != FusionSavePath.Both) FusionUnsupported = "nested shared-save branch";
                    // Repeated branches on the same native shared save cannot have opposite outcomes.
                    if (path != FusionSavePath.Passed) InspectFusionPaths(saved.Failed?.Actions, FusionSavePath.Failed, depth + 1);
                    if (path != FusionSavePath.Failed) InspectFusionPaths(saved.Succeed?.Actions, FusionSavePath.Passed, depth + 1);
                    continue;
                }
                if (action is ContextActionSavingThrow saving &&
                    (saving.MakeSaveAlsoForMagicHack || Fusion.SavingThrowType == SavingThrowType.Unknown))
                    FusionUnsupported = "independent nested saving throw";
                if (action is ContextActionSpawnAreaEffect || action is ContextActionSpawnMonster)
                    FusionUnsupported = "spawned area or creature payload";
                foreach (var referenced in ReferencedAbilities(action))
                    if (referenced != null) FusionUnsupported = "secondary spell payload";
                if (action is ContextActionApplyBuff applied && !applied.ToCaster && path != FusionSavePath.Failed &&
                    Fusion.SavingThrowType != SavingThrowType.Unknown)
                    FusionUnsupported = "buff outside the shared failed-save branch";
                if (action is ContextActionKill && path != FusionSavePath.Failed && Fusion.SavingThrowType != SavingThrowType.Unknown)
                    FusionUnsupported = "kill outside the shared failed-save branch";
                if (action is ContextActionDealDamage || action is IDealDamageProvider)
                {
                    if (FusionDamagePaths == null) FusionDamagePaths = new Dictionary<GameAction, FusionSavePath>();
                    if (FusionDamagePaths.TryGetValue(action, out var previous) && previous != path)
                        FusionUnsupported = "shared damage action on different save branches";
                    else FusionDamagePaths[action] = path;
                }
                foreach (var child in ChildActionLists(action)) InspectFusionPaths(child, path, depth + 1);
            }
        }
    }
}
