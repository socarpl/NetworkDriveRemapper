# Implementation and function reference

## Architecture

The UI receives a `DriveMappingService`. The service depends on `INetworkDriveSystem`, which is
implemented by `WindowsNetworkDriveSystem` in production and `FakeNetworkDriveSystem` in the tests.
This boundary lets the regression suite exercise disconnect/map/rollback decisions without modifying
the user's real drive mappings. Snapshot records never contain credentials.

The core and application target `net8.0-windows`. `Directory.Build.props` enables nullable checks,
implicit usings, deterministic builds, XML documentation output, and warnings as errors. There are
no external NuGet dependencies. `global.json` selects a known SDK baseline and permits newer feature
bands within that SDK's major/minor version. The manifest uses `asInvoker`; elevation would put
network mappings in a different logon context from the ordinary desktop Explorer.

## Data and input functions

### `DriveKind` and `DriveEntry`

`DriveKind.Free` means no local drive, DOS device reservation, remembered network mapping, or ambiguous
discovery result owns the letter. `Network` means there is a known mapped target. `Occupied` protects
local disks, removable drives, virtual devices, and uncertain ownership.

`DriveEntry` stores the uppercase letter, kind, grid description, optional UNC path, and persistence
flag in an immutable record. The positional constructor creates that snapshot. `DeviceName` produces
the native device name such as `Z:`; `CanMap` is true only for free letters or network entries with a
known target. This additional path check prevents attempting rollback of an unknown network location.

### `ConnectionOptions`

| Function/member | Detailed behavior |
| --- | --- |
| Constructor | Normalizes the target, trims the username, preserves password whitespace, and requires a nonblank username in Credentials mode. Empty passwords are allowed. Anonymous mode discards any supplied username/password and stores empty strings. Null characters in credentials are rejected before native string marshalling. |
| `NormalizePath` | Trims surrounding whitespace and trailing backslashes. Requires a UNC server and share. Rejects local/device paths, empty components, traversal components, control characters, trailing dots/spaces, and invalid Windows filename characters. Does not contact the network. |
| `ShareRoot` | Returns just `\\server\share` from the validated path so authentication can occur at the share before accessing a nested directory. |
| Private constructor | Validates shared input for ordinary credentials, anonymous access, and existing-session mode. The public constructor always uses ordinary/anonymous mode. |
| `ForExistingLogin` | Creates a named-account retry with `UseExistingLogin` true and no password retained. The Windows adapter passes a null password, distinct from the empty password used for anonymous/ordinary empty-password attempts. |
| `ToString` | Returns the target path only. Never includes the username or password in incidental diagnostic output. |
| Properties | `Path`, `UseCredentials`, `Username`, `Password`, and `UseExistingLogin` are read-only. Options exist only in memory and are captured once when the action starts. |

### Connection and conflict models

`NetworkConnection` is an immutable snapshot of local device, remote UNC resource, reported account,
readable status, and optional open-reference count. It has no password field. `Identity` combines device,
path, and login for confirmation-snapshot comparison.

`ServerConnectionReport` contains the normalized target, a current-logon connection list, and discovery
warnings. `Server` extracts the UNC host via `GetServer`; `ContainsServer` compares exact hostnames,
case-insensitively and ignoring a trailing DNS dot. It does not resolve DNS aliases or match hostname
prefixes. `ExistingLogin` returns one distinct known account from connected entries, otherwise null.
`CanDisconnect` requires a nonempty list with no incomplete-enumeration warnings.

`CredentialConflict.IsConflict` walks exception wrappers and recognizes native code 1219 or its
Win32 HRESULT `0x800704C3`. It never matches English/localized exception text.

## Mapping-policy functions

### `DriveMappingService`

