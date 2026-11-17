# how to setup farming and start farm list

## step-by-step

1. Select an account.
2. Open **Farming** tab.
3. Click **Load farmlist**.
4. Click a farm list from the left list.
5. Click the middle **Active/Deactive** button to toggle it.
6. Set **Start farm delay** range.
7. Optional: enable **Use Start all button (ignore active settings)**.
8. Click **Save**.
9. Click **Start** to begin farming.
10. Click **Stop** when you want to stop.

## tips

- If Start does nothing, check if at least one list is active (when Start all option is off).
- Use safe delay ranges to reduce repeated fast actions.

When returning during Clear oasis or Hero clear oasis, TBS clicks the Farm List
tab if the current page is the Rally Point; otherwise it uses the Farm List
shortcut in the village sidebar. Check target troops and lost report processing
use the sidebar shortcut to return. If the tab is missing or ambiguous, TBS
tries the shortcut. If the selected control is missing, ambiguous, cannot be
clicked, or opens the wrong page or village, TBS logs a warning in the Debug log
and falls back to the Farm List URL. The target's village is checked before
scanning or sending.

Starting the regular farm list does not require the current village to own each
farm list. TBS first uses the Farm List shortcut in the current village. If that
shortcut is unavailable because the village has no Rally Point, TBS switches to
the first village in the village list and tries the shortcut again. Check oasis,
Clear oasis and Hero clear oasis resolve the selected list's owner village and
switch to it when necessary; they keep the current Farm List page when it
remains open for that owner, reopening it only if the page changed.

## checking oasis target troops

Select a farm list on the left, then click **Check target troops**.
TBS queues a one-time **Check target troops in farm list** task for that
account. It runs through the normal sequential task queue, not concurrently
with farming; paused/offline accounts must resume before it can execute.
The selected list is captured when clicked, so changing the UI selection
does not change the queued scan.

The task opens the farm page, resolves the selected list's owning village from
its village-group header, and switches to that village before checking targets.
The group name must match exactly one owned village; missing or duplicate names
stop the scan without changing targets. TBS confirms the active village and
keeps that village context when reloading the farm page.
It expands the selected list and checks its
enabled target rows by opening each target's map-details dialog.
Check oasis, Clear oasis and Hero clear oasis only expand the selected farm
list, identified by its list ID, including lists inside a `dropContainer`.
They leave village-group expansion controls and other farm lists unchanged.
They wait for the rendered target count to match the list's target counter
before scanning; a collapsed or partially loaded list is not treated as empty.
For Check oasis, TBS opens each target in a temporary browser tab and reads
the actual **Troops** table, not report history or the farm row's configured
raiding troops. It closes the check tab before continuing in the original farm
list tab. **Active oasis** checks disabled targets and activates empty oasis or
oasis containing only allowed animal types with a total count at or below
**Safe animal**. **Deactive oasis** checks active targets and deactivates unsafe
oasis. **Check oasis** combines both: it inspects all targets in the selected
list, activates safe or empty oasis and deactivates unsafe oasis. Targets already
in the correct state are not toggled. All three use the selected list's saved
Check oasis settings. Save changes before queuing a scan.
TBS applies changes through each target's context menu, rescans the farm-list
element and confirms the changed state without reloading the farm page.
Telegram `/check-oasis <account-id> <farm-list-id>` queues the same combined scan.

Clear oasis also opens each target in a temporary tab to inspect animals and
send the raid, then closes it before continuing on the farm list tab. Targets
that exceed the animal limit or contain disallowed animals are deactivated from
the main farm list after the temporary tab closes.
In **Raid troops (empty oasis)**, blank fields mean zero. If all three counts
are zero, empty oasis are skipped without sending or waiting for raid troops.
Animal-bearing targets still use the selected attack troop and safety factor.
Safety factor accepts decimals (for example `2.5`); required troops are rounded
up, so 3 animals at 2.5 require 8 troops. New Clear oasis settings default to 2.5
and allow Rat, Spider, Bat and Wild Boar. New Hero clear oasis settings allow the
same animals. Previously saved values, including legacy account settings, are
preserved.

