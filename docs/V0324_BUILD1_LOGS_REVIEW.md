# Review of the v0.3.24-dev-build1 logs (1 November 2026, Claude)

Inputs: `Turn_Based_Mode_Log_v0.3.24-dev-build1.txt` (12 fights), `Real_Time_Mode_Log_v0.3.24-dev-build1.txt`
(13 fights), the game's `combatLog.txt` (25 combats). Startup line `[Build] v0.3.24 module=05fff211-...`: the right
DLL this time. Same party (Orvath, Aeiluvetar, Seelah, Camellia, Lann). No errors from the mod.

## Build 1's changes, seen in play

- **Summons:** none in turn-based, two in real time, at reduced values (`easy fight x0.69` → 12.4; the second with one
  of Orvath's demons still fighting, `x0.5 ... x0.6` → 3.2). Before, the same fights had summon after summon at 18.0.
- **Treat Affliction:** offered a few times, never chosen.
- **Held states (psionic focus):** not triggered; nobody in this party holds one. (The "spends ..." lines in the
  logs are toggle coverage notes about Lann's and Aeiluvetar's ammunition, a different message.)
- **Companions down:** 6 in 25 combats, against about 13 in the previous logs.

## Fixed: stepping out of melee away from a better option (turn-based)

**What happened.**
- **Fight 4, R3-R4.** Orvath took a 5-foot step out of melee worth 3.1, though his best option was a Color Spray
  needing a short walk (75.7, then 48.9). After a 5-foot step no walk is allowed, so he took a buff and a lesser spell.
- **Fight 12, R6.** He stepped (6.2) away from his full attack on the Abrikandilu (28.3) and cast Grease (12.3).

**Cause.** Not the cast step fixed in v0.3.23 (no provoking cast was the reason here), but the older rule for a
ranged-style character: back out of melee for 40 % of the adjacent enemies' threat, whatever the step gives up.

**Fix.** That step's worth is reduced by how much better the best option it rules out is than the best option left
after it. Ruled out: melee weapon attacks (out of reach after the step) and, turn-based, every option that walks first.
If nothing is lost, the step is worth what it was.

## Looked at, no change

- **Lann ending turns idle (fights 9-10).** Once because a sleeping Corrupted Mongrel Ranger was left for Camellia's
  Coup de Grace (she landed it), otherwise because his non-magic arrows do nothing to an incorporeal Ravenous Shadow
  (Aeiluvetar's magic bow was used on it).
- **Real time: 10 stuck commands cancelled** (5 before). Three were Camellia, Seelah and Orvath in turn walking at a
  Cultist Archer on a ledge 4.4 m above them; each was cancelled after 6 s and then used a ranged option. Each unit
  learns only from its own refused walk. **Proposal (needs your agreement, it is pathfinding):** when one unit's walk
  ends far below or above its target, treat that target as out of walking reach for the whole party until it moves.

## Checks run

- Release build: 0 warnings, 0 errors. Unit tests: 258 passed.
- SigDump: unchanged from build 1.
- Not yet run in the game.

## Test build

`test-builds/AI-Control-FACC-v0.3.24-dev-build2-2d6b49b-UMM-test.zip`, UMM 0.3.24, startup
`[Build] v0.3.24 module=c3e6faed-f700-40ff-82b6-3cd864c6a4b0`.

What to watch: Orvath next to an enemy with a better spell or attack available should not step away first.
