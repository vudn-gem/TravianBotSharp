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
- Telegram alerts when an account pauses automatically due to an error
- Per-village incoming evasion with maximum troop training and whole-army raids

## Tech specification

Currently, TBS only runs on Windows.

## Evasion preparation

Configure **Villages / Settings / Evasion Settings** per village. Defaults are
disabled, with readiness scheduled **120 seconds before arrival**. Select one
trainable troop type (or None), another account village for resources (or None),
and optionally troop evasion with X/Y coordinates. Resource destination
coordinates are resolved from the saved village list; another account's village
and the origin village cannot be selected. Settings are saved/exported/imported
with the existing village settings.

At the scheduled preparation time, TBS opens Barracks, Stable, or Workshop
according to the selected troop and trains the **maximum currently affordable
amount**. Only that type is trained; nonzero fields for other troop types are
cleared. None skips training. This is independent of normal Troop training
settings, which keep their existing behavior. Missing buildings, locked troops,
zero resources, invalid maximum amounts, and navigation/input/submit failures
are reported explicitly. TBS checks settings after navigation and skips training
if they changed or arrival has passed. It also reopens the incoming overview
and confirms the scheduled movement still exists before opening the training
building. It checks settings and the deadline again before
submit. A training attempt is not automatically repeated, including uncertain
submit outcomes; check the game before manually retrying.

With **Evade troops (Attack: Raid)** enabled, preparation continues after the
training phase even if training fails (for example, insufficient resources or
a missing building). TBS rechecks that the incoming movement still exists,
opens **RallyPointSendTroops** from the quick-link slot with a middle click in a
separate tab, and falls back to the account server's
`/building?id=16&tt=2` in that tab if necessary. The active village is
verified before proceeding. It scans
**all troops currently available in the origin village, including
Hero if available**, using **Attack: Raid / Send / Confirm**. Missing Hero is
allowed; Hero redeployment is disabled. Blank/zero unused fields are skipped,
and nonzero prefilled counts for unavailable types are cleared.

Evasion and Conquest share this tab navigation and the first-wave Send step:
open Send troops directly, enter X/Y before troop counts, then click the unique
`button[type='submit']` and wait for Confirm. Both close the dedicated tab on
completion, failure or cancellation and restore the original tab. Conquest
retains Normal attacks, planned waves/catapult targets and timed confirmation;
Evasion retains a single whole-army Raid before the incoming deadline.

Choose a destination that is safe to raid: this is an actual raid, not a
reinforcement or automatic return. TBS does not check target safety, cancel the
raid, or recall the army after the incoming attack. Troops not currently home
cannot be evaded. Training and sending use the account's normal input/click
delays, so allow enough preparation time.

Before Send and Confirm, TBS validates the village, X/Y, Raid type, exact army,
settings and incoming deadline. It refuses empty armies, ambiguous/malformed
availability, changed confirmation data, additional waves, and late sends.
Before Confirm it saves a durable record keyed by account/village/incoming
movement in the existing SQLite database. A failed save prevents the click.
Any recorded attempt, including an uncertain outcome, is never automatically
sent again for that movement, even after restart or a settings change. An
uncertain outcome is logged as requiring an in-game check; keep the database.
An interrupted preparation is not automatically retried in the current session.

**Resource transfer remains unavailable** and is logged when a destination is
selected; it awaits the supplied flow/HTML and implementation.

**Test Evasion (LIVE)** in Evasion Settings queues a manual test using saved
settings after an explicit confirmation. Save settings with evasion enabled
first, and keep the account running. No incoming attack is required: the test
trains the selected troop at maximum and/or sends all available troops and Hero
by a real Raid according to the saved switches. Resource transfer remains
unavailable. Tests run through the normal village-switching scheduler and
dedicated Send troops tab; results appear in the task list/logs. Each confirmed
test has its own durable no-resend record, separate from incoming movements.
Pressing the button again after a completed test creates a new live attempt.

