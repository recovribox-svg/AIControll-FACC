# AI Control - Fully Autonomous Combat Companions

A [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) mod for **Pathfinder: Wrath of the Righteous** that
replaces your companions' combat AI with a tactical one. Each companion estimates the effects and risks of its
supported attacks, spells, abilities, items and movement, then chooses among them. It works in **turn-based** and
**real-time** combat.

**Current repository package: v0.3.52** —
[download ZIP](https://github.com/recovribox-svg/AIControll-FACC/raw/refs/heads/main/release/CompanionAI_Pathfinder_v0.3.52.zip) ·
[identity](release/CompanionAI_Pathfinder_v0.3.52.identity.json) ·
[checksums](release/CompanionAI_Pathfinder_v0.3.52.sha256) ·
[changes and playtest checklist](docs/V0352_PLAYTEST_REVIEW.md)

The v0.3.52 package passed automated verification; its new behavior still needs confirmed turn-based and real-time
playtesting. [Validation results](docs/competence/V0352_PLAYTEST_CHECKS.json) record the checks and their limits.
Published tags and release assets are listed separately under [GitHub Releases](https://github.com/recovribox-svg/AIControll-FACC/releases).

---

## Contents

- [What it does](#what-it-does)
- [What's new in v0.3.52](#whats-new-in-v0352)
- [Install](#install)
- [Settings](#settings)
- [Abilities AI must not use](#abilities-ai-must-not-use)
- [Compatibility](#compatibility)
- [Reporting a problem](#reporting-a-problem)
- [Versions and test builds](#versions-and-test-builds)
- [For developers](#for-developers)
- [Credits](#credits)

---

## What it does

**Fights by the game's own rules.**

- **Attacks:** single attacks, full attacks, charges and move-and-attack, judged with the game's attack, damage and
  damage-reduction rules.
- **Spells and abilities:** chosen by their real effect (damage, control, healing, buffs), the enemy's saves, spell
  resistance and immunities.
- **Buffs:** considers existing buffs, stacking and the benefit lost when a new buff replaces or moves an old one.

**Looks after the party.**

- Weighs healing, helping downed allies and removing harmful conditions against other useful actions.

**Moves sensibly.**

- Weighs attacks of opportunity and harmful areas (Grease, clouds) before moving.
- Steps out of melee to shoot or cast, and withdraws when badly hurt.

**Uses your items.**

- Potions, scrolls, wands and bombs **in quick slots**, when they are worth it.

**Handles class features.** Some examples:

- performances, Rage, Smite;
- Power Attack and other combat modes;
- kineticist burn and infusions;
- psionic focus (Psionics mod);
- recognized Magus Spell Combat/Spellstrike mechanics, including ranged variants;
- supported Shifter forms and native Magic Deceiver fusions.

These examples describe supported mechanisms, not complete support for every class, archetype or modded ability.
Grapple maintenance/release and broader summons competence remain ongoing work.

**Lets you choose spell spending.** *Conservative* uses the lowest viable spell level; *Standard* balances normal
spending; *Aggressive* makes offensive spell slots more attractive. Target defenses, resource limits and risks still apply.

**Leaves you in control.**

- You can still give orders yourself.
- You choose which characters the AI controls, and which abilities it must never use.

---

## What's new in v0.3.52

- Death from Above can compare the same supported combat-mode choices as ordinary weapon attacks.
- Combat-mode AC scoring excludes recognized automatic swarm damage while retaining its threat to the party.
- Debug logging suppresses repeated unchanged states and avoids unnecessary formatting when output is disabled.
- Updated diagnostics and [the playtest review](docs/V0352_PLAYTEST_REVIEW.md) document unresolved behavior,
  including Invocation transfers, no-effect Dazzling Display casts and some refused approaches.

The release archive includes a build identity and checksum. Packaging and automated checks do not establish that
every supported mechanic has been exercised in game.

---

## Install

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) for the game (the mod requires version 0.27.0 or newer).
2. Download [CompanionAI_Pathfinder_v0.3.52.zip](https://github.com/recovribox-svg/AIControll-FACC/raw/refs/heads/main/release/CompanionAI_Pathfinder_v0.3.52.zip).
3. **Fully exit the game.** In Unity Mod Manager, open the *Mods* tab and drag the ZIP onto the window.
   - Or extract it to `<game>\Mods\CompanionAI_Pathfinder\`.
4. Start the game and press **Ctrl+F10** to open the settings.

Updating works the same way. Your settings are kept.

> **Tip:** put the potions, scrolls and wands you want companions to use into their **quick slots**. The game only
> lets characters use quick-slot items in combat.

---

## Settings

Open with **Ctrl+F10**. The defaults suit most players.

### Who and when

| Setting | Default | What it does |
|---|---|---|
| Control Companions | On | The AI fights for your companions. |
| Control Main Character | Off | The AI also fights for your main character. |
| Control Summoned Creatures (experimental) | Off | In combat, the AI also decides for creatures summoned by the characters it controls (not pets, swarms or a summon's own summons). They use their own abilities, their summoner's blocklist, friendly-fire rule and spell policy, and never the party's items. Real time needs the current engine. |
| Enable in Turn-Based / Real-Time Mode | On / On | Choose the combat modes the AI runs in. |
| Per character: AI on/off | On | Switch the AI off for single characters in *Party Members*. |

### Per character (click a name in *Party Members*)

| Setting | What it does |
|---|---|
| Role: DPS / Tank / Support | What the character puts first: damage, protecting allies, or healing and buffs. |
| Range: Mixed / Melee / Ranged | Where the character prefers to fight. Ranged remains usable by noncasters and does not itself forbid weapon attacks. |
| Pure Mage (under Ranged) | Optional, off by default. Disables ordinary physical attacks in favor of spells/cantrips; supported exceptions and existing safety checks apply. Mixed/Melee suspend the setting. |
| Spell Use: Conservative / Standard / Aggressive | Controls offensive spell-slot spending. Conservative was previously labelled Progressive. Cantrips, buffs, heals and items retain their own policies. |
| Abilities AI must not use | See [below](#abilities-ai-must-not-use). |

### Behaviour

| Setting | Default | What it does |
|---|---|---|
| AI manages activatable abilities (toggles) | On | The AI switches performances, Rage, Power Attack, Fighting Defensively and similar, and puts them back afterwards. Off: it never touches toggles. |
| Leave a toggle alone once you switch it yourself | On | A toggle you click stays as you set it for the rest of the fight. |
| After fights, recast long-lasting self buffs | On | Missing hour-long and permanent self buffs are recast. Switch off if you buff by hand or with a buff mod. |
| Conservative: allow higher spell levels against bosses | On | Conservative casters may use stronger spells on bosses. |
| Steer my own walk orders around spotted traps | On | Your own walk orders avoid traps the party has spotted. |

### Items

| Setting | Default | What it does |
|---|---|---|
| Use consumables in combat | On | Master switch for quick-slot items. |
| Potions / Scrolls and wands / Alchemical and thrown items | On | Each item type separately. |
| Drink healing potions below HP % | 50 | When a companion drinks a healing potion. |
| Animal companions can use their belt items | On | Pets may use items in their belt slots. |

### Other

- **Pre-Buff Party** (button): the AI-controlled casters put useful low-level buffs on the party before a fight.
- **Enable Debug Logging** (off): writes many extra lines to the log. Switch it on only when you are asked to.
- **Real time: use the old decision engine** (off): only for comparison. The old engine is no longer developed.

---

## Abilities AI must not use

Each character's panel has a searchable list of its spells, abilities, quick-slot items and toggles.

- **Block** an entry for this character only, or for **everyone**.
  - Variant spells (Command, kineticist blasts, ...) can be blocked one variant at a time or all at once.
- A blocked entry is never used by the AI:
  - in turn-based and real-time combat;
  - after fights;
  - with Pre-Buff.
- **Unblock** at any time; it takes effect at once.
- The top-level panel lists the entries blocked for everyone.

What it does **not** change:
- you can still use the ability yourself;
- automatic effects (for example Dreadful Carnage's free Dazzling Display) still happen;
- other mods are not affected;
- buffs that are already on stay on.

---

## Compatibility

- **Requires Unity Mod Manager; no other mod is required.**
- **Tabletop Tweaks (Core/Base), IsekaiMod, Psionics, Mystical Mayhem, DarkCodex, Demonology and Tome of the Firebird:**
  development has inspected these mods' mechanics and used them in modded playtests. Native rule queries pick up
  many runtime changes, but custom components and generated abilities may need dedicated support. This is not a
  guarantee of full compatibility across all versions and settings. [Installed-version evidence](docs/competence/INSTALLED_MODS.md).
- **Wrath Tactics:** in real time its rules act first, and AI Control fills the gaps. You can switch this in the
  settings. A "Do nothing" rule gives no order, so AI Control acts anyway.
- **Buff It 2 The Limit:** the settings page reminds you when it is installed. If it manages toggles or buffs, switch
  off the matching AI Control setting.
- **Psionics:** supported focus/trance mechanics are handled; this does not cover every added power or feature.
- Confused, dominated or panicked companions, and cutscenes, are left to the game.

---

## Reporting a problem

1. Describe what happened, the character/ability involved, the mode, and relevant mod versions/settings. Mention
   combat spawning, rests, manual orders or mode switches during the test.
2. Send `Log.txt` from the affected session:
   - where: `<game>\Wrath_Data\Managed\UnityModManager\Log.txt`, or the *Logs* tab in Unity Mod Manager;
   - its first lines identify your version, for example
     `[Build] v0.3.52 module=981a7b77-a654-4da1-9a22-68bc1c6a77bd`.
3. If you have an exported `combatLog.txt`, include the matching session's file to help compare decisions with
   actual outcomes. Keep different builds/sessions clearly labelled.
4. Debug logging is not needed unless asked for. When requested, enable it before reproducing the issue and retain
   the full startup identity. Do not assume two packages with the same version have the same DLL.

Selected actions are logged with candidate values; quiet real-time waits may be omitted. For example:

```text
[Tactics] Lann R2 HP100% [SMWF5] enemies=4 :: FullAttack(2)->Zombie=21.4 | Attack->Zombie=13.2 => FullAttack(2)->Zombie
```

---

## Versions and test builds

- **Repository packages:** [release folder](release/README.md), including v0.3.50, v0.3.51 and current v0.3.52,
  with identities, checksums and validation limits. Updating `main` does not create a GitHub Release.
- **Published releases:** [GitHub Releases](https://github.com/recovribox-svg/AIControll-FACC/releases).
- **Older release notes:** [v0.3.29](release/CHANGELOG_v0.3.29.md) · [v0.3.28](release/CHANGELOG_v0.3.28.md) · [v0.3.27](release/CHANGELOG_v0.3.27.md) ·
  [v0.3.26](release/CHANGELOG_v0.3.26.md) · [v0.3.25](release/CHANGELOG_v0.3.25.md) ·
  [older](release/).
- **Test builds:** development builds for play-testing, in [test-builds](test-builds/README.md). They are not releases.
  Match each archive's identity/MVID to its log's startup line.
- The previous, more detailed version of this page (version-by-version notes) is
  [archived](docs/archive/README_until_v0.3.26.md).

---

## For developers

The mod is C# (.NET Framework 4.8) and builds against the game's own DLLs.

```powershell
.\Build.ps1                                     # finds the Steam install, builds, writes dist\CompanionAI_Pathfinder_v<version>.zip
.\Build.ps1 -GamePath "<...\Pathfinder Second Adventure>" -Deploy   # also copies the mod into the game's Mods folder
dotnet test tests/CompanionAI.Tests             # unit tests (no game needed)
```

Requirements: the game with Unity Mod Manager installed, and the [.NET SDK](https://dotnet.microsoft.com/download)
8 or newer for the current unit tests and tools. Visual Studio is not needed.

| Where | What |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Architecture overview and the current handover |
| [docs/TACTICAL_AI.md](docs/TACTICAL_AI.md) | How decisions are made and valued |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branches, pull requests and releases |
| [tools/README.md](tools/README.md) | Offline verification tools |
| [docs/CLAUDE_KICKOFF_PROMPT.md](docs/CLAUDE_KICKOFF_PROMPT.md) | Starting instructions for the current Claude development work |
| [docs/CLAUDE_MASTER_PROMPT_V0351.md](docs/CLAUDE_MASTER_PROMPT_V0351.md) | Current ranked backlog, evidence and working principles; filename retained for existing links |

---

## Credits

- **MonsieurAndroid**: author. Direction, design decisions, play-testing and logs.
- **Claude** (Anthropic's AI coding assistant, via Claude Code): code, analysis and documentation. The mod is AI-coded.
- **Codex** (OpenAI): interim development, playtest audits, verification and documentation.
- Based on the original CompanionAI for Pathfinder by veria (seyelive5), which built on patterns from CompanionAI for
  Rogue Trader.

License: MIT.
