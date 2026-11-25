# what do account settings mean

## activity settings

- **Work time**: how long the bot keeps working before resting.
- **Sleep time**: how long the bot rests before working again.

## delay settings

- **Click delay**: small wait between browser clicks.
- **Task delay**: small wait between bot tasks.
- **Upgrade task delay** is configured per village in **Villages → Settings → Upgrade settings**. It adds a random buffer (milliseconds) only when **Upgrade building** schedules its next run after waiting for construction or resources. It does not replace **Task delay** or delay the first run. Set both values to `0` to disable the buffer. Existing villages inherit their previous account-level range once if they have no village setting yet.

## feature settings

- **Enable auto load village's building**: auto refresh village building data.
- **Enable auto start adventure**: auto send hero to adventures.
- **Enable auto claim daily quest**: auto claim daily quest rewards.
- **Enable auto set hero point**: auto assign hero points.
- **Enable scheduled report and incoming scans**: add a recurring account task
  that reads the report badge and/or incoming troops, independently of farming
  and task-completion triggers. Set **Scheduled scan interval** in minutes
  (minimum and maximum from 1 to 1440); the next delay is chosen randomly
  within the inclusive range. It is off by default, with a 10-minute interval.
  Intervals saved in seconds by older versions are rounded up to whole minutes.
  Settings are saved per account. The first scan is scheduled after the interval
  when the account starts or the feature and at least one notification are enabled; an already overdue scan
  waits until the account is online. Changing the interval
  reschedules a waiting task; disabling removes it.
  Scans share the account queue and browser, so a running task, sleep, or pause
  can delay them. They do not run while the account is offline.
  Each scheduled scan runs only if its corresponding Telegram notification
  switch is enabled. If both switches are off, no scheduled task is queued.
  Before scanning, it clicks the resource overview if currently on dorf2;
  otherwise it clicks the village overview (dorf2), and waits for navigation.
  If navigation fails, it logs a warning and scans the current page instead;
  its data may be stale.
  The lost-report switch can still trigger processing during a scheduled report
  scan when report notifications are enabled. Its farming-triggered scans remain
  independent of the report notification switch. Existing farming and
  task-completion scans remain active and can cause additional checks.
- **Notify on report badge increase (Telegram)**: check the report badge immediately
  after opening the farm-list page but before sending farms in each **Start farm list** task, with either Start all or individual
  active lists. These checks use the existing random farming interval.
  No farming, no active lists (unless using Start all),
  or failure to open the farm page means no farming-triggered scan; the independent
  scheduled scan can still run when enabled. A later farm-page navigation or Start-button failure
  does not undo the scan. This only reads the current page; it
  does not refresh or open reports unless target deactivation below is enabled.
- **Deactivate farm targets from unread lost offensive reports**: a separate
  per-account switch, off by default, independent of Telegram credentials.
  Uses the same pre-send scan: open farm list, scan badge, deactivate targets,
  re-establish the farm page, then farm according to settings.
  If the farm-page navigation fails,
  no Start button is clicked. The first scan after
  start/restart/resume or changing either report switch establishes a baseline.
  Later badge changes (increases or decreases) open Reports, then Offensive,
  and process only unread `Lost as attacker` reports on the first page.
  Opening a report marks it read in the game, reducing the badge.
  TBS resolves the attacker's village by its exact, unique name in the owned
  village list, switches village, and reopens the report. Disabled/unavailable
  raid-list buttons, ambiguous villages, and dialogs with no selected farm list
  are skipped with warnings; TBS does not guess a list or add a target.
  If the **Deactivate target** checkbox is unchecked, TBS checks it and saves,
  then reopens the dialog to verify the selected list and checked state.
  Already checked targets are not saved again. TBS returns to the report,
  then restores the original village/page after processing the batch.
  Its own resulting badge decrease updates the baseline without triggering
  another batch or Telegram message. Failures are logged; subsequent scans
  only process reports still unread. This feature changes server-side farm
  targets, not the bot's local farm-list enable switch.

## Telegram notifications

**Notify when bot stops due to an error (Telegram)** is a per-account switch,
off by default. It queues an alert when a task failure or unexpected scheduler
error pauses the account. Manual pause, logout, and tasks canceled without
pausing the account do not send an alert. The message includes the account,
server host, task name, error category and detection time; consult the TBS logs
for details. A later error after resuming generates a new alert.

