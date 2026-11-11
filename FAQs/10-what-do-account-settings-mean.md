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
- **Notify on report badge increase (Telegram)**: check the report badge about every
  60 seconds while the account is running. Other tasks can delay a check. This
  only reads the current page; it does not refresh or open reports.

## Telegram notifications

1. Create a Telegram bot using BotFather, obtain its token, and send `/start` to
   the bot from the receiving chat.
2. Obtain the numeric chat ID (for example, from Telegram's `getUpdates` API
   after messaging the bot). Treat the bot token as a password.
3. In account settings, enter the token and chat ID in **Telegram notifications**.
   Use **Send test message**, then **Save Telegram settings**.
4. Enable **Notify on report badge increase (Telegram)** and save the account
   settings. Telegram credentials are shared by all accounts; the enable switch
   is per account. Leave the token blank when keeping the saved token.

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
