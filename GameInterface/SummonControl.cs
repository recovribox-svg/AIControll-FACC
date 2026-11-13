// ★ summon control (docs/SUMMON_CONTROL_PLAN.md, docs/TACTICAL_AI.md §152): which summoned creatures AI Control drives,
// whose settings and blocklists they use, and which abilities they never use. Opt-in (ModSettings.ControlSummons, off by
// default); the rules themselves are pure (Tactics/SummonRules.cs, unit-tested) - this file only reads the game.
//
// From the game (decompiled 2.7.0x):
//  - RuleSummonUnit.OnTrigger gives every spawned creature a UnitPartSummonedMonster and, unless DoNotLinkToCaster, the
//    initiator's faction and group; Init stores the initiator as Summoner (the creature itself when not linked).
//    ContextActionSpawnMonster - the action of the base game's summoning spells and of the Demonology mod's rituals -
//    goes through that rule.
//  - UnitEntityData.IsDirectlyControllable is false for a summoned creature whose blueprint faction is not
//    controllable: the game's brain drives it in both modes (AiBrainController.TickBrain; in turn-based combat through
//    TurnController.Prepare / Tick -> ForceTick for the current, not directly controllable combatant).
//  - IsPet reads UnitPartPet: a pet keeps the path it always had, also when a mod gives it the summon part too.
// Nothing here writes to the game: no faction, group, controllability or status change.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Parts;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Tactics;
using TurnBased.Controllers;
using UnityEngine;

namespace CompanionAI_Pathfinder.GameInterface
{
    public static class SummonControl
    {
        /// <summary>Per creature, held weakly: gone with the creature, never saved, never keyed by its id.</summary>
        private sealed class State
        {
            public int Frame = -1;
            public string Refusal;
            public bool SwarmRead, Swarm;
            public CharacterSettings View;
            public bool AdoptionLogged;
            public int AdoptionFight = -1;
            public string LastRefusalLogged;
        }

        private static readonly ConditionalWeakTable<UnitEntityData, State> States =
            new ConditionalWeakTable<UnitEntityData, State>();

        /// <summary>
        /// Both patches of the turn-based bridge are bound (TurnController.Prepare and TurnController.Tick, next to the
        /// brain prefix). Set by Main; without it no summon the game drives is claimed in turn-based combat.
        /// </summary>
        internal static bool TurnBridgeBound;

        /// <summary>Summons AI Control took over since the fight began (the [Perf] line's cohort note).</summary>
        internal static int AdoptedThisFight;

        /// <summary>A summoned creature that is not a pet: decided by this class alone, never given a settings row.</summary>
        public static bool IsOrdinarySummon(UnitEntityData unit)
        {
            try { return unit != null && unit.Get<UnitPartSummonedMonster>() != null && !unit.IsPet; }
            catch { return false; }
        }