**Notify on incoming attack/raid (Telegram)** is a separate per-account switch.
It checks `.listEntry.village.attack` after a task succeeds when logged in,
with at least 60 seconds between scan attempts per account. Each scan schedules
a **Check incoming attack/raid** task for 10 minutes after that attempt.
Ordinary tasks can trigger a scan earlier and postpone the fallback. The fallback
runs even when there is no other due task, in the same account queue; there is
never more than one fallback task per account.
The independent scheduled scan, when enabled, also reads incoming troops at its
configured interval and is not subject to the 60-second task-triggered throttle.
It runs only while the incoming-notification switch is enabled.

**Notify on sleep and wake up (Telegram)** queues a message when the account
enters its scheduled sleep and after the browser has reopened. The sleep message
includes the Access username, sleep duration in minutes, and expected wake-up
time in UTC+07:00. The `Walkup` message includes the Access username and the
following work duration in minutes. This switch is off by default and is saved
per account.

The 10-minute interval is a target, not a hard deadline: a long task or sleep can
delay it. Paused/offline accounts do not execute the fallback. After resume, an
overdue fallback can run when the queue is free. Enabling the feature or starting
an account creates the fallback; disabling it removes the fallback. Scans are
not performed in parallel with another browser task.

The first valid scan alerts immediately for each village with incoming.
Later scans alert when either the attack count or raid count increases, even if
their combined total decreases or stays unchanged. Decreases and unchanged
counts update the stored counts without sending. Start/restart/resume clears remembered village
states, but does not bypass the one-minute scan interval. Failed scans preserve
state. Missing village rows (such as collapsed groups) do not clear remembered
incoming; expand all groups to maximize coverage.

The SVG icon alone is not a signal: it also exists in rows without incoming.
Attack and raid both mark the village row `attack`. TBS hovers each marked
village's badge and reads only the visible tooltip linked by `aria-describedby`.
Tooltip IDs are dynamic. English labels `Incoming attacks: N` and
`Incoming raids: N` provide separate counts of incoming movements (not soldiers).
Use the English game UI; unknown, malformed, unavailable, or hidden tooltips
preserve that village's counts and are logged, while other villages continue.
`Loading...` is not read as a count: TBS waits up to three seconds for an
attack/raid label in the linked visible tooltip. A loading timeout preserves
counts and does not pause the account.
Each successfully read village is logged in the account's Debug tab as
`Incoming troops scanned: <village name>, <count> attacks, <count> raids`.
Failed reads produce warnings instead of a zero-count success log.
Hovering can load tooltip data and adds bounded waiting time after task execution,
before the account queue can start another task.
Unmarked village rows reset both counts to zero without loading a tooltip.
The alert contains account, village name/ID, coordinates, both count changes,
and detection time, not soldier counts or arrival time. Incoming movements
replaced between scans with unchanged counts, or an entire incoming episode
between scans, cannot be detected by this method.
Incoming alerts use the same Telegram settings and persistent delivery queue
as report alerts.

1. Create a Telegram bot using BotFather, obtain its token, and send `/start` to
   the bot from the receiving chat.
2. Obtain the numeric chat ID (for example, from Telegram's `getUpdates` API
   after messaging the bot). Treat the bot token as a password.
3. In account settings, enter the token and chat ID in **Telegram notifications**.
   Use **Send test message**, then **Save Telegram settings**.
4. Enable the desired Telegram notification switches and save the account
   settings. Report notifications also require configuring and starting farming.
   Telegram credentials are shared by all accounts; each enable switch is per
   account. Leave the token blank when keeping the saved token.

Each start, restart, or pause/resume begins a new monitoring session. The first
valid scan establishes a baseline without sending anything. Every subsequent
valid scan updates that baseline, including decreases to zero. A message is
queued only when the count increases. Missing report links, malformed badges,
and failed scans keep the previous baseline.

This tracks badge counts, not individual reports. For example, `3 -> 0 -> 1`
sends a notification on the last scan, but `3 -> 1` does not. Reading reports
between scans, unchanged counts, or capped badges can hide new reports.
Checks stop while paused, logged out, or sleeping. Already queued notifications
continue to be delivered independently of the browser.

Credentials are protected with Windows DPAPI for the current Windows user in
`%LOCALAPPDATA%\TravianBotSharp\telegram-settings.bin`. They are not included in
account-settings exports. Copying that file to another Windows user or machine
does not transfer usable credentials.

Pending messages are stored separately in
`%LOCALAPPDATA%\TravianBotSharp\report-notifications.db`; badge baselines are
only held in memory. Delivery uses the currently saved Telegram credentials.
Messages are retried for transient failures, up to three failed attempts, and
respect Telegram's retry delay. Permanent or exhausted failures remain in that
database and are logged; they are not retried automatically. Successful messages
are removed. A timeout after Telegram accepts a message can cause a duplicate.
Messages contain the account label, counts, and detection time, not report
contents. TBS and the machine must remain running for scans and delivery.

