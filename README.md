# TravianBotSharp (TBS)

[![alt text][image]][hyperlink]

[hyperlink]: https://discord.gg/mBa4f2K
[image]:https://discordapp.com/api/guilds/740829801446637601/widget.png?style=banner4

**TravianBotSharp** is a **free** Travian bot supports [Travian Official](https://www.travian.com).

## Quick start

1. Download TBS from **GitHub Releases**.
2. Extract files to a local folder.
3. Make sure you are on **Windows** and using the **latest Google Chrome**.
4. Run `TravianBotSharp.exe`.
5. Add your account and start setup.

## Main features

- Multi-account support
- Auto builder (support use resource from hero inventory, instant upgrade)
- Auto start adventures
- Auto start farm lists
- Conquest attack planner with per-wave troop counts and catapult targets
- Auto claim daily quest/ village quest
- Auto NPC
- Auto train troop
- Telegram notifications when the unread report badge increases after starting farm lists
- Telegram alerts for incoming attack/raid after tasks, with a 10-minute fallback

## Tech specification

Currently, TBS only runs on Windows.

## Conquest waves

In **Conquest**, enter troops in the 11 fields of each wave, following the
Rally Point order (`t1` to `t10`, then Hero; `t8` is Catapult).
Troop labels follow the selected origin village's saved tribe, including new
and copied waves. If its tribe has not been identified, labels remain `t1`
to `t10`; Hero always remains the last field. Changing the village updates
labels without changing entered troop counts or catapult targets.
**Add** creates an empty wave; **Copy last wave** duplicates the last wave,
including both catapult targets. Up to eight waves can be planned.
Rows containing only blank or zero troop counts are drafts: **Calculate** and
**Action** ignore them without removing them from the editor. Blank fields in
a filled wave count as zero. Invalid or negative counts still report an error,
and at least one non-empty wave is required.

Each wave has its own catapult target. A second, optional target is enabled
from **20 catapults** and cleared if the count drops below 20.
Target names, groups, and values match the supplied Rally Point HTML:
for example, Random = 99, Cropland = 4, Residence = 25, Palace = 26,
Treasury = 27, and Command Center = 44 (for Hun targets when offered by the game).
Horse Drinking Trough = 41 is included under Military for Roman targets.
The former combined/automatic target is replaced by
explicit building choices. If a selected target is unavailable in the game,
the task selects Random (99) for that target and logs a warning. This applies
to either catapult target, including Command Center and Horse Drinking Trough. An empty second target
remains empty. If Random is also unavailable, the task stops before confirming.

**Calculate** previews the plan; **Action** queues a snapshot of non-draft waves.
Later edits to the wave inputs do not change an already queued task.
Conquest always selects **Attack: Normal**, never **Attack: Raid**. If the
normal attack option is missing or disabled, the task stops before submitting.
Additional waves use the Confirm page's **Add a wave**, then **Add as wave**.
Their hidden coordinates and normal-attack type are checked rather than edited.

## FAQs

User guides are available in [FAQs/README.md](FAQs/README.md).

- Getting started
- Add account / proxy
- Account settings
- Village build queue
- NPC and troop training
- Farming setup