| Function | Inputs, effects, and failures |
| --- | --- |
| Constructor | Receives a Windows/test boundary through the primary constructor. The UI never calls native imports directly. |
| `GetDrives` | Calls the boundary, sorts A–Z, and optionally removes every entry whose kind is not Free. Filtering a mapping out does not disconnect it. |
| `TestConnection` | Delegates validated options to the isolated remote-access check. Does not use the selected drive letter. |
| `OpenLocation` | Passes exact target-path text to the Windows boundary. Does not validate connection options, test connectivity, authenticate, or apply a mapping. |
| `OpenDrive` | Delegates browsing of a mapped letter, either from its main-window OPEN action or after mapping when the checkbox is checked. |
| `Remove` | Rejects non-network entries, calls EnsureUnchanged against fresh inventory on every attempt, then unmaps only that letter. Requires a known target and matching kind/path/persistence. The optional force flag defaults to false; the UI passes true only after explicit Yes to error 2401. Does not test connectivity, reconnect, or drop other server connections. The adapter removes the remembered profile. Failures propagate to the main window. |
| `GetServerConnections` | Delegates current-logon account/resource discovery for the UNC target's server. |
| `GetRetryDrive` | Reads the selected letter again after a failed attempt/recovery decision. Allows a free letter (including failed rollback or confirmed removal), or the original mapped path. Rejects a new local device or unrelated mapping before a retry can overwrite it. |
| `DisconnectServer` | Requires a complete reviewed list belonging only to the target server, rereads it, and compares resource/account identities. A changed list prevents any drop. Disconnects devices first, then deviceless resources with force true, after the controller received the user's Yes. Counts completed disconnections and stops with an explicit partial-result error if any later operation fails. Rereads afterward and refuses to map if resources remain, were reopened, or cannot be verified. |
| `EnsureUnchanged` | Validates A–Z and editability, reads fresh inventory, and compares kind, case-insensitive target, and persistence with the dialog's original snapshot. A changed/reserved letter raises an actionable error. No mapping calls occur. |
| `Apply` | Calls `EnsureUnchanged`, tests the target, and calls `EnsureUnchanged` again. Disconnects only an existing network mapping, then makes the new persistent mapping. A failed preflight or disconnect leaves the old mapping intact. If mapping fails after a successful disconnect, calls `Restore` with the original snapshot. Reports successful rollback or combines mapping/recovery errors with the original target. A free-letter failure has no rollback target. |

Windows APIs provide no transaction or ownership compare-and-swap. The two snapshot checks reduce
stale-dialog risk. Native errors handle races after those checks; no code force-disconnects another owner.

## Windows adapter functions

### `WindowsNetworkDriveSystem`

