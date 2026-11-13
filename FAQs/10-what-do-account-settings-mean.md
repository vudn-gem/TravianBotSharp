# what do account settings mean

## activity settings

- **Work time**: how long the bot keeps working before resting.
- **Sleep time**: how long the bot rests before working again.

## delay settings

- **Click delay**: small wait between browser clicks.
- **Task delay**: small wait between bot tasks.

## feature settings

- **Enable auto load village's building**: auto refresh village building data.
- **Enable auto start adventure**: auto send hero to adventures.
- **Enable auto claim daily quest**: auto claim daily quest rewards.
- **Enable auto set hero point**: auto assign hero points.
- **Enable scheduled report and incoming scans**: add a recurring account task
  that reads the report badge and/or incoming troops, independently of farming
  and task-completion triggers. Set **Scheduled scan interval** in seconds
  (minimum and maximum from 1 to 86400); the next delay is chosen randomly
  within the inclusive range. It is off by default, with a 600-second interval.
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

## chrome settings

- **Use headless chrome**: run Chrome in background without visible browser window. (this will break watching ads and some other interactive features)

## farming-related account setting

- **Start farm delay**: random delay range before farm list starts.
- **Use Start all button**: use the in-game Start all behavior instead of only active lists.
