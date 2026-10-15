# Product specification

## Platform and identity

| ID | Required behavior |
| --- | --- |
| P01 | C# Windows Forms desktop application named NetworkDriveRemapper in title, heading, assembly/product identity, executable, project and solution. |
| P02 | Compatible with Windows 10/11; target net8.0-windows, PerMonitorV2 DPI, STA UI, asInvoker manifest; normal user execution in the desktop logon context. |
| P03 | Use the provided application ICO for executable and main window. Embed it and the four action PNGs; runtime must not depend on working-directory asset files. |
| P04 | Keep passwords transient; never log, serialize, put in command lines, or recover old passwords. No third-party package dependencies in the baseline. |

## Main window and inventory

| ID | Required behavior |
| --- | --- |
| G01 | Show exactly A: through Z: in ascending order when unfiltered. One drive-letter column, a path/status column, and rightmost action cells. |
| G02 | Free letters display Available. Network letters display the mapped UNC target, with (Disconnected) for remembered/unavailable mappings. |
| G03 | Local or reserved letters have logical status labels and no row actions. Local disks take precedence over stale remembered mappings. Uncertain discovery must not classify a letter as free. |
| G04 | Default filter is unchecked. Show only available means free letters only: hide existing network mappings as well as local/reserved entries. |
| G05 | Free row: EDIT only, at far right. Known mapped row: OPEN, REMOVE, EDIT from left to right. Occupied row: no clickable actions. |
| G06 | EDIT uses icon_edit2.png; OPEN uses icon_open.png; REMOVE uses icon_remove.png; Refresh uses icon_refresh.png. Do not use the superseded icon_edit.png. |
| G07 | Single-click an icon activates that row's action independently of previous selection. OPEN launches that letter's root in Explorer. EDIT opens its owned modal editor. REMOVE follows R01-R05. |
| G08 | Double-click drive/status text or press Enter there to edit an eligible row. Enter on an action cell activates that action; native Space activation is retained. Headers and absent actions do nothing. |
| G09 | Refresh and F5 reread inventory. The grid has no outer/cell/header borders; icons have no painted button frames. Refresh has a tooltip/accessibility name. |
| G10 | Icons resize up or down to fill current cell/control image bounds after a fixed 2 physical pixels on all sides. No source-size cap, fixed 16/24px rendering, or extra inherited cell padding. |
| G11 | Use double buffering and in-place inventory reconciliation. OPEN must not refresh the inventory. Actions must not disable/re-enable the whole grid and recolor every row. Keep unchanged row objects/heights, selected cell and scroll index through refresh. |

## Mapping dialog

| ID | Required behavior |
| --- | --- |
| D01 | Owned modal Map network drive editor identifies the letter; free path starts empty, mapped path is prefilled. |
| D02 | Credentials appears first; Anonymous second, including keyboard order. Exactly one mode is selected. |
| D03 | Free letters default to Credentials with WindowsIdentity.GetCurrent().Name as username and blank password. Domain accounts use DOMAIN\user; local accounts use COMPUTER\user. A workgroup name is not substituted for the account authority. |
| D04 | Existing network mappings retain Anonymous as initial mode and blank credential fields. The new free-letter default must not silently change this behavior. |
| D05 | Credentials enables username and masked password. Anonymous disables both and passes empty username/password regardless of retained textbox text. Empty named-account passwords are allowed; missing named username is rejected. |
| D06 | Open directory after mapping is checked by default. |
| D07 | Button order: Test connection, Open in Explorer, Cancel, OK. Enter invokes OK; Escape invokes Cancel when idle; initial focus is target path. |
| D08 | Test connection ordinarily checks login, directory existence/type, and ability to start listing entries in an isolated outbound logon. It does not assign a drive letter. Empty directories can succeed. Error 1219 recovery is the explicit exception described below. |
| D09 | Open in Explorer passes exact textbox text directly to Explorer. No UNC/login validation, connection test, or app authentication. Local paths and unreachable network paths are passed on too. Explorer owns authentication and access feedback. |
| D10 | OK validates, checks ownership, tests, checks ownership again, and maps persistently. Existing known mapping is unmounted without force before replacement, even for the same path with changed credentials. |
| D11 | Failed preflight or failed unmap preserves the old mapping. Failed mapping after unmap attempts best-effort restoration of old path/persistence using default/existing Windows credentials and reports restoration success/failure. |
| D12 | Cancel abandons the proposed mapping and closes to the drive list. No mapping or disconnect is initiated just by ordinary Cancel. Work already explicitly confirmed in a recovery flow is not automatically undone. |
| D13 | Native work runs off the UI thread. Disable editable mapping controls/buttons and closure during native work; show marquee/progress. Clear password on close and stale feedback when connection input changes. |