Non-oasis targets,
unknown/invalid troop counts, missing dialogs/context menus, and mismatched
list/coordinates are skipped with warnings; no new targets are added.
The English game UI is required. Check the account's **Debug** tab for
per-target results and the final summary. This button does not send raids
or toggle the local farm-list Active setting.

Check oasis, Activate oasis, Deactive oasis, Clear oasis and Hero clear oasis
task names show the farm list name. Clear oasis and Hero clear oasis
per-target Debug messages identify oasis by map coordinates `(x|y)`, including
skipped targets and sent raids, instead of internal farm-list slot IDs.

## clearing oasis with the hero

Select a farm list and configure **Hero clear oasis - selected farm list**.
Hero combat settings are separate from normal clear-oasis settings. Save them before
clicking **Hero clear oasis**.

- Set **Min animal**, inclusive **Max animal**, and allowed animal types.
  Any positive count of a disallowed animal skips the target without disabling it.
- **Use troop to clear weak oasis** is off by default. When off, counts at or below Min animal are
  skipped; only counts above Min animal enter the hero path.
  When on, empty oasis and counts below Min animal use the same farm list's saved
  **Clear oasis** settings, without the hero or simulation. Positive animal
  counts use Attack troop and Safety factor (rounded up); empty oasis use Raid
  troops and are skipped if all raid counts are zero. Clear oasis Allowed animals
  and Limit animal apply to this troop-only path, not the hero animal selection.
  At exactly Min animal (> 0), the hero path applies when Use troop is on.
  There is no separate Hero raid troop composition or safety factor; save the
  shared Clear oasis settings before starting.
- Configure fixed Hun sub troop counts: Steppe Rider, Marksman and Marauder.
  Zero for all three sends only the hero. For other attacker troop inputs, only
  positive counts are cleared; zero and empty fields are left untouched.
  Troop upgrade levels and hero equipment are left unchanged.
  Unresearched/disabled sub troops do not prevent a hero-only raid when their
  configured counts are zero. Empty army fields are accepted as zero only for
  unselected troops; the hero must still be exactly one.
- **Min hero health allow** applies twice: current health below the minimum
  cancels this task; simulated remaining health below it skips only the target.
- **Max hero health lost** measures health percentage points, not a fraction of
  current health. A change from 90 to 61 loses 29 points.
- **Max sub troop lost** limits the total dead sub troops; zero permits no deaths.
  Equality with any threshold is allowed. A dead hero is never sent.

TBS checks the hero HUD and its village against the farm list owner, switching
villages if needed; it does not open Send troops just to check hero availability.
For each target, TBS opens the oasis in a temporary tab, checks its animals, and
simulates or prepares the raid in that tab. The main tab stays on the farm list.
Hero availability is checked again on the send form before a hero dispatch. After
sending, the temporary tab closes and the task waits until the hero returns and
the task delay buffer elapses. On resumption TBS reopens the Farm List to refresh
the hero state before continuing, even if the main tab was already on that page.
Each eligible hero raid is simulated with the configured army. TBS checks
real troop availability and requires the
confirmation army, village, coordinates and raid type to match the simulation
exactly. Missing or reduced troops are never silently accepted.
Troop-only raids also validate the exact army (including no hero), village,
coordinates and raid type at confirmation. They yield for the task delay buffer
and count toward phase limits, but do not wait for the hero's return.

Only one hero-clear task can be queued per account. Automatic adventures wait
while it is queued. After Confirm, success requires both the Overview URL
(`tt=1`) and active Overview tab. The task then yields to the normal queue until
the estimated return time (arrival plus outbound duration, with a five-second
buffer). Actual hero availability is checked again on resumption. Return travel
can differ if the army or speed bonuses change.

Disabled targets are ignored. Insufficient sub troops skip the target. Unknown
data stops sending and is logged. An uncertain final confirmation pauses the
account and blocks automatic re-sending: check the game and remove the task
before restarting it. **Remove hero clear oasis task** removes a waiting task,
not any raid already sent. Queue progress and the confirmation guard are held in
memory; they do not survive an application restart.
