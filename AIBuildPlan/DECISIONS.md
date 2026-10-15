# Development decisions and evolution

This is the consolidated process record. The final specification and baseline source are authoritative.
Superseded behavior below is historical context only. Commit subjects/hashes describe local Git history;
the bundled source ZIP does not include that history.

| Baseline commit | Change / reason |
| --- | --- |
| bb650e8 | Initialized local Git before application source, as requested. |
| 276d6ce | Scaffolded documented WinForms/core solution and build setup. |
| 4d16a49 | Implemented drive discovery and safe replacement policy with a testable Windows boundary. |
| 9faca0e | Added A-Z grid and asynchronous mapping dialog. Original Anonymous-first/default behavior was later changed for free letters. |
| b56d231 | Added the initial 19 regression checks, native read-only smoke test and UI renders. The current baseline has 33. |
| b6c050e | Added detailed README/function documentation and verified packaging script. |
| f56b719 | Changed dialog Explorer opening to pass the entered path directly. Connection testing/credential validation had blocked the requested action. |
| 68338b2 | Added access-denied contextual help with exactly preserved explanation and external links. |
| 9176615 | Added native current-user 1219 connection/account discovery and explicit recovery choices. Avoided administrative SMB PowerShell dependency. |
| 1027a1e | Added OPEN/EDIT action icons and icon-only Refresh with dynamic sizing. |
| d10820c | Removed grid borders, added fixed 2px margins, removed icon frames, buffered drawing, preserved rows and stopped disabling/recoloring the whole grid. |
| 49b67c5 | Replaced icon_edit.png with icon_edit2.png and added REMOVE between OPEN and EDIT. |
| 9282caa | Added a Yes/No force retry specifically after native error 2401; initial removal and normal replacement stay non-forced. |
| dbddc26 | Corrected the old capitalization throughout the application to NetworkDriveRemapper, including projects/executable/resources. |
| f51464e | Generated multi-size ICO and source PNG as standalone assets, preserving provenance. |
| a6df1ef | Integrated ICO in executable and main window and included assets in Visual Studio solution. |
| 4cad088 | Final baseline: free letters default to Credentials/current qualified Windows identity; Credentials moved before Anonymous. Existing mappings retain their original Anonymous default. |

## Why these mechanics matter

- **Run as the desktop user:** elevation can place mappings in a different logon namespace from Explorer.
  The design uses ordinary current-user C# APIs rather than requiring elevated SMB management commands.
- **Isolate ordinary tests:** authentication tests should not disturb existing desktop connections.
  Explicit existing-login recovery deliberately uses the established desktop session instead.
- **Do not equate Anonymous with current account:** empty username/password, NULL credentials and
  server-mapped Guest identity have different semantics. Preserve the help explanation.
- **No implicit password access:** prefill only the qualified identity; old/existing account passwords
  are unavailable. Rollback can therefore fail and must be described as best-effort.
- **Recheck before mutation:** a slow preflight or user confirmation permits external ownership changes.
  Checks reduce risk without pretending Windows APIs provide atomic compare-and-swap.
- **Explicit destructive scope:** single-letter force after 2401 and server-wide force after 1219 are
  distinct actions. They have different prompts, scopes and repeat behavior.
- **No repeated single-letter force loop:** after Yes, one retry either succeeds or reports its error.
  In contrast, recurring 1219 returns to the specialized review.
- **Preserve exact wording:** the credentials-help text came from an earlier explanation and was
  requested verbatim, including its final follow-up sentence. Do not editorialize it during rebuild.
- **Small icons need the real artwork:** source images, fixed physical margins and executable ICO
  frames are supplied. Do not substitute Unicode symbols or a newly generated approximation.
- **No blink from short actions:** busy guards do not need whole-grid disabling. Reconciliation and
  double buffering preserve state and avoid replacing all rows after a modal closes.

## Explicitly superseded requirements

Do not recreate the old application capitalization, old icon_edit.png artwork, Anonymous-first
radio order, Anonymous default on free letters, framed Refresh button, visible grid borders,
connection-tested Explorer button, or an unconditional refusal to offer force after 2401.

The latest rule is 2px around each icon; it supersedes the earlier request to fill the entire button
with no inset. The exact error-2401 offer does not broaden to error 2404.