Enabling any village's evasion also enables incoming scans independently of
Telegram alerts, using the same post-task scans and 10-minute fallback as
incoming Telegram alerts while the account is running. When both are enabled,
each scan can both notify and schedule evasion.
An increase in either attack or raid count queues inspection of the affected
village; unchanged or decreasing counts do not reopen the overview. Inspection
switches village through the existing task pipeline,
tries **Incoming Troops** in Link List, then falls back to the current account
server's `/building?id=16&tt=1&filter=1&subfilters=1&newdid=...`.
It schedules deduplicated readiness tasks by movement ID using the live
attack/raid countdown, including arrivals across midnight. The displayed player
arrival time is checked against the TBS system clock and countdown modulo one day;
a mismatch over five seconds or missing clock data is logged without changing
the countdown-based schedule. The first incoming starts a group; an incoming
more than the configured lead time after that group's planned arrival starts
another group. If the first movement is canceled, readiness checks the group
and uses its earliest remaining movement. A failed inspection
retries after one minute. Plans are removed when all incoming clear or when a
later inspection finds that a movement has disappeared; readiness also verifies
the movement before training or sending.
If already within the lead window, readiness is queued immediately.
Disabling evasion or changing its saved settings clears pending plans.

Planning currently requires a single-page incoming overview; additional pages,
malformed arrival data, or an empty overview after incoming was detected produce
a warning and retry, never a guessed arrival.
Plans and training-attempt guards are session-local and rebuilt from incoming
scans after restart; an incoming still present can train again after restart,
but a recorded troop evasion is not resent.
They do not interrupt a running task and cannot guarantee readiness before arrival
when the account is paused, sleeping, or occupied by a long-running task.

## Troop input timing

Clear oasis, Hero clear oasis (including the simulator), Evasion, and Conquest enter
troop counts sequentially through the standard browser input helper. Typing
uses a 45-120 ms delay per character, followed by the account's configured
click-delay range before moving to the next field. Conquest's editable
coordinates use the same timing; hidden wave coordinates are only verified.
Both oasis flows on the Send troops tab and Conquest on every wave skip
unused troop fields that are blank or already zero, and clear any nonzero
prefilled count for an unused troop type.
Higher click delays increase preparation time, so allow enough time before
a scheduled Conquest send.

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
The browser flow is: switch village, open Rally Point, click **Send troops**,
fill troops and coordinates, select **Attack: Normal**, submit to Confirm,
then add any additional waves before the final timed confirmation.
Preparation must finish before **send time minus 10 seconds**, when TBS performs
the final wave, target, arrival-time, and Confirm-button checks. At **send time
minus 5 seconds**, TBS locks cancellation and durably records the confirmation
attempt, displaying **Send locked - waiting to send**. If preparation, final
checks, or saving this record misses its deadline, TBS skips the attack rather
than sending it late. A failed save prevents the click.
The final wait uses a monotonic clock and accounts for time spent saving.
At send time, TBS calls Confirm directly without the account click delay or
additional DOM queries/database writes. Browser, OS, and network latency still
prevent a guarantee of millisecond-accurate server receipt. The configured
network delay is already subtracted when calculating send time.
If Conquest preparation fails, the attempt's tab is closed and the task is
retried once using the existing retry delay. A second failure removes the task
and lets the account continue with its queue instead of pausing it. Late-arrival
skips are not retried. Once final confirmation has been attempted, failures
are not retried to avoid sending duplicate attacks; check the game for the
outcome. Manual pause/cancellation retains its existing behavior.

The **Saved Conquest tasks** table shows origin, target, waves,
preparation/send/arrival times, status, attempt count, and the last error.
Select a row and use **Cancel selected** to remove a scheduled task or cancel
preparation, the send-time wait, or the retry delay. Its dedicated tab is closed
before execution finishes, and the account continues with its next task without
being paused. Cancel is separate from the account's Pause button.
Tasks locked for confirmation (five seconds before send) cannot be canceled;
an uncertain outcome is shown as **Check in game** and must not be resent.
Plans, wave counts/targets, attempt counts, cancellation, and history are saved
in the existing SQLite database. On startup, TBS restores unfinished plans
whose send time is still in the future and whose origin still belongs to the
account. They run only when the account is online; opening TBS does not log in
or start the account automatically. Past send times become **Expired**.
The two-attempt limit is retained across restarts. Completed, canceled, expired,
and failed plans remain as history and are not queued again.
Five seconds before final Confirm, TBS durably records that confirmation may
be attempted. If TBS stops at that point, even before the click, the plan becomes
**Check in game** on restart and is never automatically resent. Keep the
database when moving TBS.

## FAQs

User guides are available in [FAQs/README.md](FAQs/README.md).

- Getting started
- Add account / proxy
- Account settings
- Village build queue
- NPC and troop training
- Farming setup