        /// <summary>The creature's summoner, when it is an ordinary summon linked to another unit.</summary>
        public static UnitEntityData OwnerOf(UnitEntityData unit)
        {
            try
            {
                var part = unit?.Get<UnitPartSummonedMonster>();
                if (part == null || unit.IsPet || !part.IsLinkedToSummoner) return null;
                var summoner = part.Summoner;
                return summoner != null && summoner != unit ? summoner : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// AI Control drives this ordinary summon now (checked at every decision and again when a command is issued).
        /// The answer is read once a frame per creature.
        /// </summary>
        public static bool ShouldDrive(UnitEntityData unit, out string reason)
        {
            reason = "no unit";
            if (unit == null) return false;
            var state = States.GetValue(unit, _ => new State());
            int frame = Time.frameCount;
            if (state.Frame != frame)
            {
                state.Frame = frame;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { state.Refusal = SummonRules.Refusal(Read(unit, state)); }
                catch (Exception ex) { state.Refusal = "not readable: " + ex.GetType().Name; }
                // The control check runs outside the engine's decision timer: measured apart ([Perf] cohorts line).
                s_GateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                s_GateChecks++;
                Note(unit, state);
            }
            reason = state.Refusal;
            return reason == null;
        }

        public static bool ShouldDrive(UnitEntityData unit) => ShouldDrive(unit, out _);

        private static SummonFacts Read(UnitEntityData unit, State state)
        {
            var settings = ModSettings.Instance;
            var f = new SummonFacts
            {
                ModEnabled = Main.Enabled,
                FeatureOn = settings?.ControlSummons ?? false,
            };
            // Off (the default): nothing else is read.
            if (!f.ModEnabled || !f.FeatureOn) return f;

            f.ArmyBattle = Kingmaker.Armies.TacticalCombat.TacticalCombatHelper.IsActive;
            f.TurnBased = CombatController.IsInTurnBasedCombat();
            f.ModeOn = f.TurnBased ? settings.EnableInTurnBased : settings.EnableInRealTime;
            f.LegacyRealTime = settings.RealTimeLegacyEngine;
            f.TurnBridgeBound = TurnBridgeBound;
            f.DirectlyControllable = unit.IsDirectlyControllable;

            f.InCombat = unit.IsInCombat;
            f.InGame = unit.IsInGame;
            f.HasBodyAndBrain = unit.View != null && unit.Body != null && unit.Brain != null;
            f.PlayerFaction = unit.IsPlayerFaction;
            var unitState = unit.Descriptor?.State;
            f.Conscious = unitState?.IsConscious ?? false;
            f.FinallyDead = unitState?.IsFinallyDead ?? true;
            f.Detached = unit.IsDetached;
            f.Panicked = unitState?.IsPanicked ?? false;
            f.PreventDirectControl = (bool)unit.PreventDirectControl;
            var confusion = unit.Get<UnitPartConfusion>();
            f.GameDrivesConfusion = confusion != null && confusion.State != ConfusionState.ActNormally;
            // The body kind does not change: read once per creature (its active facts' components, no name list).
            if (!state.SwarmRead)
            {
                state.Swarm = CombatMath.IsSwarmActor(unit);
                state.SwarmRead = true;
            }
            f.Swarm = state.Swarm;
            f.Ridden = unit.Get<UnitPartSaddled>()?.Rider != null;

            var part = unit.Get<UnitPartSummonedMonster>();
            var summoner = part?.Summoner;
            f.HasSummoner = summoner != null;
            f.SummonerIsSelf = part == null || !part.IsLinkedToSummoner || summoner == unit;
            if (summoner == null || f.SummonerIsSelf) return f;
            f.SummonerIsSummon = summoner.Get<UnitPartSummonedMonster>() != null;
            f.SummonerIsMain = summoner.IsMainCharacter;
            f.SummonerInParty = f.SummonerIsMain || InParty(summoner);
            f.SummonerActive = summoner.IsInGame && summoner.IsPlayerFaction && (summoner.Descriptor?.State?.IsConscious ?? false);
            f.ControlMain = settings.ControlMainCharacter;
            f.ControlCompanions = settings.ControlCompanions;
            // The summoner's existing row only: nothing is created for this check.
            var row = settings.PeekSettings(summoner.UniqueId);
            f.OwnerRowExists = row != null;
            f.OwnerRowEnabled = row?.EnableCustomAI ?? false;
            return f;
        }

        private static bool InParty(UnitEntityData unit)
        {
            var party = Game.Instance?.Player?.PartyCharacters;
            if (party == null) return false;
            for (int i = 0; i < party.Count; i++)
                if (party[i].Value == unit) return true;
            return false;
        }

        /// <summary>One line when a creature is taken over, and a verbose one when the reason for a refusal changes.</summary>
        private static void Note(UnitEntityData unit, State state)
        {
            if (state.Refusal == null)
            {
                state.LastRefusalLogged = null;
                // Once per creature and fight (a long-lived summon is counted in each fight it is driven in).
                if (state.AdoptionLogged && state.AdoptionFight == s_Fight) return;
                state.AdoptionLogged = true;
                state.AdoptionFight = s_Fight;
                AdoptedThisFight++;
                Main.Log($"[Summon] {unit.CharacterName}: AI Control drives it (summoned by {OwnerOf(unit)?.CharacterName}; " +
                         "its summoner's blocklist and spell policy, its own abilities)");
                return;
            }
            // Released after it was driven: said once, at normal level (the game's brain has it again).
            if (state.AdoptionLogged)
            {
                state.AdoptionLogged = false;
                // The fight ending is not news (every driven summon would say so).
                if (state.Refusal == "not in combat") return;
                Main.Log($"[Summon] {unit.CharacterName}: back with the game's AI - {state.Refusal}");
                state.LastRefusalLogged = state.Refusal;
                return;
            }
            if (!Main.VerboseOn || state.Refusal == state.LastRefusalLogged) return;
            if (!(ModSettings.Instance?.ControlSummons ?? false) || !unit.IsPlayerFaction) return;
            state.LastRefusalLogged = state.Refusal;
            Main.Verbose($"[Summon] {unit.CharacterName}: not driven - {state.Refusal}");
        }

        private static long s_GateTicks;
        private static int s_GateChecks, s_Fight;

        /// <summary>What the control checks of summoned creatures cost in this fight (outside the decision timer).</summary>
        internal static string GateCost() =>
            $"{s_GateChecks} control checks, {s_GateTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms in all";

        /// <summary>Called when a fight starts or ends.</summary>
        internal static void NewFight()
        {
            AdoptedThisFight = 0;
            unchecked { s_Fight++; }
            s_GateTicks = 0;
            s_GateChecks = 0;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Settings: one read path that never creates a row for a temporary creature
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The settings a decision about <paramref name="unit"/> reads. A party member or pet: its own row (created on
        /// first use, as always). An ordinary summon: a temporary, neutral view that is never stored - Mixed range, DPS
        /// role, no Pure Mage (the summoner's Ranged / Pure Mage choice would take a melee summon's natural attacks
        /// away), with the summoner's friendly-fire rule, heal threshold and spell policy. Also for a summon nobody
        /// drives: another caster's decision reads its recipients' settings.
        /// </summary>
        public static CharacterSettings SettingsFor(UnitEntityData unit)
        {
            if (unit == null) return new CharacterSettings();
            if (!IsOrdinarySummon(unit))
                return ModSettings.Instance?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName) ?? new CharacterSettings();
            var state = States.GetValue(unit, _ => new State());
            var view = state.View ?? (state.View = new CharacterSettings());
            view.CharacterName = unit.CharacterName ?? "";
            var owner = OwnerOf(unit);
            var row = owner != null ? ModSettings.Instance?.PeekSettings(owner.UniqueId) : null;
            SummonSettingsRule.Refresh(view, row);
            return view;
        }

        /// <summary>The id whose ability blocklist applies to this unit: an ordinary summon uses its summoner's.</summary>
        public static string BlockScopeId(UnitEntityData unit)
        {
            if (unit == null) return null;
            var owner = OwnerOf(unit);
            return owner != null ? owner.UniqueId : unit.UniqueId;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Abilities a summon never uses
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Why an ordinary summon does not use this ability (null when it may; always null for other units).</summary>
        public static string AbilityRefusal(UnitEntityData unit, AbilityData ability)
        {
            if (ability == null || !IsOrdinarySummon(unit)) return null;
            return RefusalOf(ability);
        }

        private static string RefusalOf(AbilityData ability)
        {
            try
            {
                return SummonRules.AbilityRefusal(AbilityProfile.Get(ability)?.Summons ?? false,
                                                  ability.SourceItem != null, ability.RequireMaterialComponent);
            }
            catch (Exception ex)
            {
                // Unreadable: not offered to a summon.
                return "not readable for a summoned creature: " + ex.GetType().Name;
            }
        }

        /// <summary>The abilities an ordinary summon may be offered (the same list for every other unit).</summary>
        public static List<AbilityData> Allowed(UnitEntityData unit, List<AbilityData> abilities)
        {
            if (abilities == null || !IsOrdinarySummon(unit)) return abilities;
            List<AbilityData> result = null;
            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                string why = ability == null ? null : RefusalOf(ability);
                if (why == null)
                {
                    result?.Add(ability);
                    continue;
                }
                if (result == null)
                {
                    result = new List<AbilityData>(abilities.Count);
                    for (int j = 0; j < i; j++) result.Add(abilities[j]);
                }
                // Keyed by the creature's kind, not its id: a pack of one kind says it once, and nothing grows per summon.
                AbilityDiscovery.ReportOnce($"summon-skip|{unit.Blueprint?.AssetGuid}|{ability.Blueprint?.AssetGuid}",
                    () => $"[Summon] {unit.CharacterName}: {ability.Name} not offered - {why}");
            }
            return result ?? abilities;
        }
    }

    /// <summary>What the temporary settings view of a summon takes from its summoner's row (pure, unit-tested).</summary>
    public static class SummonSettingsRule
    {
        public static void Refresh(CharacterSettings view, CharacterSettings ownerRow)
        {
            // Neutral combat style: the creature's own weapons and spells compete by value.
            view.CharacterId = "";
            view.EnableCustomAI = true;
            view.RangePreference = RangePreference.Mixed;
            view.PureMage = false;
            view.Role = AIRole.DPS;
            if (view.BlockedAbilities == null || view.BlockedAbilities.Count > 0)
                view.BlockedAbilities = new List<AbilityBlockEntry>();
            // The summoner's choices that are about the party, not about a fighting style.
            var defaults = ownerRow ?? Defaults;
            view.AvoidFriendlyFire = defaults.AvoidFriendlyFire;
            view.HealAtHPPercent = defaults.HealAtHPPercent;
            view.SpellPolicy = defaults.SpellPolicy;
        }

        private static readonly CharacterSettings Defaults = new CharacterSettings();
    }
}
