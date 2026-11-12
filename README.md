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
**Add** creates an empty wave; **Copy last wave** duplicates the last wave,
including both catapult targets. Up to eight waves can be planned.

Each wave has its own catapult target. A second, optional target is enabled
from **20 catapults** and cleared if the count drops below 20.
Target names, groups, and values match the supplied Rally Point HTML:
for example, Random = 99, Cropland = 4, Residence = 25, Palace = 26,
and Treasury = 27. The former combined/automatic target is replaced by
explicit building choices. If a selected target is unavailable in the game,
the task stops without confirming the attack.

**Calculate** previews the plan; **Action** queues a snapshot of all waves.
Later edits to the wave inputs do not change an already queued task.
Conquest always selects **Attack: Normal**, never **Attack: Raid**. If the
normal attack option is missing or disabled, the task stops before submitting.

## FAQs

User guides are available in [FAQs/README.md](FAQs/README.md).

- Getting started
- Add account / proxy
- Account settings
- Village build queue
- NPC and troop training
- Farming setup
