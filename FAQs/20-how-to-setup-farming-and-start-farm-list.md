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
For oasis targets, it reads the actual **Troops** table, not report history
or the farm row's configured raiding troops. A `none` row or all-zero counts
leaves the target unchanged. Any positive troop/animal count causes TBS to
open the existing **Edit farm list** link for that exact list and slot,
check **Deactivate target**, and save. TBS reloads the farm page and confirms
the same slot is disabled before logging success.

Already disabled targets stay disabled, even if empty. Non-oasis targets,
unknown/invalid troop counts, missing dialogs/edit links, and mismatched
list/coordinates are skipped with warnings; no new targets are added.
The English game UI is required. Check the account's **Debug** tab for
per-target results and the final summary. This button does not send raids
or toggle the local farm-list Active setting.

## clearing oasis with the hero

Select a farm list and configure **Hero clear oasis - selected farm list**.
These settings are independent of normal clear-oasis settings. Save them before
clicking **Hero clear oasis**.

- Set inclusive **Min animal** and **Max animal** limits and allowed animal types.
  Any positive count of a disallowed animal skips the target without disabling it.
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

TBS switches to the farm list's village and checks hero availability in Send
troops. The hero must be available there. Each eligible oasis is simulated as a
raid with the configured army. TBS checks real troop availability and requires the
confirmation army, village, coordinates and raid type to match the simulation
exactly. Missing or reduced troops are never silently accepted.

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
