# AI Control - Fully Autonomous Combat Companions

*Pathfinder: Wrath of the Righteous* · by MonsieurAndroid · Unity Mod Manager id `CompanionAI_Pathfinder`

Unity Mod Manager mod that replaces party-companion combat AI with a utility-scoring tactical AI
(role-based DPS / Tank / Support plans, buff/heal/debuff selection, positioning). Works in turn-based
combat (primary) and real-time combat.

**0.3.0** replaces the turn-based decision making with a step-wise tactical engine
([docs/TACTICAL_AI.md](docs/TACTICAL_AI.md)):
- **Weapon attacks** (single, move + attack, full attack, charge) valued with the game's own attack and damage rules.
  Archers now shoot, and animal companions bite and claw.
- **Spells and abilities** chosen by expected effect: damage, hard control, heals, buffs. No re-casting buffs that are
  already active, and no spamming utility abilities such as Appraise Foe.
- **Consumables:** potions, scrolls, wands and alchemical/thrown items **in quick slots**, used when they are worth it
  (for example a healing potion at low HP). Each type can be toggled.
- **Positioning:** 5-foot step out of melee for archers and casters, advance into reach or range when nothing can be
  hit, withdraw at very low HP.
- **Ridden mounts** no longer follow the mouse cursor when an AI-controlled rider gives an order.

**0.3.1** fixes what the first 0.3.0 play logs showed ([docs/TACTICAL_AI.md](docs/TACTICAL_AI.md) §6):
- No more healing potions drunk at full HP.
- Cure / Inflict Wounds and Lay On Hands are judged per target (Inflict heals undead, Cure harms them), and heals are
  never aimed at enemies.
