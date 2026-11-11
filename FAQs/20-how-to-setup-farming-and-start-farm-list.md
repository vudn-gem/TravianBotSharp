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

## checking oasis target troops

Select a farm list on the left, then click **Check target troops**.
TBS queues a one-time **Check target troops in farm list** task for that
account. It runs through the normal sequential task queue, not concurrently
with farming; paused/offline accounts must resume before it can execute.
The selected list is captured when clicked, so changing the UI selection
does not change the queued scan.

The task opens the farm page, expands the selected list, and checks its
enabled target rows by opening each target's map-details dialog.
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
