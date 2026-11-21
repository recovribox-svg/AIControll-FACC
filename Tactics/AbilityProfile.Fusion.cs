// ★ Magic Deceiver: one native cast, its selected delivery and the two ordered native RunAction payloads.
// Profiles contain immutable blueprint metadata only. Nothing here creates a spell, edits a blueprint or runs an action.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal MagicHackData Fusion { get; private set; }
        internal string FusionUnsupported { get; private set; }
        internal string FusionIdentity { get; private set; }
        // F0: cold metadata diagnostics only. Keep the original last-reason admission rule and normal log unchanged.
        internal sealed class FusionLimitation
        {
            public readonly string Reason, Location;
            internal FusionLimitation(string reason, string location) { Reason = reason; Location = location; }
        }
        private List<FusionLimitation> fusionLimitations;
        internal IReadOnlyList<FusionLimitation> FusionLimitations =>
            (IReadOnlyList<FusionLimitation>)fusionLimitations ?? Array.Empty<FusionLimitation>();
        private void LimitFusion(string reason, string location)
        {
            FusionUnsupported = reason;
            if (fusionLimitations == null) fusionLimitations = new List<FusionLimitation>();
            foreach (var old in fusionLimitations)
                if (old.Reason == reason && old.Location == location) return;
            fusionLimitations.Add(new FusionLimitation(reason, location));
        }
        internal bool? FusionTargetFreeContext;
        internal static string FusionIdentityOf(AbilityData ability) => ability?.MagicHackData == null ? "" : GetFusion(ability).FusionIdentity;
        internal enum FusionSavePath { Both, Failed, Passed }
        private Dictionary<GameAction, FusionSavePath> FusionDamagePaths;
        private Dictionary<GameAction, FusionSavePath> fusionBuffPaths;
        internal bool FusionBuffPathsConsistent { get; private set; } = true;
        /// <summary>★ Fusion F4 (docs/FUSION_OUTCOME_EFFECTS_PLAN.md): the first target buff application that the native
        /// cast runs on a passed root save or on both outcomes. Such a fusion is offered only through the joint outcome
        /// value (TacticalEngine.FusionWithheld); the older forecast multiplies control by the fail chance.</summary>
        internal string FusionOutcomeEffectLocation { get; private set; }
        /// <summary>★ Fusion F7: one source spell spends a shared Hit Dice pool (Sleep). See SharedHdControl.</summary>
        internal bool FusionSharedHd { get; private set; }
        /// <summary>Immutable plan metadata for that pool, built once by SharedHdControl; no actor or context.</summary>
        internal object FusionSharedHdPlan;
        private int fusionLastDamageSource;
        /// <summary>★ Fusion F7: every direct damage action belongs to an earlier source spell than this application, so
        /// the cast's own damage is dealt before the buff exists (ActionList(spell 1, spell 2) runs in order).</summary>
        internal bool FusionDamageBefore(BuffApplication application) =>
            application != null && fusionLastDamageSource < application.SourceOrdinal;
        internal FusionSavePath FusionBuffPath(GameAction action) =>
            fusionBuffPaths != null && fusionBuffPaths.TryGetValue(action, out var path) ? path : FusionSavePath.Both;
        internal bool FusionValueFallbackReported;
        internal bool FusionValueQualifiedReported;
        internal bool? FusionOutcomeMetadataQualified;
        internal string FusionOutcomeMetadataReason;
        internal string FusionGuardRefusal;
        private bool fusionRequiresSavedResult, fusionHasIndependentSave;
        private int fusionSourceOrdinal;
        private int fusionActionOrdinal;
        private bool? fusionIndependentGuards;
        private static readonly Dictionary<Type, FieldInfo[]> FusionConditionReferences = new Dictionary<Type, FieldInfo[]>();

        // F2/F3 does not project a changed target into a later payload's checker. Qualify stable inputs once.
        internal bool FusionIndependentGuards
        {
            get
            {
                if (fusionIndependentGuards.HasValue) return fusionIndependentGuards.Value;
                bool supported = BuffApplications.All(a => StableFusionGuards(a.Guards, FusionSharedHd && SharedHdBuffs.Contains(a.Buff),
                                                                              KillsOnStart(a.Buff))) &&
                    Damage.All(a => !m_Guards.TryGetValue(a, out var guards) || StableFusionGuards(guards, false));
                fusionIndependentGuards = supported;
                return supported;
            }
        }

        private bool StableFusionGuards(EffectGuard[] guards, bool pooled, bool dust = false)
        {
            foreach (var guard in guards)
            {
                if (guard.Checker?.Conditions == null) { FusionGuardRefusal = "roll-dependent guard"; return false; }
                foreach (var condition in guard.Checker.Conditions)
                {
                    if (condition == null) continue;
                    var type = condition.GetType();
                    // ★ fusion2g1 review: CasterHasFact reads the caster's facts. A caster-side payload is already refused
                    // (LimitFusion "healing or caster-side payload"), and the ordinal check below still refuses one an
                    // earlier application could grant. Mystical Mayhem's Cyphermage patch guards both Runes this way.
                    if (type != typeof(ContextConditionHitDice) && type != typeof(ContextConditionHasFact) &&
                        type != typeof(ContextConditionHasBuff) && type != typeof(ContextConditionHasBuffFromCaster) &&
                        type != typeof(ContextConditionAlignment) && type != typeof(ContextConditionIsAlly) &&
                        type != typeof(ContextConditionIsEnemy) && type != typeof(ContextConditionIsCaster) &&
                        type != typeof(ContextConditionCasterHasFact) &&
                        // ★ Fusion coverage C1: the main target is the cast's clicked target (AbilityExecutionContext.MainTarget),
                        // fixed for the whole cast and read against the scored aim (AbilityProfile.AimedAt). Molten Orb's
                        // direct hit against its splash; a ground aim has no main target.
                        type != typeof(ContextConditionIsMainTarget) &&
                        // ★ Fusion F7: a pooled application's "is unconscious" test is the kill dependency the joint
                        // value already carries (no control for a recipient the cast's damage drops).
                        !(pooled && type == typeof(ContextConditionIsUnconscious)) &&
                        // ★ Fusion coverage C3: Disintegrate's "HP left below 0" gate on a buff that only kills on activation
                        // (the dust). On an enemy it is the kill the joint value's damage already carries; on an ally it is
                        // a permanent death, priced and vetoed apart (CombatMath.PermanentDeathRisk, the author's policy).
                        !(dust && type == typeof(ContextConditionCompareTargetHP)))
                    { FusionGuardRefusal = "mutable/unread guard " + type.Name; return false; }
                    if (!FusionConditionReferences.TryGetValue(type, out var fields))
                    {
                        var found = new List<FieldInfo>();
                        for (var level = type; level != null; level = level.BaseType)
                            found.AddRange(level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                .Where(f => typeof(BlueprintReferenceBase).IsAssignableFrom(f.FieldType)));
                        FusionConditionReferences[type] = fields = found.ToArray();
                    }
                    foreach (var field in fields)
                    {
                        var reference = field.GetValue(condition) as BlueprintReferenceBase;
                        if (reference == null) continue;
                        // Comparing immutable ids needs no resolution/load of unrelated race or creature-type facts.
                        if (reference.Guid != BlueprintGuid.Empty && BuffApplications.Any(prior =>
                            prior.ActionOrdinal < guard.ReadOrdinal && prior.Buff.AssetGuid == reference.Guid &&
                            !OppositeBranchOfOneRead(prior, guards)))
                        { FusionGuardRefusal = "earlier application changes a guard fact " + reference.Guid; return false; }
                    }
                }
            }
            return true;
        }

        // ★ Fusion F4 (slice B): an earlier application in the opposite branch of a Conditional that this same source
        // payload read once cannot have run when the guard is reached. Ray of Exhaustion: "already fatigued -> exhausted",
        // else "already exhausted -> nothing", else the save. The same checker read by the other source payload, or
        // read again, is a second native evaluation and stays a dependency.
        private static bool OppositeBranchOfOneRead(BuffApplication prior, EffectGuard[] chain)
        {
            foreach (var mine in chain)
            {
                if (mine.Checker == null || mine.SourceOrdinal == 0 || mine.SourceOrdinal != prior.SourceOrdinal) continue;
                foreach (var theirs in prior.Guards)
                    if (ReferenceEquals(theirs.Checker, mine.Checker) && theirs.OnCaster == mine.OnCaster &&
                        theirs.ReadOrdinal == mine.ReadOrdinal && theirs.SourceOrdinal == mine.SourceOrdinal &&
                        theirs.Branch != mine.Branch) return true;
            }
            return false;
        }

        /// <summary>★ Fusion coverage C2: where a recast on random enemies was read (no credit, no harm), else null.</summary>
        internal string FusionEnemyRecastLocation { get; private set; }

        /// <summary>
        /// ★ Fusion coverage C2: a selector that keeps only the caster's enemies (native RunAction removes every unit that is
        /// not an enemy of the caster) and whose every action is a spell cast with no other effect of its own.
        /// </summary>
        internal static bool IsEnemyOnlyRecast(ContextActionOnRandomTargetsAround random)
        {
            var actions = random?.Actions?.Actions;
            if (random == null || !random.OnEnemies || actions == null || actions.Length == 0) return false;
            foreach (var action in actions)
                if (!(action is ContextActionCastSpell cast) || cast.Spell == null || ChildActionLists(action).Any(list => list != null && list.Length > 0))
                    return false;
            return true;
        }

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
                    profile.LimitFusion("incomplete native fusion data", "configuration");
                else
                {
                    profile.Analyze();
                    if (snapshot.IsTouch) profile.NeedsAttackRoll = true;
                    // ★ Fusion coverage C4: a line delivery is read by the native line query (ProjectileLine); only a line it
                    // does not take (a custom or controlled projectile, several, one that follows the terrain) is withheld.
                    if (snapshot.DeliverBlueprint.GetComponent<AbilityDeliverProjectile>()?.Type == AbilityProjectileType.Line &&
                        !profile.IsProjectileLine)
                        profile.LimitFusion("line delivery geometry" + (profile.UnreadLine != null ? ": " + profile.UnreadLine : ""), "delivery");
                    if (snapshot.AdditionalAoeBlueprint != null) profile.LimitFusion("additional area delivery", "delivery/additional");
                    // ★ Fusion coverage C11: Vampiric Touch's caster-side payload - temporary HP equal to the damage the cast
                    // dealt (TemporaryHitPointsFromAbilityValue, its shared value) - gives the caster something and takes nothing
                    // from anyone. As for an ordinary Vampiric Touch it is not credited (caster buffs of a damaging cast are not
                    // priced), so the fused cast is valued by what it does to its recipients alone. Any other healing or caster
                    // payload stays withheld.
                    if (profile.Heals.Count > 0 || profile.CasterHeals.Count > 0 ||
                        profile.CasterBuffs.Count > 0 && !profile.CasterBuffs.All(OwnResultTemporaryHp))
                        profile.LimitFusion("healing or caster-side payload", "profile/subjects");
                    if (profile.Save == SavingThrowType.Unknown && profile.fusionRequiresSavedResult && !profile.fusionHasIndependentSave)
                        profile.LimitFusion("saved-result branch without a declared root save", "profile/save");
                }
            }
            catch (Exception ex) { profile.LimitFusion("profile reader " + ex.GetType().Name, "profile"); }
            FusionCache.Remove(data);
            FusionCache.Add(data, new FusionEntry { Holder = ability.Blueprint, Snapshot = snapshot, Profile = profile });
            _built++;
            return profile;
        }

        /// <summary>
        /// ★ Fusion coverage C11: a caster buff that only gives temporary HP from the cast's own shared value (Vampiric Touch's
        /// VampiricTouchBuff: TemporaryHitPointsFromAbilityValue, a descriptor, and a disabled trigger that removes itself).
        /// </summary>
        internal static bool OwnResultTemporaryHp(Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff buff)
        {
            if (buff == null) return false;
            bool tempHp = false;
            foreach (var component in buff.ComponentsArray)
            {
                switch (component)
                {
                    case null:
                    case SpellDescriptorComponent _:
                        continue;
                    case Kingmaker.Designers.Mechanics.Buffs.TemporaryHitPointsFromAbilityValue thp
                        when thp.Value != null && thp.Value.ValueType == Kingmaker.UnitLogic.Mechanics.ContextValueType.Shared:
                        tempHp = true;
                        continue;
                    case Kingmaker.UnitLogic.Mechanics.Components.AddIncomingDamageTrigger trigger
                        when trigger.Actions?.Actions != null && trigger.Actions.Actions.All(a => a is ContextActionRemoveSelf):
                        continue;
                    default:
                        return false;
                }
            }
            return tempHp;
        }

        /// <summary>AbilityEffectRunAction.Apply uses each spell's own RunAction, falling back to its sticky-touch
        /// delivery only when that component is absent. It does not cast the two source spells separately.</summary>
        private void ReadFusionActions(HashSet<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility> visited, int depth, EffectSubject subject)
        {
            int sourceIndex = 0;
            foreach (var spell in new[] { Fusion.Spell1, Fusion.Spell2 })
            {
                string source = "source" + (++sourceIndex);
                fusionSourceOrdinal = sourceIndex;
                if (spell.GetComponent<AdditionalAbilityEffectRunActionOnClickedTarget>() != null)
                    LimitFusion("clicked-target secondary payload", source + "/clicked-target");
                var run = spell.GetComponent<AbilityEffectRunAction>() ??
                          spell.GetComponent<AbilityEffectStickyTouch>()?.TouchDeliveryAbility?.GetComponent<AbilityEffectRunAction>();
                if (run == null) { LimitFusion("source spell without a native RunAction payload", source); continue; }
                InspectFusionPaths(run.Actions?.Actions, FusionSavePath.Both, 0, source);
                // Separate visited sets preserve both applications when the two payloads reference a common child.
                Walk(run.Actions?.Actions, new HashSet<Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility>(visited),
                     depth, subject, 0, NoGuards);
            }
        }

        private void InspectFusionPaths(GameAction[] actions, FusionSavePath path, int depth, string location)
        {
            if (actions == null) return;
            if (depth > 12) { LimitFusion("payload recursion limit", location); return; }
            for (int index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                if (action == null) continue;
                string at = location + "/" + index + ":" + action.GetType().Name;
                if (action is ContextActionSelectByValue selection)
                {
                    var floor = ReadValueSelection(selection);
                    if (floor.Application == null) LimitFusion("unread value-selected payload: " + floor.Refusal, at);
                    else InspectFusionPaths(floor.Actions, path, depth + 1, at + "/common-buff-minimum");
                    continue;
                }
                if (action is ContextActionConditionalSaved saved)
                {
                    fusionRequiresSavedResult = true;
                    if (path != FusionSavePath.Both) LimitFusion("nested shared-save branch", at);
                    // Repeated branches on the same native shared save cannot have opposite outcomes.
                    if (path != FusionSavePath.Passed) InspectFusionPaths(saved.Failed?.Actions, FusionSavePath.Failed, depth + 1, at + "/failed");
                    if (path != FusionSavePath.Failed) InspectFusionPaths(saved.Succeed?.Actions, FusionSavePath.Passed, depth + 1, at + "/passed");
                    continue;
                }
                if (action is ContextActionSavingThrow saving &&
                    (saving.MakeSaveAlsoForMagicHack || Fusion.SavingThrowType == SavingThrowType.Unknown))
                {
                    fusionHasIndependentSave = true;
                    LimitFusion("independent nested saving throw", at);
                }
                if (action is ContextActionSpawnAreaEffect || action is ContextActionSpawnMonster)
                    LimitFusion("spawned area or creature payload", at);
                // ★ Fusion coverage C2 (docs/FUSION_COVERAGE_PROGRAMME.md): Hideous Laughter's Best Jokes branch casts the spell
                // again on one random enemy around (ContextActionOnRandomTargetsAround with OnEnemies keeps only the caster's
                // enemies; ContextActionCastSpell). It never reaches an ally, and its own save and target are random: no
                // credit, no ally harm, and the fused profile does not read the recast (Walk). Only a selector whose every
                // action is such a cast qualifies; any other secondary payload stays withheld.
                if (action is ContextActionOnRandomTargetsAround random && IsEnemyOnlyRecast(random))
                {
                    FusionEnemyRecastLocation = FusionEnemyRecastLocation ?? at;
                    continue;
                }
                foreach (var referenced in ReferencedAbilities(action))
                    if (referenced != null) LimitFusion("secondary spell payload", at);
                // ★ Fusion F4: recorded, no longer a refusal. AbilityEffectRunAction rolls one root save and
                // ContextActionApplyBuff has no save test, so the application runs on exactly the path labelled below.
                if (action is ContextActionApplyBuff applied && !applied.ToCaster && path != FusionSavePath.Failed &&
                    Fusion.SavingThrowType != SavingThrowType.Unknown && FusionOutcomeEffectLocation == null)
                    FusionOutcomeEffectLocation = at;
                if (action is ContextActionApplyBuff application)
                {
                    if (fusionBuffPaths == null) fusionBuffPaths = new Dictionary<GameAction, FusionSavePath>();
                    if (fusionBuffPaths.TryGetValue(action, out var prior) && prior != path) FusionBuffPathsConsistent = false;
                    else fusionBuffPaths[action] = path;
                }
                if (action is ContextActionKill && path != FusionSavePath.Failed && Fusion.SavingThrowType != SavingThrowType.Unknown)
                    LimitFusion("kill outside the shared failed-save branch", at);
                if (action is ContextActionDealDamage || action is IDealDamageProvider)
                {
                    fusionLastDamageSource = Math.Max(fusionLastDamageSource, fusionSourceOrdinal);
                    if (FusionDamagePaths == null) FusionDamagePaths = new Dictionary<GameAction, FusionSavePath>();
                    if (FusionDamagePaths.TryGetValue(action, out var previous) && previous != path)
                        LimitFusion("shared damage action on different save branches", at);
                    else FusionDamagePaths[action] = path;
                }
                int childIndex = 0;
                foreach (var child in ChildActionLists(action))
                    InspectFusionPaths(child, path, depth + 1, at + "/child" + childIndex++);
            }
        }
    }
}