- A command that ends without using its action (Albedo's repeated 0.7 m "Move+Attack") is not retried.
- Long walks are no longer cut off by the wait timeout, and an idle leftover command no longer costs a turn.
- Aid Another and other one-round marks are valued as one round; one-round self-buffs such as Perfect Strike are not
  cast when no attack is left this turn.
- Condition-removal abilities (e.g. Treat Affliction, if it removes by descriptor) only when there is something to remove.

**0.3.2** fixes what the first 0.3.1 log showed ([docs/TACTICAL_AI.md](docs/TACTICAL_AI.md) §7):
- Buffs are valued by what they actually do and whether they stack: no Treat Affliction "just because", no Feather Step
  or Mage Armor (over real armor) items wasted.
- Orvath no longer flips Shift Form between Overlord and Mortal Guise every turn; party-wide self buffs (Soul Power)
  no longer count enemies in their radius as "marks".
- An advance that runs out of its move action can continue with the standard action; abilities are not aimed at a
  target on which none of their effects apply.

**0.3.18** ([release/CHANGELOG_v0.3.18.md](release/CHANGELOG_v0.3.18.md), includes 0.3.17): Melee
characters walk around Grease, Entangle and clouds, and real-time walks are checked against the path the game takes and
an ally's area spell about to land. Fight length and enemy danger are calibrated from the damage actually dealt. Spells
that do nothing to a target are dropped for it, touch spells walk into reach in real time too, prone units stand up, and
no consumables go to summons. Two new settings let you keep toggles and combat modes to yourself.

**0.3.16** ([release/CHANGELOG_v0.3.16.md](release/CHANGELOG_v0.3.16.md)): casters no longer walk into a crowd to read a
scroll or cast: casting among enemies is weighed by the game's defensive-casting rule and the attacks of everyone around.
Turn-based touch spells walk into reach the way the game measures it and are cast after the walk (fixing turns lost in
0.3.15), no movement is offered after a 5-foot step, paths no longer end on an ally's square, and real-time escapes go
only to walkable spots.

**0.3.15** ([release/CHANGELOG_v0.3.15.md](release/CHANGELOG_v0.3.15.md)): spells and items that did nothing are fixed
or caught. Kineticists no longer cast the Kinetic Blade's internal Fire Blast, touch scrolls and spells on others walk
into reach before casting in turn-based mode, held touch charges are used first and stuck ones removed, and any ability
the game silently does nothing with is logged and dropped after two tries. Melee characters weigh every round they
would spend in Grease or other harmful areas. Crowd control uses each enemy's real duration and saves. Tanks accept
more risk.

**0.3.14** ([release/CHANGELOG_v0.3.14.md](release/CHANGELOG_v0.3.14.md)): real-time combat now uses the turn-based
tactical AI (the old one stays selectable in the settings). Companions handle their own conditions: they stand up when
prone (in turn-based mode they used to lie there until the fight ended), walk out of clouds while nauseated, and skip
actions being staggered or held rules out. Attacks of opportunity are judged along the whole path, with the chance of
being dropped in one blow. Downed allies are revived when they will stay up. Harmful areas can be left, weighing saves,
immunities and the attacks on the way. Spell estimates use spell resistance and penetration, energy resistance and
immunity, and what each enemy actually took.

**0.3.13**: characters no longer walk away from an enemy they are fighting without weighing the attacks of
opportunity that costs (the game itself is asked which enemies would strike): a move out of melee is chosen only when
it is clearly better, and is cheaper to justify for a healthy character than a wounded one. Casters no longer put a
second copy of the same area (Entangle) on enemies already inside their own. Empowered and maximized spells, including
automatic metamagic such as IsekaiMod's "Overpowered Ability - Auto Empower/Maximize", are valued at their real
damage and healing. New option (on by default): animal companions can use the items in their belt slots in combat.

**0.3.12**: spells that entangle, stun or otherwise hinder are no longer cast on the caster or allies as if they were
buffs (Orvath's Entangle), and they now count as offensive for Progressive; control spells are no longer aimed at
enemies immune to them (the immunity check missed nearly every real spell). Real time: "Avoid friendly fire" now works
(area spells were cast onto allies), a queued character no longer gets a new command while it is still carrying one
out, metamagic/item/other-caster copies of a spell no longer share one classification, and the decision-interval
setting is used. Abilities the AI fails to analyse are named in the normal log, and an unreadable settings file is kept
as a backup before defaults replace it.

**0.3.11**: healers no longer waste heals on summons that just died, and value summons below companions; no advancing
towards an enemy that no path can reach this turn.

**0.3.10**: no more lost turns around 5-foot steps (after a step the AI no longer plans movement the game refuses,
and attacks whose path would end out of reach are not issued); badly hurt characters within an enemy's reach withdraw
when that makes them less likely to go down; archers stepping out of melee keep a target in sight.

**0.3.9**: archers take a 5-foot step and still make their full attack; healing weighs who can hit the wounded
character before its next turn (Lann no longer shoots at 25 % HP instead of drinking), and downed allies can be healed
back up; Coup de Grace is used on helpless enemies (also with Tabletop Tweaks); sleeping enemies are not woken by weak
hits or stunned again.

**0.3.8**: Spirit Weapon Enchantment (and other weapon enchantments) is no longer recast while still active; Smite
Evil goes on the enemy the paladin is about to attack; area spells respect who they can affect (Sleep and creatures with
too many hit dice); no "move + attack" in a surprise round, no move offered after a 5-foot step; the log shows which
spells the Progressive policy covers, and describes abilities the AI cannot read yet (Coup de Grace).

**0.3.7**: new per-character **Spell Use: Progressive** option (off by default): casters use the lowest spell level
that has a damage or control spell with a real chance to work, and save higher levels for when lower ones cannot work
or for enemies the game marks as bosses. Area spells (Grease, Web, Stinking Cloud...) are now understood as control,
and not dropped on allies. See [docs/PROGRESSIVE_SPELLS.md](docs/PROGRESSIVE_SPELLS.md).

**0.3.6**: Perfect Strike (and other "once, then gone" buffs) no longer cast after fights; casters step out of melee
before a spell that would provoke, and value such spells a little lower when they cannot; the debug line
`IsReadyForNextAction` is logged only when it changes.

**0.3.5**: the out-of-combat buff routine only keeps up real long-lasting buffs (no more Soul Power, Perfect Strike
or item use after fights); a companion that cannot act ends its turn at once; Treat Affliction is no longer a "utility"
fallback.

**0.3.4**: Camellia uses Spirit Weapon Enchantment again; Mage Armor items are no longer used over real armor;
"dispel"-type abilities (Treat Affliction) are only considered when there is something to remove.

**0.3.3**: Soul Power only when there are fresh corpses to draw on (and worth more with burn); Treat Affliction's
"treated today" marker is not a buff; summon spells count as summons; the out-of-combat routine no longer flips
toggles such as Shift Form; bonus stacking follows the game's own rule (and Tabletop Tweaks' changes to it).

0.2.119 fixed companions not physically moving in turn-based combat ([docs/INVESTIGATION.md](docs/INVESTIGATION.md)).

---

## Install (players)

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) into the game.
2. Take `CompanionAI_Pathfinder_v0.3.18.zip` - prebuilt in `release\`, or build it yourself (below; output in `dist\`).
3. Install it either way:
   - Unity Mod Manager → *Mods* tab → drag the ZIP onto the window / *Install Mod*, **or**
   - extract it to `<game>\Mods\CompanionAI_Pathfinder\` so the folder contains
     `CompanionAI_Pathfinder.dll` and `Info.json`.
4. Start the game and open the mod settings with **Ctrl+F10**.

### Settings (Ctrl+F10)
- *Enable in Turn-Based Mode* / *Enable in Real-Time Mode* - both are honoured from 0.2.119.
- *Control Companions* / *Control Main Character* and per-character AI on/off, role and range preference.
- *Enable Debug Logging* - **off by default**; when on it writes many `[V]` lines (several per decision). Leave it off unless
  you are debugging. Existing installs keep their old `settings.json`, so turn it off there if it was on.

- *Spell Use* (per character, in the character's panel): **Standard** (as before) or **Progressive** (lowest usable
  spell level first for damage/control spells; cantrips, buffs, heals and items unaffected). *Progressive: allow higher
  spell levels against bosses* (global, on by default) frees higher levels for spells that would hit an enemy the game
  marks as a boss.
- *AI may start performances, rage and similar abilities* and *AI may switch combat modes (Power Attack, Fighting
  Defensively, Mobility)* (both on): the AI switches these by itself during fights, also ones you set before the fight,
  and puts them back as you left them when the fight ends. *Leave a toggle alone for the rest of the fight once you switch
  it yourself* (on): a toggle you click during a fight stays as you set it until that fight ends.
- *Consumables (quick-slot items)*: master switch plus potions / scrolls & wands / alchemical & thrown items, and the HP %
  below which a character drinks a healing potion. **Put the items you want companions to use into their quick slots**:
  the game only allows quick-slot items in combat, and never lets animal companions use items.

### Compatibility with other mods
The mod has **no dependencies** besides Unity Mod Manager. Checked against the decompiled builds of
Tabletop Tweaks Core 0.7.15 / Base 2.6.18, IsekaiMod 6.5.0, Demonology 0.1.5, Tome of the Firebird 1.4.18 and
Mystical Mayhem 0.7.0h (with KRXLib):
- None of them patches a method this mod patches (`TurnController.Tick`, `AiBrainController.TickBrain`,
  `CombatController.StartTurn/Activate/Deactivate`, `UnitCommands.Run`), so load order does not matter.
- Their changes to game rules reach the AI automatically, because it asks the game: attack/AC/damage rules, spell
  resistance (`UnitPartSpellResistance.GetValue`), Use Magic Device DC, targeting (`AbilityData.CanTarget`), variants
  (`GetConversions`) and bonus stacking (`ModifierDescriptorHelper.IsStackable`, e.g. TTT's natural-armor rule).
- Mystical Mayhem's changes to action types (`AbilityData.ActionType`, `RequireFullRoundAction`) reach the AI through
  `RuntimeActionType`. Its Borrowed Time extra swift action can make the log report a swift ability as "action still
  unused"; that only stops the same ability on the same target being repeated that turn. Its spell-resistance special
  cases (Draconic Malice, Pact Wizard) are not in the AI's estimate; the game still applies them when the spell lands.
- Abilities added by these mods are read like vanilla ones (effects, buffs, conditions). Conditions the AI cannot
  evaluate are logged once as `[Profile] ... not evaluated` and counted conservatively.
This is a code-level check; it has not been run in the game with these mods.

Units the game is currently controlling itself (confused, panicked, dominated, cutscenes) are left to the game.

---

## Build (developers)

Requirements (Windows 10/11):
- The game installed with Unity Mod Manager applied (the build references the game's own DLLs).
- [.NET SDK 6 or newer](https://dotnet.microsoft.com/download) - e.g. `winget install Microsoft.DotNet.SDK.8`.
  Visual Studio is **not** required (the .NET Framework 4.8 reference assemblies come from NuGet).

```bat
Build.bat
```
or
```powershell
.\Build.ps1                                   # auto-detects the Steam install
.\Build.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure"
.\Build.ps1 -GamePath "<...>" -Deploy         # also copies the mod into <game>\Mods\CompanionAI_Pathfinder
```

The script:
1. finds `Wrath_Data\Managed` (`-GamePath`, `-ManagedPath`, `WRATH_PATH` env var, Steam registry +
   `libraryfolders.vdf`, or an existing `References\Managed`);
2. copies the referenced game assemblies into `References\Managed\` (git-ignored - they are the game's
   copyrighted files; afterwards you can rebuild without the game path);
3. runs `dotnet build -c Release`;
4. writes `dist\CompanionAI_Pathfinder_v<version>.zip` with exactly the layout of the original release
   (`CompanionAI_Pathfinder.dll` and `Info.json` at the archive root) and verifies its entries.

Plain `dotnet build -c Release -p:GamePath="<game folder>"` also works; output goes to `bin\Release\`.

Reference assemblies used (all from `<game>\Wrath_Data\Managed`): `Assembly-CSharp`, `Owlcat.Runtime.Core/UI/Visual/Validation`,
`Owlcat.SharedTypes`, `AstarPathfindingProject`, `UniRx`, `Newtonsoft.Json` (the game's own 8.0.0.0 build - the mod
must bind to it, not to the NuGet 13.0 package), `UnityEngine*.dll`, and `UnityModManager\UnityModManager.dll`.
Harmony comes from NuGet as compile-only; Unity Mod Manager provides it at runtime.

---

## In-game test checklist (turn-based first)

Every decision and every command is logged, so one fight shows why a companion did something. In the Unity Mod
Manager log (`<game>\Wrath_Data\Managed\UnityModManager\Log.txt`, next to `UnityModManager.dll`, or the in-game UMM
*Logs* tab) look for:

```
[Tactics] Lann R2 HP100% [SMWF5] enemies=4 rounds~2.6 diff=0.71 :: FullAttack(2)->Zombie=21.4 | Attack->Zombie=13.2 => FullAttack(2)->Zombie
[Cmd] Lann: ISSUED UnitAttack 'FullAttack(2)->Zombie' mode=TB/Preparing forcedPath=none approachR=31.0
[Cmd] Lann: ENDED UnitAttack 'FullAttack(2)->Zombie' result=Success acted=True moved=0.0m after 2.4s
```
`[SMWF5]` = Standard, Move, sWift, Full attack and 5-foot step still available. The options are listed best first, with
their values, and `=>` shows the choice. See [docs/TACTICAL_AI.md](docs/TACTICAL_AI.md) §5.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Archer (Lann) with enemies in bow range | Shoots (full attack if he didn't move); doesn't retreat while there are targets |
| 2 | Archer in melee | 5-foot step out of melee, then shoots |
| 3 | Animal companion (Albedo) | Moves in and attacks (full attack when adjacent, charge when far); no wandering |
| 4 | Melee (Seelah) | Smite Evil (swift) when valid, then attacks; charges distant targets |
| 5 | Caster (Orvath) | Damage or control spells on the best target; Appraise Foe at most once per combat; no buff recast while active |
| 6 | Support / healer | Heals allies below the threshold first; buffs early in the fight |
| 7 | Healing potion in a quick slot, character below the potion threshold | Drinks it when that beats attacking |
| 8 | Consumables toggled off | No item use in turn-based or real-time mode |
| 9 | Ridden mount with an AI-controlled rider | Mount moves where the AI ordered, not to the cursor (`[Cmd] ... mount command ... given forcedPath=`) |
| 10 | Player delays a companion's turn / combat start and end / switching TB ↔ RT | Turns proceed normally |

## Architecture

```
TurnController.Tick (postfix)  ──► TurnBasedController ─► Tactics/TacticalEngine (one action at a time)
AiBrainController.TickBrain (prefix, real-time) ─► RealTimeController ─► UnifiedDecisionEngine
                                         │
                                         ▼
                     ActionExecutor / RealTimeController
                                         │
                                         ▼
          GameInterface/CommandDispatcher  (Init → ForcedPath in turn-based → UnitCommands.Run → verify)
                                         │
                                         ▼
                 GameInterface/CommandTrace (EventBus: ISSUED / REJECTED / ENDED log lines)
```

See `ARCHITECTURE.md` (Korean) for the decision/scoring pipeline.

## License / credits

MIT. **AI Control - Fully Autonomous Combat Companions** (<https://github.com/recovribox-svg/AIControll-FACC>).

Contributors:
- **MonsieurAndroid** - author: direction, design decisions, play-testing and logs.
- **Claude** (Anthropic's AI coding assistant, via Claude Code) - code, analysis and documentation. The mod is
  AI-coded.

Based on the original CompanionAI for Pathfinder by veria (seyelive5), which built on patterns from CompanionAI for
Rogue Trader.
