# Workflow and state contracts

## Inventory and editor lifecycle

1. On main-window Shown, set an operation guard, disable filter/Refresh, read inventory on a worker, then reconcile rows on the UI thread.
2. Leave the grid visually enabled; all action handlers check the guard. Selection/scrolling remain available.
3. A filter change uses the cached snapshot, not another network read.
4. Edit free/known mapped entries only; show MappingDialog owned by MainForm.
5. After any editor dismissal, reread inventory. Do not clear/recreate all rows.
6. Insert only missing filtered letters; remove only excluded letters; compare immutable snapshots and invalidate changed rows only. Restore selected letter/action column and first displayed row index where possible.

## Capture of mapping inputs

Capture validated options and the Explorer checkbox when OK/Test starts, not after a worker finishes.
Normalize the UNC for mapping/testing only; preserve password whitespace and trim username. Anonymous
produces empty strings. Free-letter account prefill is ordinary named authentication with a blank
password until edited, not an authorization to recover passwords or use an existing session automatically.

## Ordinary Test

Idle -> read/validate options -> busy -> isolated logon/share connection -> verify directory/listing ->
release enumerator -> disconnect temporary connection without force -> dispose token -> success feedback -> idle.

A failure shows complete error feedback and an ordinary message box, except 1219 enters its specialized
flow. Error 5 also enables contextual help. Testing must not disturb normal desktop mappings or sessions.
A cleanup error is still reported; do not claim success while suppressing it.

## Dialog Explorer

Idle -> capture the exact target textbox -> busy -> start explorer.exe with a single ArgumentList
entry -> status that the location was sent -> idle. Do not call ReadOptions, NormalizePath, TestConnection,
or map/unmap. Passwords never enter the command line. An empty/invalid target is left for Explorer to
interpret; the app can still report process-launch failure. This operation does not prove access succeeded.

## OK, replacement, rollback

1. Ensure original snapshot is eligible and unchanged.
2. Test target/access without changing desktop mappings.
3. Ensure snapshot is unchanged again after the potentially slow test.
4. If network-mapped, unmap that letter with profile update and force=false.
5. Map the requested UNC with supplied authentication and persistent profile.
6. On success, optionally open that letter. An Explorer-launch failure is a warning about browsing,
   not a mapping failure; do not remap or roll back a successful mapping.
7. On failure after old unmap, restore old target and old persistence with default/existing Windows
   credentials. Never use the NEW target's credentials to restore an unrelated OLD target.
8. Report both mapping and rollback outcomes. Keep the editor open after ordinary failure.
9. On successful mapping, close with OK; on conflict cancellation, close with Cancel; otherwise stay editable.

Even identical old/new paths must perform credential-changing replacement. A free-letter map failure
has no old mapping to restore. Failed non-forced unmap must never proceed to Map.

## 1219 state transitions

| Review choice | Immediate behavior | After review closes |
| --- | --- | --- |
| Use existing login | Available only with one distinct known account among Connected entries. | Rebase selected letter safely; create existing-session options; reflect account and empty password; retry mapping. |
| Drop connections | Ask Yes/No; No leaves this SAME review modal open. | Only after Yes: rebase letter, validate/reload reviewed server list, force-disconnect scoped resources, rebase letter again, restore originally provided options, retry mapping. |
| Cancel/close/Escape | Resolution defaults to Cancel. | Close mapping editor back to main inventory; no new disconnect. |

Connection discovery itself reads metadata only. The review never performs network mutations.
The warning describes that other connections cannot be restored automatically without their passwords.
UseExistingLogin means: Credentials selected, reported username, password cleared and disabled; native
mapping receives a null password. Directory verification happens in the desktop session after checking
the reported login still matches. Manual username/path/login-mode edits leave reuse mode and restore
ordinary credential entry; password is not magically recovered.

When an original failure is wrapped by rollback messaging, detect 1219 in inner exceptions too.
A failed replacement may have left the letter free; GetRetryDrive permits the original mapping or a
free letter, but never an unrelated new owner. Repeated 1219 reopens review without automatically dropping
anything. A retry selected after TEST enters this mapping flow and can assign a letter.

## Scoped bulk disconnect

- Require nonempty Connections, zero discovery warnings, and all resources on the target named server.
- Reread; compare sorted case-insensitive identity strings: local device | remote path | reported login.
- Disconnect entries with device names before deviceless resources. Recheck each device target against
  the reviewed UNC immediately before cancellation; fallback to remembered registry target if necessary.
- Profile update applies to device mappings. Deviceless disconnect uses the UNC and flags=0.
- force=true is allowed only after the specific server-disconnect warning received Yes.
- Already-disconnected resources can be benign. Other native failures stop the batch and report the
  number already disconnected; do not retry mapping after a partial failure.
- Reread afterward. Warnings or any remaining/reopened connections stop mapping retry.
- Do not match server prefixes or resolve server aliases to expand the scope.

## REMOVE and 2401

Idle -> busy -> validate snapshot -> non-forced Unmap.

- Success: refresh in place; row is free and offers EDIT only.
- Other failure: ordinary owned removal error -> refresh -> usable main window.
- Native/wrapped 2401: show original error plus scoped Yes/No warning.
  - No: no second Unmap -> refresh -> usable main window.
  - Yes: reread and compare original snapshot -> one forced Unmap for the same letter.
  - Changed owner: reject before forced call -> error -> refresh.
  - Retry failure (even 2401 again): ordinary error once -> refresh; do not offer another force prompt.

Do not reuse the server-wide 1219 disconnect routine for single-letter removal. Removal never runs
preflight, remaps automatically, or deletes remote content.

## Contextual help and input state

New actions and manual connection-input edits clear stale status/help. A detected access denial sets
red text and reveals Read more.... Its owned modal preserves all editor inputs. Help links launch the
external default browser and set visited state; browser-launch failures get their own error. Closing
help only closes help. The historically requested final follow-up paragraph is intentionally preserved.

While mapping/testing native work is pending, block cancellation and window closure, disable editing,
and show progress. Windows network calls can be slow and are not safely cancellable mid-replacement.
