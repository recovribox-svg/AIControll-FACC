// ★ summon control: the pure rules of Tactics/SummonRules.cs (docs/TACTICAL_AI.md §152).
using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SummonRulesTests
    {
        /// <summary>A linked summon of an AI-driven companion, in a real-time fight on the current engine.</summary>
        private static SummonFacts Driven() => new SummonFacts
        {
            FeatureOn = true, ModEnabled = true, TurnBased = false, ModeOn = true, LegacyRealTime = false,
            TurnBridgeBound = true, DirectlyControllable = false, ArmyBattle = false,
            InCombat = true, InGame = true, HasBodyAndBrain = true, PlayerFaction = true, Conscious = true,
            HasSummoner = true, SummonerInParty = true, SummonerActive = true,
            ControlMain = false, ControlCompanions = true, OwnerRowExists = true, OwnerRowEnabled = true,
        };

        [Fact]
        public void A_linked_summon_of_a_driven_companion_is_driven_in_both_modes()
        {
            Assert.Null(SummonRules.Refusal(Driven()));
            var turnBased = Driven(); turnBased.TurnBased = true;
            Assert.Null(SummonRules.Refusal(turnBased));
        }

        [Fact]
        public void Off_by_default_and_with_the_mod_off()
        {
            // A settings file without the switch loads false: default(SummonFacts) is the "nothing set" case.
            Assert.Equal("the mod is off", SummonRules.Refusal(default));
            var off = Driven(); off.FeatureOn = false;
            Assert.Equal("summon control is off", SummonRules.Refusal(off));
            var modOff = Driven(); modOff.ModEnabled = false;
            Assert.Equal("the mod is off", SummonRules.Refusal(modOff));
        }

        [Fact]
        public void The_mode_switches_and_the_old_real_time_engine_keep_the_games_ai()
        {
            var rt = Driven(); rt.ModeOn = false;
            Assert.Equal("real-time mode is off", SummonRules.Refusal(rt));
            var tb = Driven(); tb.TurnBased = true; tb.ModeOn = false;
            Assert.Equal("turn-based mode is off", SummonRules.Refusal(tb));
            var legacy = Driven(); legacy.LegacyRealTime = true;
            Assert.Contains("old real-time engine", SummonRules.Refusal(legacy));
            // The old engine is a real-time choice: turn-based combat still uses the tactical engine.
            var legacyTurnBased = Driven(); legacyTurnBased.LegacyRealTime = true; legacyTurnBased.TurnBased = true;
            Assert.Null(SummonRules.Refusal(legacyTurnBased));
        }

        [Fact]
        public void Turn_based_needs_the_bridge_unless_another_mod_made_the_summon_clickable()
        {
            var unbound = Driven(); unbound.TurnBased = true; unbound.TurnBridgeBound = false;
            Assert.Contains("bridge", SummonRules.Refusal(unbound));
            // Real time never needs it.
            var realTime = Driven(); realTime.TurnBridgeBound = false;
            Assert.Null(SummonRules.Refusal(realTime));
            // Clickable (ToyBox): the companion path drives it, bridge or not.
            unbound.DirectlyControllable = true;
            Assert.Null(SummonRules.Refusal(unbound));
        }

        [Fact]
        public void Out_of_combat_and_army_battles_are_never_driven()
        {
            var idle = Driven(); idle.InCombat = false;
            Assert.Equal("not in combat", SummonRules.Refusal(idle));
            var army = Driven(); army.ArmyBattle = true;
            Assert.Equal("army battle", SummonRules.Refusal(army));
        }

        [Fact]
        public void The_creature_must_be_a_usable_party_side_actor_the_game_does_not_drive_itself()
        {
            void Refused(System.Action<SummonFacts[]> change, string expected)
            {
                var f = new[] { Driven() };
                change(f);
                Assert.Equal(expected, SummonRules.Refusal(f[0]));
            }
            Refused(f => f[0].InGame = false, "not a usable actor");
            Refused(f => f[0].HasBodyAndBrain = false, "not a usable actor");
            Refused(f => f[0].PlayerFaction = false, "not in the party's faction");   // hostile, neutral, charmed away
            Refused(f => f[0].FinallyDead = true, "dead");
            Refused(f => f[0].Conscious = false, "unconscious");
            Refused(f => f[0].Detached = true, "detached");
            Refused(f => f[0].Panicked = true, "panicked");
            Refused(f => f[0].PreventDirectControl = true, "the game prevents control");
            Refused(f => f[0].GameDrivesConfusion = true, "confused");
            Refused(f => f[0].Swarm = true, "a swarm");
            Refused(f => f[0].Ridden = true, "it carries a rider");
        }

        [Fact]
        public void The_summoner_must_be_a_linked_party_member_whose_ai_is_on()
        {
            var noLink = Driven(); noLink.HasSummoner = false;
            Assert.Equal("not linked to a summoner", SummonRules.Refusal(noLink));
            var self = Driven(); self.SummonerIsSelf = true;     // scripted / DoNotLinkToCaster
            Assert.Equal("not linked to a summoner", SummonRules.Refusal(self));
            var nested = Driven(); nested.SummonerIsSummon = true;
            Assert.Equal("its summoner is a summon itself", SummonRules.Refusal(nested));
            var stranger = Driven(); stranger.SummonerInParty = false;   // an enemy's or a guest's summon
            Assert.Equal("its summoner is not in the party", SummonRules.Refusal(stranger));
            var down = Driven(); down.SummonerActive = false;            // dead, unconscious, charmed away
            Assert.Equal("its summoner cannot act for the party now", SummonRules.Refusal(down));
        }

        [Fact]
        public void The_summoners_own_control_switches_decide_main_versus_companion()
        {
            var companion = Driven(); companion.ControlCompanions = false; companion.ControlMain = true;
            Assert.Equal("companion control is off", SummonRules.Refusal(companion));
            var main = Driven(); main.SummonerIsMain = true;             // ControlMain is false in Driven()
            Assert.Equal("main character control is off", SummonRules.Refusal(main));
            main.ControlMain = true; main.ControlCompanions = false;
            Assert.Null(SummonRules.Refusal(main));
        }

        [Fact]
        public void A_summoner_without_a_settings_row_defers_and_a_switched_off_one_refuses()
        {
            var noRow = Driven(); noRow.OwnerRowExists = false; noRow.OwnerRowEnabled = false;
            Assert.Equal("its summoner has no AI Control settings yet", SummonRules.Refusal(noRow));
            var off = Driven(); off.OwnerRowEnabled = false;
            Assert.Equal("AI is off for its summoner", SummonRules.Refusal(off));
        }

        [Fact]
        public void A_summon_never_summons_or_spends_the_partys_items_and_materials()
        {
            Assert.Null(SummonRules.AbilityRefusal(false, false, false));
            Assert.Contains("summons nothing", SummonRules.AbilityRefusal(true, false, false));
            Assert.Contains("no item", SummonRules.AbilityRefusal(false, true, false));
            Assert.Contains("material", SummonRules.AbilityRefusal(false, false, true));
            // The summoning reason comes first: it is the one that would start a chain.
            Assert.Contains("summons nothing", SummonRules.AbilityRefusal(true, true, true));
        }

        [Fact]
        public void The_brain_call_decides_only_for_the_current_claimed_summon_while_it_acts_once_a_frame()
        {
            // Not eligible (bridge unbound) or not its turn: the game's brain runs.
            Assert.Equal(SummonRules.BrainTick.Native, SummonRules.OnBrainTick(false, true, false, true, false, false));
            Assert.Equal(SummonRules.BrainTick.Native, SummonRules.OnBrainTick(true, false, false, true, false, false));
            // The game's brain call inside Prepare (status not yet set): held, nothing decided.
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, true, false, false, false));
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, true, true, false, false));
            // Acting and idle: the driver decides.
            Assert.Equal(SummonRules.BrainTick.Decide, SummonRules.OnBrainTick(true, true, false, true, false, false));
            // A second call in the same frame (the Prepare postfix already served it), or a turn the driver ended.
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, false, true, true, false));
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, false, true, false, true));
            // Any status but Acting (Scrolling, Ending, Ended): held, the game ends the turn itself.
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, false, false, false, false));
        }

        [Fact]
        public void A_quick_action_then_a_main_action_are_two_decisions_in_two_frames()
        {
            // Frame 1: Prepare postfix served the turn (quick action issued) - the same frame's brain call holds.
            Assert.Equal(SummonRules.BrainTick.Hold, SummonRules.OnBrainTick(true, true, false, true, true, false));
            // Frame N: the quick action ended, no command - the driver decides the main action.
            Assert.Equal(SummonRules.BrainTick.Decide, SummonRules.OnBrainTick(true, true, false, true, false, false));
        }

        [Fact]
        public void Pacing_entries_of_summons_are_dropped_and_companions_keep_theirs()
        {
            var pacing = new Dictionary<string, float> { ["seelah"] = 1f, ["leopard-1"] = 2f, ["leopard-2"] = 3f, ["pet"] = 4f };
            var summons = new HashSet<string> { "leopard-1", "leopard-2", "expired-before-any-entry" };
            Assert.Equal(2, SummonRules.Prune(pacing, summons));
            Assert.Equal(new[] { "pet", "seelah" }, new SortedSet<string>(pacing.Keys));
            // Repeated summon / expire cycles leave nothing behind.
            for (int cycle = 0; cycle < 50; cycle++)
            {
                string id = "summon-" + cycle;
                pacing[id] = cycle;
                Assert.Equal(1, SummonRules.Prune(pacing, new[] { id }));
            }
            Assert.Equal(2, pacing.Count);
            Assert.Equal(0, SummonRules.Prune<float>(null, summons));
            Assert.Equal(0, SummonRules.Prune(pacing, null));
        }
    }
}
