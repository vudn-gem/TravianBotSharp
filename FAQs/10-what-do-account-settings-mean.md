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
- **Notify on report badge increase (Telegram)**: check the report badge immediately
  after each successful **Start farm list**, with either Start all or individual
  active lists. Checks use the existing random farming interval, without a
  separate report timer. No farming, no active lists (unless using Start all),
  or a failed farm start means no scan. This only reads the current page; it
  does not refresh or open reports.

## Telegram notifications

**Notify on incoming attack/raid (Telegram)** is a separate per-account switch.
It checks `.listEntry.village.attack` after a task succeeds when logged in,
with at least 60 seconds between scan attempts per account. Each scan schedules
a **Check incoming attack/raid** task for 10 minutes after that attempt.
Ordinary tasks can trigger a scan earlier and postpone the fallback. The fallback
runs even when there is no other due task, in the same account queue; there is
never more than one fallback task per account.

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
4. Enable **Notify on report badge increase (Telegram)** and save the account
   settings, then configure and start farming. Telegram credentials are shared
   by all accounts; the enable switch is per account. Leave the token blank
   when keeping the saved token.

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