## Error handling and recovery

| ID | Required behavior |
| --- | --- |
| H01 | Access-denied native code 5 or UnauthorizedAccessException, including wrappers, displays Read more... beneath the error. Ordinary error still has its full message box. Other errors do not show this help link. |
| H02 | Read more... opens an owned, formatted, scrollable modal with exactly STRINGS.md's five paragraphs, bold/code emphasis, three external-browser links, and Close. Preserve mapping inputs. Clear link on input edits/new operations. |
| C01 | Native/wrapped error 1219 or HRESULT 0x800704C3 initially suppresses the ordinary error box and opens the specialized owned connection review. Match codes, not localized text. |
| C02 | Review shows original error, server, reported login(s), connection rows (including deviceless uses), status/open references where known, discovery warnings, and the exact continuation prompt and three actions. |
| C03 | use existing login instead replaces username with the one reported distinct active account, clears/disables password, and retries mapping using that existing desktop session. No password retrieval/guessing. Disable when no unambiguous known active account is reported. |
| C04 | drop all connections and retry with provided credentials opens a scoped Yes/No warning, default No. No returns to the review. Yes confirms forced drop of the reviewed current-user connections on that exact named server, then retries originally supplied credentials. |
| C05 | Cancel operations, Escape, or review close cancels the whole mapping flow and closes the mapping editor to the drive list. |
| C06 | Reject incomplete/empty bulk lists; reread and compare reviewed connection identities; recheck each mapped target before disconnecting. Stop on partial failure or remaining/reopened resources. Do not disconnect similar hostnames, resolve aliases, or touch other logons. |
| C07 | A recurring 1219 opens the review again. Both OK and Test can enter recovery. A retry action selected after a Test conflict attempts to MAP the selected letter, rather than merely repeating Test. |

## Removal

| ID | Required behavior |
| --- | --- |
| R01 | REMOVE acts only on the clicked known network mapping. Check current kind, target and persistence against the displayed snapshot before disconnecting. Remove its remembered mapping too; never delete server files. |
| R02 | Initial removal is always non-forced. Only native Win32Exception code 2401, through wrappers, offers force. Code 2404, other errors, and the text 2401 alone do not qualify. |
| R03 | Show original error plus the exact scoped warning/question in STRINGS.md. Yes/No, owned by main window, warning icon, No selected by default. |
| R04 | No returns to the usable main window without force. Yes rechecks the original snapshot and retries once with force=true for that letter only. A changed mapping is rejected. Retry failures show one ordinary error, with no repeated force prompt. |
| R05 | Reconcile inventory after completion/failure/No. Successful removal changes the same row to Available with EDIT only. Preserve other mappings and avoid blinking. |

## Exclusions and limits

No background service, scheduled mapping, installer, credential vault, automatic elevation, password discovery, guest-policy modification, server-file management, or automatic forced replacement is part of the app. Native APIs are not atomic ownership transactions; keep the existing prechecks and report race failures honestly. Exact explanatory wording and supplied artwork are requirements, not placeholders.