## Telegram remote commands

Remote control is **off by default**. In Settings, save the Telegram bot token
and your **positive private chat ID**, enable **Enable Telegram commands (private
chat only)**, then click **Save Telegram settings**. This global switch permits
control of all accounts in this TBS instance; it is separate from the per-account
notification switches. Notification-only use still supports group chat IDs.
Only messages from that private chat's owner are accepted. Groups, channels,
bots, forwarded messages, edited messages and messages relayed via another bot
cannot control TBS.

TBS uses long polling: no public server, domain or open inbound port is needed.
TBS and the machine must be running and connected to the Internet. Use only one
receiver for each bot token and disable any webhook configured for that bot
before enabling this feature. Polling conflicts and configuration errors appear
in the TBS logs; TBS does not automatically delete an existing webhook.
After enabling or restarting, allow a few seconds for the receiver to become
ready, then send `/tbs_command` as plain text to see the available commands.

| Command | Behavior |
|---|---|
| `/tbs_command` | Show this command list and argument syntax. |
| `/accounts` | List saved account IDs, names and statuses. |
| `/status <account-id>` | Show account status, queue size and up to 20 queued tasks. |
| `/farm_lists <account-id>` | List saved farm list IDs, names and Active flags. Load lists in TBS first. |
| `/active <account-id> <farm-list-id>` | Set the local farm list Active; does not start farming. |
| `/deactive <account-id> <farm-list-id>` | Set the local farm list Inactive. |
| `/active_oasis <account-id> <farm-list-id>` | Queue Check oasis to activate safe disabled oasis targets using saved Check oasis settings. |
| `/deactive_oasis <account-id> <farm-list-id>` | Queue Check oasis to deactivate unsafe active oasis targets using saved Check oasis settings. |
| `/check_oasis <account-id> <farm-list-id>` | Check all targets; activate safe or empty oasis and deactivate unsafe oasis using saved Check oasis settings. |
| `/hero_farm <account-id> <farm-list-id>` | Queue Hero clear oasis with validated, saved settings. Only one hero-clear task per account is allowed. |
| `/pause <account-id>` | Pause an online account, requesting cancellation of its current task. |
| `/resume <account-id>` | Resume a paused account. This does not open or log in an offline account. |
| `/start_farm <account-id>` | Queue Start farm list. Requires an active list or Start all. Existing farm schedules are not changed. |
| `/stop_farm <account-id>` | Remove Start farm list. A send in progress may finish; raids already sent continue in game. |

Example: send `/accounts`, then `/farm_lists 1`, then `/hero_farm 1 10`.
The Active/Deactive and Check oasis commands require both account ID and farm list ID;
TBS rejects lists not owned by that account. `/active` and `/deactive` set a fixed state rather
than blindly toggling it and refresh the Farming list in the UI. They do not
change targets in the game; Start all still ignores the local Active flags.
The oasis commands use the same task as Farming -> Check oasis setting ->
Active oasis / Deactive oasis, including the saved allowed-animal and Safe animal
rules. They do not blindly toggle every target or change the list's local Active
flag. Repeating the same oasis command while its task is queued/running does
not add another task or change its schedule.
`/check_oasis` combines both oasis actions in one scan, regardless of each target's
initial state. Empty oasis are safe and can be activated by both `/active_oasis`
and `/check_oasis`. Non-empty oasis must contain only allowed animal types and
have a total animal count at or below Safe animal. Unknown or invalid data
leaves the target unchanged and is logged.
Task commands use the normal sequential account queue, not a second browser
controller. Paused/offline accounts retain newly queued tasks until online.
Telegram replies distinguish queue acceptance from actual execution; they do not
announce task completion. Account transition states reject control commands.
The existing removal of Start farm list on sleep, logout and Restart is unchanged.

Use underscores in command names, as shown above. The previous hyphenated
forms are still accepted as plain-text shortcuts for compatibility.

Commands pending at startup, re-enabling or a bot/chat change are discarded.
Commands older than two minutes are rejected; send a fresh command after the
receiver is ready. TBS saves each received update ID before executing it in
`%LOCALAPPDATA%\TravianBotSharp\telegram-command-cursor.json`. If saving fails,
no command is executed. This prevents replay after restart, but a crash after
claiming a command can lose that command or its reply: check `/status` before
retrying. Reply delivery failures are logged and do not re-execute commands.
Long responses are split into multiple plain-text messages.

## chrome settings

- **Use headless chrome**: run Chrome in background without visible browser window. (this will break watching ads and some other interactive features)

## farming-related account setting

- **Start farm delay**: random delay range before farm list starts.
- **Use Start all button**: use the in-game Start all behavior instead of only active lists.
