// ★ summon control (docs/SUMMON_CONTROL_PLAN.md, docs/TACTICAL_AI.md §152): the pure rules of the opt-in "AI Control
// drives the party's summoned creatures" feature. No game types: the facts are read by GameInterface/SummonControl.cs
// and judged here, so every boundary is unit-tested (tests/CompanionAI.Tests/SummonRulesTests.cs).
//
// Scope of the first slice: a creature that carries the game's summon part (UnitPartSummonedMonster, set by
// RuleSummonUnit), is not a pet, and is linked to a summoner that is the main character or a current party member whose
// own AI Control is on. Everything else stays with the game's brain: pets keep their own path; enemy, neutral, unlinked,
// nested (a summon's summon), swarm-bodied, ridden and game-driven (confused, panicked, detached) summons are refused.
using System.Collections.Generic;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>What the game says about a summoned creature and its summoner at this moment.</summary>
    public struct SummonFacts
    {
        // --- the feature and the mode ---
        public bool FeatureOn;          // ModSettings.ControlSummons
        public bool ModEnabled;         // Main.Enabled
        public bool TurnBased;          // the fight is turn-based now
        public bool ModeOn;             // "Enable in Turn-Based Mode" / "Enable in Real-Time Mode" for the current mode
        public bool LegacyRealTime;     // "Real time: use the old decision engine"
        public bool TurnBridgeBound;    // both turn-based patches of the bridge are bound (TurnController.Prepare + Tick)
        public bool DirectlyControllable; // another mod made it clickable (ToyBox): the companion path drives it
        public bool ArmyBattle;

        // --- the creature ---
        public bool InCombat;
        public bool InGame;
        public bool HasBodyAndBrain;    // view, body and brain present
        public bool PlayerFaction;
        public bool Conscious;
        public bool FinallyDead;
        public bool Detached;
        public bool Panicked;
        public bool PreventDirectControl;
        public bool GameDrivesConfusion; // a confusion part whose state is not "act normally"
        public bool Swarm;              // an active swarm body or swarm round attack
        public bool Ridden;             // it carries a rider: the rider's turn owns it

        // --- the summoner ---
        public bool HasSummoner;
        public bool SummonerIsSelf;     // not linked (RuleSummonUnit.DoNotLinkToCaster)
        public bool SummonerIsSummon;   // nested: the summoner carries the summon part itself
        public bool SummonerInParty;    // the main character or a member of Player.PartyCharacters
        public bool SummonerIsMain;
        public bool SummonerActive;     // in game, conscious, player faction
        public bool ControlMain;        // ModSettings.ControlMainCharacter
        public bool ControlCompanions;  // ModSettings.ControlCompanions
        public bool OwnerRowExists;     // the summoner already has a character row (never created for this check)
        public bool OwnerRowEnabled;    // ... and its "AI Control" switch is on
    }

    public static class SummonRules
    {
        /// <summary>
        /// Why AI Control does not drive this summon now, or null when it does. The order is the order of the log line a
        /// player reads: the feature first, then the mode, then the creature, then its summoner.
        /// </summary>
        public static string Refusal(in SummonFacts f)
        {
            if (!f.ModEnabled) return "the mod is off";
            if (!f.FeatureOn) return "summon control is off";
            if (f.ArmyBattle) return "army battle";
            if (!f.InCombat) return "not in combat";
            if (!f.ModeOn) return f.TurnBased ? "turn-based mode is off" : "real-time mode is off";
            if (!f.TurnBased && f.LegacyRealTime) return "the old real-time engine is selected (it does not drive summons)";
            if (f.TurnBased && !f.DirectlyControllable && !f.TurnBridgeBound) return "the turn-based summon bridge is not bound";

            if (!f.InGame || !f.HasBodyAndBrain) return "not a usable actor";
            if (!f.PlayerFaction) return "not in the party's faction";
            if (f.FinallyDead) return "dead";
            if (!f.Conscious) return "unconscious";
            if (f.Detached) return "detached";
            if (f.Panicked) return "panicked";
            if (f.PreventDirectControl) return "the game prevents control";
            if (f.GameDrivesConfusion) return "confused";
            if (f.Swarm) return "a swarm";
            if (f.Ridden) return "it carries a rider";

            if (!f.HasSummoner || f.SummonerIsSelf) return "not linked to a summoner";
            if (f.SummonerIsSummon) return "its summoner is a summon itself";
            if (!f.SummonerInParty) return "its summoner is not in the party";
            if (!f.SummonerActive) return "its summoner cannot act for the party now";
            if (f.SummonerIsMain ? !f.ControlMain : !f.ControlCompanions)
                return f.SummonerIsMain ? "main character control is off" : "companion control is off";
            if (!f.OwnerRowExists) return "its summoner has no AI Control settings yet";
            if (!f.OwnerRowEnabled) return "AI is off for its summoner";
            return null;
        }

        /// <summary>
        /// An ability a driven summon never uses, or null. A summon casts no summoning spell (a recognised spawn action:
        /// AbilityProfile.Summons - no summons of summons), and uses nothing that takes an item or a material component:
        /// those come out of the party's inventory, which is not the summon's to spend.
        /// </summary>
        public static string AbilityRefusal(bool summons, bool fromItem, bool needsMaterial)
        {
            if (summons) return "a summoned creature summons nothing";
            if (fromItem) return "a summoned creature uses no item";
            if (needsMaterial) return "a summoned creature spends no material component";
            return null;
        }

        /// <summary>What the turn-based bridge does when the game asks a claimed summon's brain to act.</summary>
        public enum BrainTick
        {
            /// <summary>Not its turn, or not claimed: the game's brain runs.</summary>
            Native,
            /// <summary>The game's brain is held back and nothing is decided (inside TurnController.Prepare, or already served this frame).</summary>
            Hold,
            /// <summary>The existing turn-based driver decides now.</summary>
            Decide,
        }

        /// <summary>
        /// The game calls the brain of the current, not directly controllable combatant from TurnController.Prepare (before
        /// the turn's status is set) and from TurnController.Tick while it acts with no command. A decision is taken only
        /// in the second case, once a frame; the Prepare call is answered by the bridge's Prepare postfix, after the game
        /// has finished preparing the turn.
        /// </summary>
        public static BrainTick OnBrainTick(bool eligible, bool isCurrentRider, bool insidePrepare, bool acting,
                                            bool servedThisFrame, bool turnFinished)
        {
            if (!eligible || !isCurrentRider) return BrainTick.Native;
            if (insidePrepare || !acting || servedThisFrame || turnFinished) return BrainTick.Hold;
            return BrainTick.Decide;
        }

        /// <summary>
        /// Ids whose real-time pacing entries are dropped when a fight starts or ends: those noted for summons. Companions
        /// keep theirs (they pace the out-of-combat upkeep too).
        /// </summary>
        public static int Prune<T>(IDictionary<string, T> pacing, ICollection<string> summonIds)
        {
            int removed = 0;
            if (pacing == null || summonIds == null) return 0;
            foreach (var id in summonIds)
                if (id != null && pacing.Remove(id)) removed++;
            return removed;
        }
    }
}