| Function | Detailed behavior |
| --- | --- |
| `GetDrives` | Reads the logical-drive bitmask, then inspects all 26 letters. For each, obtains drive type, DOS device target, live network target, and any remembered `HKCU\Network\<letter>\RemotePath`. Local volumes take precedence over stale remembered mappings. Known live/saved UNC targets become Network entries, with offline status where appropriate. Unknown network targets and unexpected discovery errors are reserved. Reads no network directory contents and writes no registry data. |
| `ReadNetworkTarget` | Calls `WNetGetConnection` with a character buffer, retries once with the required length on `ERROR_MORE_DATA`, and accepts target text for success or `ERROR_CONNECTION_UNAVAIL`. Other results preserve the native error for conservative classification. |
| `DescribeLocalType` | Maps Win32 drive types 2, 3, 5, and 6 to removable, hard drive, CD/DVD, and RAM-disk labels. Other values get a reserved-device label. SUBST targets beginning with `\??\` are labeled separately in discovery. |
| `TestConnection` | For ordinary credentials/anonymous access, uses `LogonUser` type 9/provider 3 to clone the current identity into a separate outbound network logon. Splits `DOMAIN\user` for the token API; leaves UPN/unqualified users intact. Anonymous tests clone the current username/domain but pass explicit empty credentials to WNet. Under `RunImpersonated`, connects the share, verifies the directory, and cancels the temporary connection in `finally`, then disposes the token. For existing-session mode, rechecks the reported active login and verifies the directory in the normal desktop session without creating an isolated login. |
| `ConnectShare` | Calls `WNetAddConnection2` with disk type, share root, no local letter, and `CONNECT_TEMPORARY`. Passes explicit strings for both credentials; anonymous mode passes empty strings. Throws a native error on failure. |
| `VerifyDirectory` | Uses `File.GetAttributes` to require a directory, then advances a directory-entry enumerator once. Preserves filesystem/access exceptions that `Directory.Exists` would hide. Empty directories succeed. Disposes enumeration before disconnecting the test session. |
| `Map` | Validates the letter and calls `WNetAddConnection2` with the full target, device name, requested login, and persistent or temporary flag. Existing-session mode sends the reported username and a null password so Windows can reuse that account's connection; ordinary modes send explicit password strings. New UI mappings request persistence. Provider/authentication errors still propagate, and Windows decides whether it can map a nested directory. |
| `Unmap` | Validates the letter and calls WNetCancelConnection2 for that device with CONNECT_UPDATE_PROFILE and the supplied force flag (false by default). Ordinary replacement remains non-forced. A confirmed single-letter retry after error 2401 passes true. Reports native failures without canceling by server name or removing unrelated letters. |
| `Restore` | Reuses the old device, path, and persistence setting. Passes null credentials so Windows uses defaults/existing credentials. Cannot recover an old password, and never sends the replacement password to an unrelated previous target. |
| `OpenLocation` | Calls `LaunchExplorer` directly with the entered path. Does not call preflight, share connection, or directory verification. Explorer handles access and authentication in its own Windows context. |
| `OpenDrive` | Validates the letter, then opens its root such as `Z:\` after a successful mapping. |
| `LaunchExplorer` | Starts `explorer.exe` using `ProcessStartInfo.ArgumentList` with exactly one path argument. Does not use shell command construction, put credentials on the command line, or create a password-bearing process. Disposes the process wrapper without closing Explorer. |
| `ValidateLetter` | Throws for any character outside uppercase A–Z before a mutation or drive-root launch. |
| `CreateError` | Converts a native code to a `Win32Exception` with action text and the code. Queries provider-specific text immediately for error 1208. Adds hints for credential conflicts, denied/invalid logins, missing paths, and open files. Does not echo password values. |

### Current-logon connection functions

The partial implementation in `WindowsNetworkDriveSystem.Connections.cs` provides enumeration and
disconnection without the elevated SMB CIM provider or PowerShell subprocesses.

| Function | Detailed behavior |
| --- | --- |
| `GetServerConnections` | Normalizes the UNC path, combines SMB uses, connected provider resources, and mapped-drive snapshots, filters to the exact named server, and merges duplicate device/path rows. Prefers known account and connected-state metadata while retaining open-reference counts. Records per-source enumeration failures as warnings instead of pretending the list is complete. No server files are read and no connections are changed. |
| `ReadSmbConnections` | Calls `NetUseEnum` level 2 with a resume handle until all pages are read. Marshals device/path/status/account/reference metadata, never the native password pointer. Obtains provider account names via `WNetGetUser` where possible and falls back to reported domain/username. Frees every NetAPI buffer in finally, including partial-result buffers. Rejects a non-advancing enumeration. |
| `ReadConnectedResources` | Opens `RESOURCE_CONNECTED` enumeration, reads `NETRESOURCEW` buffers, grows the buffer on `ERROR_MORE_DATA`, and stops on `ERROR_NO_MORE_ITEMS`. Records mapped/deviceless resources with provider-reported accounts. Bounds growth to avoid an invalid provider allocating excessive memory; frees the buffer and closes the enumeration handle in finally. |
| `ReadMappedConnections` | Supplements enumeration with known mapped or remembered network letters. Marks disconnected entries accordingly and queries account names when available. |
| `ReadConnectionLogin` | Calls `WNetGetUser` for a device or UNC resource, resizes a too-small character buffer, and returns null if Windows cannot report the account. |
| `CombineLogin` | Qualifies unqualified usernames with their reported domain; preserves UPN/already-qualified usernames and returns null for an absent username. |
| `EmptyToNull` | Converts blank device/account provider fields to null. |
| `DescribeConnectionStatus` | Translates USE_INFO_2 status codes to connected/paused/disconnected/error/connecting/reconnecting labels. |
| `DisconnectConnection` | Rechecks each mapped device's actual or remembered UNC target immediately before cancellation. A changed target is not disconnected. Calls `WNetCancelConnection2` on the exact device or deviceless UNC resource; mapped devices use `CONNECT_UPDATE_PROFILE` to remove remembered mappings. Only the confirmed bulk workflow passes force true. Already released resources are benign; other native failures stop the batch. |

### `NativeMethods`

All string imports use Unicode. `NetworkResource` matches the sequential NETRESOURCEW layout: four
integer fields followed by local name, remote name, comment, and provider pointers. Only disk type,
local name where needed, and remote name are populated; unused fields remain zero/null.

| Native declaration | Return contract and use |
| --- | --- |
| `GetLogicalDrives` | A bitmask where bit 0 is A. Zero is treated as an enumeration failure; reads last error. |
| `GetDriveType` | A numeric type for a root path; does not enumerate directory contents. |
| `QueryDosDevice` | Populates a DOS target buffer. Detects reservations omitted from ordinary drive discovery. Last error 2 means no device; other failures do not prove availability. |
| `WNetGetConnection` | Returns a Windows error code directly, with buffer length in characters and optional remote target. |
| `WNetAddConnection2` | Returns a direct code; 0 is success. `CONNECT_UPDATE_PROFILE` (1) remembers a letter; `CONNECT_TEMPORARY` (4) avoids persistence. |
| `WNetCancelConnection2` | Returns a direct code and marshals force as a Win32 BOOL. Normal tests, replacements, and initial removals pass false. Only confirmed single-letter error-2401 retries and confirmed error-1219 server drops pass true. Device names disconnect only the selected letter. |
| `WNetGetLastError` | Retrieves extended provider text after WNet error 1208. |
| `LogonUser` | Returns BOOL and last error, with an owning `SafeAccessTokenHandle`. Type 9/provider 3 creates the isolated outbound credential context. |
| `NetUseEnum` | Returns current-user SMB-use buffers at level 2, with a DWORD resume handle and direct native status code. No special group membership is required. |
| `NetApiBufferFree` | Frees buffers allocated by NetUseEnum. |
| `WNetOpenEnum` | Returns a handle for the caller's connected provider resources. |
| `WNetEnumResource` | Reads NETRESOURCEW entries and returns required buffer size or completion status. |
| `WNetCloseEnum` | Releases the enumeration handle. |
| `WNetGetUser` | Reports the username used for the requested device/UNC connection; no password is returned. |

`NativeMethods.UseInfo2` matches USE_INFO_2 pointer/DWORD layout. Its password member remains an opaque
pointer and is never copied into managed text. All returned buffers/handles are released explicitly.

## WinForms functions and handlers

### Application entry

`Program.Main` is marked STA, selects per-monitor-v2 DPI behavior, enables Windows visual styles,
creates the real adapter/service, and starts `MainForm` in the WinForms message loop.

### `MainForm`

| Function | Detailed behavior |
| --- | --- |
| Constructor | Stores the service, sets the exact application title and desktop sizing, constructs the controls, and wires events. Tries to fit all 26 rows on tall screens; smaller screens use scrolling. Uses native resizable controls and DPI scaling. |
| Internal constructor | Accepts injectable force-confirmation and removal-error callbacks. The public constructor supplies real owned MessageBox callbacks; tests use recording callbacks while executing the same REMOVE event handler. Owns the same controls and source images in either case. |
| `BuildLayout` | Builds heading/instruction rows, the unchecked filter and borderless icon-only Refresh button, the buffered read-only drive/status grid with OPEN, REMOVE, then EDIT action columns, and footer counts. Removes outer, cell, and header borders. Disables insertion, deletion, row resizing, and sorting. Row tags carry immutable snapshots for later actions. Refresh has an accessible name and F5 tooltip. |
| `RefreshDrivesAsync` | Prevents overlapping actions; disables filter/Refresh, reads inventory with `Task.Run`, reconciles the cached snapshot on the UI thread, and restores controls in `finally`. Keeps the grid visually enabled for selection/scrolling. Displays an error if discovery fails. |
| `PopulateGrid` | Reconciles the filtered inventory in place. Removes only letters excluded by filtering, inserts missing letters in order, and updates only snapshots whose record values changed. Unchanged rows retain native identity and height. Uses blue for available, green for network, and gray for occupied. Assigns OPEN/REMOVE/EDIT images, values, and tooltips according to availability and invalidates changed rows only. REMOVE requires a known network target; free entries retain only EDIT. Restores selected letter/action column and scroll index; reports shown/free/network counts. |
| `EditSelectedDriveAsync` | Ignores reserved entries and busy state, then delegates the current row snapshot to `EditDriveAsync`. |
| `EditDriveAsync` | Opens a disposable owned modal `MappingDialog` for the supplied free/known mapped entry. Refreshes after any dismissal, including failure/cancel, so external changes become visible. |
| `HandleRowActionAsync` | Validates row/header/busy state and uses the clicked row snapshot independently of previous selection. EDIT opens the mapping dialog; REMOVE delegates to RemoveDriveAsync for known network entries; OPEN launches the mapped letter on a worker thread. Explorer errors are displayed and controls restored in finally. |
| `RemoveDriveAsync` | Guards overlapping actions and first tries checked non-forced removal on a worker thread. Catches only typed error 2401 for an owned confirmation. No ends the operation; Yes invokes Remove again with force true and the original snapshot, rechecking ownership after confirmation. An outer error handler reports ordinary failures or failed retries once without another force prompt. Restores busy state in finally and always refreshes in place afterward. |
| `IsOpenFilesError` | Walks exception wrappers for Win32Exception.NativeErrorCode 2401. Other codes, including 2404, and message-only matches are not eligible for a forced retry. |
| `BuildForceRemovalPrompt` | Preserves the original error text, identifies the selected letter and UNC target, explains interrupted applications/transfers and possible loss of unsaved changes or pending writes, and asks the explicit force question. |
| `ConfirmForceRemoval` | Shows a warning MessageBox owned by the main window with Yes/No and Button2 (No) selected by default. Only DialogResult.Yes returns true. |
| `ShowRemovalError` | Shows an owned OK/error MessageBox for unrelated removal failures or a failed forced retry. Does not repeat confirmation. |
| `CreateActionColumn` | Creates an unsortable, non-resizable native button column with a `DriveActionCell` template, blank header, and no inherited text padding. |
| `UpdateActionColumnWidths` | Matches all three action column widths to the largest current row height (template height when empty). Reacts to row-height, row-list, action-width, and DPI changes. Changes widths only when needed, avoiding redundant whole-grid invalidation. Guards against disposal and missing columns. |
| `SizeRefreshButton` | Makes Refresh square using the current toolbar height minus its DPI-scaled vertical margins. Runs when toolbar size changes. |
| `SetBusy` | Updates the action guard, disables filter/Refresh, and changes the wait cursor. Keeps the grid enabled so short Explorer launches do not gray and repaint every row. Every action handler checks the guard; selection and scrolling remain available. |
| `Dispose` | Disposes native controls before releasing the window-owned application icon, source bitmaps, and tooltip. Cells/buttons share those images without owning them. |

Event handlers: Shown and Refresh/F5 run discovery. Filter changes only redraw cached rows.
Double-clicking drive/status text calls the editor. Single-click or Space activates a native action
button; Enter activates the current action cell or opens the editor from text cells. Icon double-clicks
do not dispatch an additional text-row edit. Header clicks are ignored. FormClosing
refuses closure while inventory work is in progress, preventing continuations from using disposed controls.

### Icon resources and controls

The application project embeds the original root-level `icon_edit2.png`, `icon_open.png`,
`icon_remove.png`, and `icon_refresh.png` with fixed logical resource names. MainForm owns and disposes
all four source bitmaps. No working-directory image files are needed at runtime.

The application project also sets ApplicationIcon to assets/app-icon/NetworkDriveRemapper.ico, embedding
the Windows executable icon, and embeds that same ICO as a managed resource for MainForm.Icon.
MainForm owns its cloned icon and disposes it after the controls. Both shell and window icons therefore
use the same artwork without a runtime file dependency. The solution's Assets folder lists the original
ICO, PNG, provenance notes, and row/toolbar image files; all are included in local Git history.

| Function/member | Detailed behavior |
| --- | --- |
| `BufferedDriveGrid` constructor | Enables the protected DataGridView double-buffering option, presenting complete frames during selection, scrolling, and inventory updates. |
| `IconAssets.Load` | Reads the named embedded resource and clones it into an independently owned bitmap before closing the stream. Missing assets fail explicitly. The requesting window disposes the returned image. |
| `IconAssets.LoadApplicationIcon` | Reads the embedded Application.ico resource, constructs and clones the multi-size icon before closing the stream, and returns an independent window-owned icon. Missing resources fail explicitly. MainForm assigns it to Icon for title-bar/taskbar use. |
| `IconAssets.ImageBounds` | Insets the current control/cell rectangle by exactly two physical pixels on every side, independent of source size or DPI. Returns empty if the control is too small to contain the margins. Both painting and row-action hit testing use this rectangle. |
| `IconAssets.Draw` | Stretches the complete source canvas into the exact supplied current rectangle, both above and below the original image size. Uses bicubic interpolation and mirrored edges for transparency. Disabled images receive a grayscale/alpha matrix. Ignores empty bounds and restores graphics state in finally. No fixed-size image cache or source-size cap is used. |
| `StretchedIconButton.ActionImage` | Stores a shared, window-owned source reference. |
| `StretchedIconButton` constructor | Selects flat styling with zero border and buffered, resize-aware painting. Retains native button keyboard and accessibility behavior. |
| `StretchedIconButton.OnPaint` | Explicitly fills the parent-colored background, then draws the image inside its two-pixel margins. Skips native Button.OnPaint so no frame appears through transparent PNG pixels. Adds a keyboard-focus indicator inside the icon when space permits. |
| `DriveActionCell.ActionImage` | Stores a shared source reference; null removes both the painted button and its clickable content. Text values still identify OPEN/REMOVE/EDIT for native accessibility. |
| `DriveActionCell.Clone` | Preserves the image reference when WinForms clones the column template. |
| `DriveActionCell.GetContentBounds` | Returns the same current button rectangle used for painting, or empty for an unavailable action/header. This aligns native button hit testing with the displayed icon. |
| `DriveActionCell.Paint` | Paints the cell background/selection without default button/text content, then draws only the image inside its two-pixel margins. No button frame is drawn. Preserves keyboard-focus feedback and disabled-image support. |

### `MappingDialog`

| Function | Detailed behavior |
| --- | --- |
| Constructor | Stores the original drive snapshot and service. Prefills the old UNC target. For a free letter, selects Credentials and prefills WindowsIdentity.GetCurrent().Name (DOMAIN\user or COMPUTER\user), disposing the identity token wrapper immediately afterward. The account prefix is the real account domain/computer, not its network workgroup. Password starts blank and no authentication occurs during initialization. Existing mapped entries retain Anonymous with blank credential fields. Checks opening after mapping, wires actions, assigns OK/Cancel keyboard behavior, and clears the password on FormClosed. |
| `BuildLayout` | Creates a target field, the mutually exclusive login radio group with Credentials first and Anonymous second in visual/tab order, labeled username/masked-password fields, checkbox, status/progress area, and four buttons. A hidden AutoSize LinkLabel follows the status text and becomes visible for access denial. Explains direct Explorer opening and the guest-policy requirement. All controls have stable names. |
| `CreateInputRow` | Creates one label/input table row; the label has a fixed width and the textbox fills remaining space. |
| `ConfigureButton` | Sets each action button's stable control name, visible caption, size, and margin. |
| Internal constructor | Injects the real recovery-dialog factory for automated tests; the public constructor selects the production dialog/confirmation path. |
| `UpdateCredentialControls` | Enables named credential inputs only outside native work. The password is disabled in existing-session mode because it cannot be recovered; editing the username/path or changing login mode returns to ordinary credentials. |
| `ReadOptions` | Constructs validated ordinary or existing-session options from the current controls when an action begins. |
| `RunOperationAsync<T>` | Captures action-specific input: validated options for Test, raw path text for Browse. Runs the worker operation and restores controls. Test enables 1219 recovery: it suppresses the ordinary message box and enters `ApplyMappingAsync` with the initial conflict. Browse remains independent of credential validation. |
| `ApplyMappingAsync` | Captures original credentials/checkbox and runs a retry loop. An initial/native/wrapped 1219 loads server connection metadata and opens the owned conflict modal instead of the ordinary message box. Reuse substitutes reported login plus password-free session mode. Confirmed drop disconnects the reviewed resources, rebases the selected letter, and restores the originally supplied options for retry. Rechecks letter ownership before changes. Cancel closes the mapping editor back to the drive list. Recurring 1219 is offered again rather than silently disconnecting. On success optionally opens Explorer and closes with OK; ordinary failures leave the editor available. |
| `ReflectConnectionOptions` | Replaces visible username/login mode/password with the chosen retry input while busy, preserving existing-session state through programmatic control events. Never invents a stored password. |
| `ConnectionInputChanged` | Manual edits clear existing-session mode and stale status/help, then restore ordinary credential-field behavior. Programmatic updates during work do not clear the chosen retry mode. |
| `ShowError` | Calls `SetErrorFeedback` and shows the complete error in a message box so long native/recovery messages remain readable. |
| `SetErrorFeedback` | Displays red error text and sets contextual-help visibility from `IsAccessDenied`. Shared by the real error path and browser-free regression checks. |
| `IsAccessDenied` | Walks `InnerException` wrappers and recognizes `Win32Exception.NativeErrorCode == 5` or `UnauthorizedAccessException`. Does not match English/localized message strings. Unrelated errors do not show the link. |
| `ShowCredentialsHelp` | Creates/disposes `CredentialsHelpDialog` and calls `ShowDialog(this)` for owned modality. The original target and login inputs remain intact. Ignores activation while an operation is busy. |
| `ClearStatus` | Clears previous test/browse feedback and contextual help when target/login input changes; avoids displaying success or error help for stale input. |
| `SetBusy` | Disables editable fields, login radios, checkbox, and every action button; updates the marquee and cursor. Hides old help when a new operation begins and disables its activation during work. Calls `UpdateCredentialControls` when returning to idle. |

Event handlers: Test passes `ReadOptions` and its operation delegate to `RunOperationAsync`.
Open in Explorer passes a reader of the exact path textbox text and its launch delegate, with no
connectivity or credential validation. OK calls
`ApplyMappingAsync`. Cancel closes without calling mapping APIs. Credential selection enables fields;
changes to target/login values clear previous status. Shown focuses the path. FormClosing is blocked
only during work; FormClosed clears the password textbox. Anonymous and Credentials are in the same
radio-button container, so exactly one can be selected. The Read more LinkClicked handler opens the
credentials explanation modally. Its AutoSize row collapses when the link is hidden.

### `CredentialsHelpDialog`

The dialog preserves every paragraph, source caption, and URL from the requested explanation, including
the original follow-up sentence. Markdown bold/code styling is rendered with native RichTextBox fonts.
Three separate LinkLabel controls follow their respective paragraphs and open external documentation.
There is no embedded browser and no network request when the explanation itself opens.

| Function | Detailed behavior |
| --- | --- |
| Public constructor | Uses `OpenDefaultBrowser` as the external link opener and constructs the formatted explanation. |
| Internal constructor | Accepts an `Action<Uri>` browser opener for regression checks, initializes dialog sizing/style, owns the formatting fonts, and calls `BuildLayout`. |
| `BuildLayout` | Adds a heading, an AutoScroll panel containing the exact original paragraphs, and a Close button. The single-column content grows as paragraphs wrap; Close and Escape dismiss the help window. |
| `AddParagraph` | Creates a read-only, selectable RichTextBox with named bold/code spans. `ContentsResized` adjusts its height after wrapping to avoid clipped text. Optionally adds a native LinkLabel with the original caption and `Uri` in LinkData. Its actual LinkClicked handler invokes the external opener, marks successful links visited, and shows an error if browser launch fails. |
| `FormatSpan` | Locates an exact emphasized substring and applies its font without altering the wording. Missing spans fail explicitly rather than silently losing formatting. |
| `OpenDefaultBrowser` | Calls `Process.Start` with the fixed HTTPS documentation URL and `UseShellExecute = true`, allowing Windows to select the user's external browser. Disposes only the process wrapper. |
| `Dispose` | Disposes native controls and the bold/code fonts owned by this dialog. |

`Properties/AssemblyInfo.cs` grants access to internal UI test seams only to the regression assembly.
Tests can display error feedback without leaving a real message box open and replace the browser opener
without launching browser windows. Production calls still use the actual message-box and browser paths.

### `CredentialConflictDialog`

`CredentialConflictResolution` represents Cancel, UseExistingLogin, or DisconnectAndRetry. The modal
only gathers a decision; the mapping service performs network changes after it closes.

| Function/member | Detailed behavior |
| --- | --- |
| Public constructor | Uses the production Yes/No warning callback. |
| Internal constructor | Accepts an injectable confirmation callback, stores the connection report, and builds the real modal UI for both production and non-destructive tests. |
| `Resolution` | Defaults to Cancel, including Escape/window-close dismissal. |
| `BuildLayout` | Displays the unchanged original error in a scrolling read-only field, actual login/server, a connection grid, discovery limitations, the exact continuation prompt, and three requested buttons. Unknown/ambiguous login disables reuse; incomplete/empty listing disables bulk drop. No network call occurs in this dialog. |
| `CreateButton` | Creates consistent native action buttons with stable names; all three fit at the minimum supported window size. |
| `BuildConfirmationText` | Lists affected resources/accounts and explains unsaved-work loss, interrupted applications/transfers, removed remembered drive mappings, lack of automatic restoration, and possible reconnects. Ends with the exact question Do you really want to proceed? |
| `ConfirmDisconnect` | Opens an owned Yes/No warning with No as the default. No keeps this same review open; Yes selects the destructive retry action. |
| `Complete` | Records the explicit choice, sets the matching DialogResult, and closes only the review window. The mapping controller subsequently executes or cancels the workflow. |

## Test runner and fake boundary

`tests/Program.Main` initializes an STA WinForms context with an invisible root Application.Run window,
runs 33 named checks, prints results, and returns nonzero if any fail. The root loop keeps the same
synchronization context alive across worker continuations and nested modal dialogs, matching production.
The tests cover both successful and unsuccessful operations; they do not merely
assert the implementation's internal representation. Each test function has XML documentation describing
the behavior it verifies. `--render` enables artifact output.

`FakeNetworkDriveSystem` creates A–Z in memory. `GetDrives` returns snapshots and records reads;
`TestConnection` records inputs, optionally mutates ownership during preflight, and can throw;
`Map` records exact credentials/persistence and can fail; `Unmap` records the letter/force pair in
UnmapAttempts, consumes a queued UnmapFailures result (or the default UnmapError), and releases only
that fake letter on success;
`Restore` records/restores the original snapshot or fails; `OpenLocation` and `OpenDrive` record browsing
without starting processes; `Set` simulates ownership changes. Error properties allow deterministic
preflight, open-file, credential-conflict, and recovery failures.

Runner helpers: `OldMapping`, `ExistingMappingSystem`, and `NewOptions` create repeatable inputs.
`ShowOffscreen` shows native windows outside the visible desktop so normal control/async behavior occurs.
`PumpUntil` processes STA messages with a ten-second bound; it is test code, not production async logic.
`Find` locates named controls recursively. `SaveRender` writes complete form images using DrawToBitmap.
`Check`, `Equal`, and `Throws` implement assertion failures and inspect expected exceptions.
`DialogControls` verifies free-letter Credentials defaults, the actual qualified Windows identity,
empty masked password, enabled inputs, radio visual/tab order, mode toggling without losing the prefill,
and preservation of Anonymous defaults/target for existing mappings. `DialogMapping` also verifies that
OK forwards the default named account and blank password as ordinary credentials, without selecting
existing-session reuse. Reading the current token identity does not retrieve its password.
`NativeInventory` and `NativeServerConnections` use the production Windows boundary read-only. They
read drive/connection metadata without mapping, disconnecting, opening Explorer, or reading share files.

`MainRowActionsAndScaling` verifies OPEN/REMOVE/EDIT order and conditions for free, network, and occupied rows,
including empty hit areas for absent actions. It activates real grid action handlers to verify the clicked
letter opens in Explorer and free/mapped EDIT opens the correct owned mapping dialog. It checks Enter,
Refresh inventory changes, accessible Refresh labeling, square toolbar sizing, 16- and 48-pixel rows,
and 1.5x control scaling. Button hit bounds must match the current image destination size with 2px margins.
`AssertRefreshMargin` renders an opaque test source through the actual Refresh control and verifies every
margin pixel matches the background, catching leaked button frames. It restores the original image afterward.
`ActivateGridAction`
raises the native protected CellContentClick event through reflection. All Explorer/network effects are fake.
Optional renders include `main-icons-row-16.png`, `main-icons-row-48.png`, and `main-icons-scaled.png`.
After rendering, it activates REMOVE by click and Enter, verifies the correct letter becomes free,
and checks that OPEN/REMOVE disappear while EDIT remains, without row replacement or grid disabling.

`RemoveMapping` checks the service's exact removal call sequence, preservation of unrelated mappings,
and removal of persistence. It rejects free/local/unknown/invalid letters and changed path/kind/persistence.
An open-file error must preserve the original mapping with no attempted reconnection or forced server drop.

`ForceRemovalDetection` checks native/wrapped 2401 recognition, rejects other codes and text matches,
and verifies original error preservation, drive identity, work-loss warning, and explicit force question.
`ForceRemovalWorkflow` executes the real REMOVE handler with injected owned-message callbacks and
fake Windows effects. It covers No, Yes, wrapped error, failed forced retries (including recurring 2401),
changed ownership during confirmation, and unrelated errors. Assertions verify exact force flags/letters,
one prompt at most, error reporting, unrelated mappings, return to a usable main window, and row reuse.
No real disconnection or native message box occurs in these regression scenarios.

`MainRefreshStability` exercises actual OPEN, modal EDIT/close, unchanged Refresh, and changed-inventory
Refresh. It asserts no rows are removed/added, no grid EnabledChanged events occur, and existing row
objects/heights, selected action cell, and scroll position survive. A changed mapping must still update
its displayed target. This guards the two concrete causes of blinking while preserving inventory correctness.

`CredentialsHelpAndRender` verifies native error-5 and wrapped/directory access-denial detection, absence
of help on unrelated errors, link placement, stale-help clearing, the unchanged original explanation,
bold styling, all three external URLs, Close, and owned modality. `ActivateLink` invokes a LinkLabel's
protected native event through reflection so tests execute its real handler without using the mouse.
`IsWindowEnabled` reads the native owner-window state: ShowDialog disables that window without changing
the managed Control.Enabled property. Modal assertions run in ShowDialog's nested message loop and close
only the help window. Optional renders include `mapping-access-denied.png` and `credentials-help.png`.

Recovery checks: `ConflictOptions` verifies typed/wrapped/HRESULT 1219 detection and password-free reuse.
`ServerScopedDisconnect` preserves unrelated fake servers; `DisconnectGuards` rejects incomplete, changed,
or cross-server lists; `DisconnectFailure` reports partial progress without mapping; `RetryOwnership`
rejects reassigned letters. `ConflictReviewControls` tests original error/account/resource information,
No/Yes/Cancel decisions, unavailable choices, and button layout at minimum size. `ConflictRecoveryWorkflow`
uses the actual OK/Test event handlers through all three choices, rollback wrappers, original-credential
retry, and existing-session mode with fake network effects. `ConflictError` and `ConflictSystem` supply
representative records. Optional renders include `credential-conflict.png` and its minimum-size version.

The fake adapter's `GetServerConnections` returns scoped snapshots/warnings. `DisconnectConnection`
records forced fake drops and supports queued failures. Mapping/unmapping/restoration update fake
connection records as well as drive state. Queued test/mapping failures model an initial conflict and
successful retry; no real connection is dropped by a regression check.

## Build script

`scripts/build.ps1` accepts `-RenderUi` and `-Publish`. It resolves the repository from its own location,
temporarily sets CLI/workload/telemetry environment values, builds Release, and runs the regression
executable without rebuilding. `Invoke-DotNet` accepts separate arguments and raises an error for nonzero
exit codes. With Publish, it publishes a runtime-dependent folder, copies the README, and creates a ZIP
containing that folder's files. `finally` restores the caller's environment and location even on failure.
The script does not alter Git history or disconnect real network resources.
