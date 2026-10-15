# NetworkDriveRemapper

A documented Windows Forms application for Windows 10 and Windows 11. Inspect every drive letter
from **A:** to **Z:**, test a network location, and map or replace a drive using anonymous access or
explicit credentials. The application runs as the signed-in user, without requiring administrator rights.

## Run the application

The supplied build is framework-dependent and uses the **.NET 8 Desktop Runtime (x64)**.
Install the **Desktop Runtime**, rather than only the general .NET runtime, from the
[official .NET 8 download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

- Published folder in this workspace: `artifacts\publish\NetworkDriveRemapper.exe`.
- Distributable package: `artifacts\NetworkDriveRemapper.zip`.
- Extract the entire ZIP into a folder and run `NetworkDriveRemapper.exe`. Keep the other files beside it.
- Run normally from your desktop account so Explorer and the app see the same drive mappings.

## Automatic GitHub releases

Commit and push changes to GitHub to run [Build release ZIPs](.github/workflows/release.yml).
It runs on every branch push, builds each new commit (including multiple commits in one push),
runs the regression checks, and publishes `NetworkDriveRemapper.zip` on the repository's
[Releases page](https://github.com/socarpl/NetworkDriveRemapper/releases).
Local commits start building once they are pushed. The ZIP contains the compiled application,
and requires the .NET 8 Desktop Runtime (x64) described above.

Only the **three newest successful automatic releases** are retained: current, previous, and
previous-1. After a new ZIP is uploaded successfully, older automatic releases, their ZIPs,
and their `build-<commit SHA>` tags are deleted. Failed builds/uploads preserve the previous
successful downloads. Manual releases and other tags are outside this automatic retention policy.
Git commits and source history remain available.

Pushes are queued and processed one at a time to keep publishing and cleanup consistent.
The first push of a new branch builds the new commits supplied by GitHub, without rebuilding
all earlier repository history. Repeated pushes of a retained commit reuse its release.
You can also select **Actions > Build release ZIPs > Run workflow** to build the selected branch tip.
GitHub Actions must be enabled; the workflow uses the built-in `GITHUB_TOKEN`, with no extra secret.

## Features and defaults

The executable and main window use the network-drive application icon in
[assets/app-icon](assets/app-icon/README.md). Its ICO contains seven sizes from 16 to 256 pixels;
the ICO, original PNG, and action images are tracked in Git and listed under Assets in the solution.
The icon is embedded, so running the app does not require a separate ICO file.

| Item | Behavior |
| --- | --- |
| Drive grid | Lists A–Z in alphabetical order, with a separate column for the UNC target or reservation status. |
| Free letter | Displays `Available` and a rightmost EDIT icon to configure its mapping. |
| Network letter | Displays its target; remembered unavailable mappings include `(Disconnected)`. Shows OPEN, REMOVE, then rightmost EDIT. OPEN opens the drive letter in Explorer; REMOVE disconnects its mapping; EDIT replaces its mapping when the target is known. |
| Local/reserved letter | Displays labels such as `Taken - Hard Drive`, `Taken - Removable Drive`, `Taken - CD/DVD Drive`, or `Taken - Virtual / SUBST Drive`. Cannot be edited. |
| Unknown ownership | Displays a conservative unavailable/reserved status and prevents editing. |
| Show only available | Unchecked by default. When checked, displays **unused letters only**, hiding both local drives and existing network mappings. |
| Open editor | Click EDIT, double-click the drive/status text of a free/network row, or select that text and press Enter. Enter on an action cell activates that action. |
| Target path | Accepts `\\server\share` and `\\server\share\folder`. An existing mapping is prefilled. |
| Credentials | First login option and default for available letters. Prefills the current Windows account as `DOMAIN\user` (or `COMPUTER\user` for a local account); password starts blank. Enables username and masked password fields; supports `user@domain` and server-local usernames too. |
| Anonymous | Second login option; supplies empty username and password. Remains the initial mode when editing an existing mapping. |
| Open directory after mapping | Checked by default. Opens the successfully mapped letter in Explorer. |
| Refresh | The borderless refresh icon above the grid reloads the current drive state; tooltip and keyboard shortcut F5. |
| Read more... | Appears below access-denied test/mapping errors. Opens a formatted modal explanation of Everyone permissions, anonymous credentials, and Guest access; documentation links open in the external default browser. |
| Login conflict recovery | Windows error 1219 opens a dedicated modal review with the existing login, server connections, and choices to reuse the login, confirm disconnection and retry, or cancel back to the drive list. |

## Mapping a drive

Row actions use the supplied `icon_open.png`, `icon_remove.png`, and `icon_edit2.png`; Refresh uses `icon_refresh.png`.
The images are embedded in the application, so separate PNG files are not needed when running it.
Each image grows or shrinks to the current icon area with **2px of margin on all four sides**,
including after row/control resizing or DPI scaling. Action columns follow the current row height.
The grid and icons have no visible borders. Local/reserved rows have no actions.

The grid uses buffered drawing and updates changed entries in place. Opening Explorer does not refresh
the list or disable/recolor every row. Returning from the mapping editor still checks for drive changes,
while retaining unchanged rows, the selected cell, row heights, and scroll position.

Click **REMOVE** on a mapped row to disconnect that letter and remove its remembered mapping.
The app first checks that the mapping still matches the displayed entry. Open files can prevent
normal removal. Windows error **2401** opens an owned Yes/No warning containing the original error,
the selected drive/target, and the risks of interrupted transfers and lost unsaved or pending writes.
**No** is selected by default and returns to the main window without forcing disconnection.
**Yes** rechecks that same mapping and retries once with force enabled. A failed retry displays its
error without asking again. Other errors use the ordinary error message and do not offer force.
Other drive letters and unrelated server connections remain
in place. The row becomes Available and retains only EDIT after successful removal. Files on the
server are not deleted.

1. Select an unused letter or an existing network mapping and click EDIT or double-click its drive/status text.
2. Enter a UNC share/directory path. Select Anonymous or Credentials and fill in the enabled fields.
   For an available letter, Credentials and the signed-in account name are already selected. The prefix
   comes from the account's Windows identity: its account domain or local computer, rather than the
   network workgroup. Enter the share password if required; the app does not retrieve your Windows
   password or automatically reuse a network session just because it prefills the username.
3. **Test connection** verifies authentication, directory existence, and directory-listing permission.
   A successful empty-directory listing also counts as success. Tests do not assign a letter.
4. **Open in Explorer** sends the current target-path text directly to Explorer. The app does not test
   connectivity, validate the login fields, or authenticate first. Explorer handles access and any
   Windows login prompts. This action does not change the selected drive letter.
5. Choose whether to open the directory after mapping, then click **OK**.
6. **Cancel**, Escape, or the window close button abandons the proposed mapping.

OK rechecks the letter, tests the target before changing anything, and checks the letter again after
the test. A free letter is mapped directly. An existing network mapping is disconnected and replaced.
The new mapping is remembered by Windows for future sign-ins. The app does not save passwords itself
or request that Windows Credential Manager save them; reconnection may require Windows to prompt again.

## Connection and failure behavior

- **Testing new credentials is isolated.** Each ordinary test creates a temporary outbound Windows logon session, connects to the
  share, reads the directory, disconnects that test session, and releases its token. Existing desktop
  drive mappings and network sessions are not disconnected by a test. An explicitly selected existing-login
  retry checks directory access in the desktop session so it can reuse the connection already established there.
- **Explorer opens the entered path directly.** The button also accepts local directory paths, independently
  of the UNC validation used for testing/mapping. It does not use the dialog's username/password or create
  a network session in the app. Explorer manages its own access, existing Windows sessions, and login
  prompts. No password is passed in Explorer's command line.
- **Anonymous access depends on Windows and the server.** Many SMB servers and Windows installations block
  guest/anonymous access. The app requests empty credentials and does not change guest-access policy or
  silently retry with a different login. Already established Windows server sessions may affect mapping.
- **Access-denied help is available in the dialog.** Windows error 5 and directory-access denials show
  **Read more...** below the error. Its modal window preserves the original explanation, emphasis, and
  three Microsoft documentation links. Links open in your default external browser. Closing help returns
  to the same mapping inputs. The help link clears when inputs change or a new action starts.
- **Credential conflicts (error 1219)** initially suppress the ordinary error message box and open the
  dedicated login-conflict dialog described below. Other servers and other logon sessions are outside
  the disconnection action; only connections reviewed for the target server can be dropped.
- **Open files prevent replacement.** Disconnecting an old mapping is never forced. Close files and Explorer
  windows using it, then retry.
- **Failed replacement triggers best-effort recovery.** If the old mapping was disconnected and the new one
  fails, the app tries to restore the old path and its persistence setting with Windows' default/existing
  credentials. Windows cannot reveal the old password, so recovery may fail. The error states whether the
  old mapping was restored; otherwise refresh and reconnect it using its original credentials.
- **External changes are detected.** If another application changes the letter while the dialog is open,
  the operation stops. Windows APIs are not a transaction; a final concurrent change can still produce a
  native failure, which is reported without force-removing the new owner.
- **Native calls can take time.** Tests and mapping run off the UI thread. Buttons and window closure are
  disabled while an operation is pending because Windows network operations cannot safely be canceled
  midway through a replacement. The progress indicator stays active until Windows returns.

## Recovering from Windows error 1219

The conflict dialog shows the original error in full, the target server, the login Windows reports using,
and a grid of connections to that server. The list includes mapped drives and connections without drive
letters, plus connection status and open-reference counts where available. Discovery uses C# P/Invoke
with `NetUseEnum`, `WNetOpenEnum`/`WNetEnumResource`, and `WNetGetUser` in the current user's logon;
the app does not require the elevated SMB PowerShell management provider.

Below the list, **Choose how you would like to continue** offers:

- **use existing login instead** replaces the username with the reported existing account and clears
  the password field. Windows cannot return that account's password. The retry checks the established
  desktop session and requests mapping with the same account and a null password. It does not guess a
  password or attempt an isolated login with a blank password. Windows can still reject a session that
  has expired or changed; that failure is reported. The password field is disabled in existing-session
  mode; editing the username/path or changing login mode returns to normal credential entry.
- **drop all connections and retry with provided credentials** first displays a Yes/No warning about
  unsaved changes, interrupted applications/transfers, removed drive mappings, and the inability to
  automatically restore other connections without their passwords. The warning lists the affected
  resources and asks **Do you really want to proceed?** No is the default and returns to the same
  options dialog. Yes permits forced disconnection of those reviewed connections, removes their
  remembered drive mappings, and retries the originally supplied login/password.
- **Cancel operations**, Escape, or closing the conflict window cancels recovery and closes the mapping
  editor back to the drive list. It does not perform an additional disconnection. An old mapping already
  restored after the original failed attempt remains restored.

The app rereads the reviewed list before disconnection and verifies each mapped device again before
canceling it. An incomplete/changed list prevents a bulk drop; an unknown or ambiguous active login
disables reuse rather than inventing an account. Server matching uses the exact UNC server name,
case-insensitively, without resolving aliases or disconnecting similar/unrelated names.
If a batch fails partway through, completed disconnections remain in effect and the app reports how
many occurred without retrying mapping. If connections remain or another application reopens them,
the app stops and reports that state. A recurring 1219 during a mapping retry opens the options again.

Both OK and Test Connection can enter this recovery flow. Choosing either retry action after a test
conflict attempts to map the selected drive, as it does after an OK conflict.

## Build, test, and publish

Open PowerShell in the repository on Windows. Install a .NET SDK compatible with `global.json`
(baseline SDK **9.0.203**, with later 9.0 feature bands allowed). The application targets `net8.0-windows`;
SDK and runtime versions serve different purposes. There are no third-party package dependencies.

```powershell
# Builds the solution and runs all checks.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1

# Also renders the windows and creates the app folder plus ZIP.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -RenderUi -Publish
```

The script keeps CLI state in `.dotnet-home`, disables optional workload discovery, restores its
environment changes before exiting, and fails if any build or regression check fails. UI checks require
a Windows desktop session; the forms are placed offscreen. Optional renders are saved in `artifacts\ui`.
Build artifacts and CLI state are excluded from Git.

Equivalent individual commands, after setting the workload environment variable:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build NetworkDriveRemapper.sln -c Release
dotnet run --project tests/NetworkDriveRemapper.Tests -c Release --no-build -- --render
dotnet publish src/NetworkDriveRemapper/NetworkDriveRemapper.csproj -c Release -o artifacts/publish
```

## Validation

The automated runner contains **33 regression checks** covering UNC validation, credential forwarding,
all-letter discovery/filtering, replacement order, same-path login changes, preflight/unmap/map failures,
successful and failed rollback, stale dialogs, concurrent ownership changes, reserved letters, real
read-only Windows drive discovery, dialog defaults/buttons, direct Explorer opening with an unavailable
network and incomplete credentials, new-letter account defaults, Credentials/Anonymous ordering and
field toggling, preservation of existing-mapping defaults, Cancel, both Explorer checkbox values, the main grid, and contextual
access-denied help (error detection, exact content, formatting, modal ownership, and external link targets).
Additional checks cover 1219 detection, existing-session options, server-scoped drops, incomplete/stale
lists, partial-disconnect failure, retry ownership, real read-only connection enumeration, No/Yes/Cancel
behavior, and all recovery choices through the actual mapping/testing event handlers.
Row-action checks verify icon visibility/order, the clicked drive's Explorer/editor/removal actions, keyboard
activation, Refresh, exact 2px icon margins at smaller/larger row heights, and scaling of the controls.
Painted Refresh pixels are checked for clean margins without a button frame. A stability check verifies
row reuse, selection/scroll retention, updated data, and absence of whole-grid enabled-state changes.
Removal checks cover unrelated mappings, stale/free/reserved/unknown letters, changed persistence,
open-file failures, removal of remembered settings, and the resulting icon availability.
Force-removal checks cover native/wrapped 2401 detection, unchanged error text, warning content,
Yes/No, exact force flags and drive targets, changed ownership during confirmation, unrelated errors,
and forced-retry failures without repeated prompts. Ordinary mapping replacement stays non-forced.
Mapping and Explorer operations in those tests use an in-memory adapter: no real drive
is changed. Help-link tests inject a browser opener, so no actual browser windows are launched.
Window renders were inspected for layout and readability.

Real SMB mapping/session reuse with valid server accounts, live forced disconnection, and running on
both Windows 10 and 11 require deployment
acceptance testing; those environments and test credentials were not supplied. Use a spare drive letter
and a test share to check successful login, incorrect login, replacement, reconnect after sign-in,
server-offline behavior, open-file protection, guest-access policy, and Explorer access on each OS.

## Code and function documentation

Every class and function has XML documentation or PowerShell comment-based help. Public members are
compiled with XML documentation generation and missing public documentation is a build error.
Inline comments explain native session isolation, reserved-letter detection, rollback credentials,
async UI behavior, and password handling. [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) provides a detailed
function reference, execution flow, Windows API contracts, and project responsibilities.

For a portable specification, assets, screenshots, exact text, source snapshot, acceptance scenarios,
and instructions for another LLM, begin with [AIBuildPlan/START_HERE.md](AIBuildPlan/START_HERE.md).

```text
NetworkDriveRemapper.sln
src/
  NetworkDriveRemapper/        WinForms entry point, main grid, and mapping dialog
  NetworkDriveRemapper.Core/   Validated inputs, drive snapshots, mapping policy, Win32 adapter
tests/
  NetworkDriveRemapper.Tests/  Regression runner, fake adapter, native read-only smoke check, UI renders
scripts/build.ps1             Build/test/render/publish automation
docs/DEVELOPMENT.md            Detailed implementation and function reference
assets/app-icon/               Application ICO, original PNG, and generation notes
icon_*.png                    Original icon images; the project embeds the current four assets
```

## Local Git history

The repository was initialized and an initial commit recorded **before source files were created**.
Each completed implementation change has a descriptive commit. History is also pushed to a local-only
bare remote named `local`, at `artifacts\local-git-remote.git`; no hosted repository is involved.

```powershell
git log --oneline
git status --short
git remote -v
git push local main
```

Keep the local remote directory if you want that additional local copy of Git history. Moving the
workspace together with `artifacts\local-git-remote.git` preserves its relative remote path.

## Windows API references

Implementation follows Microsoft's contracts for
[WNetAddConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w),
[WNetCancelConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetcancelconnection2w),
[WNetGetConnection](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetconnectionw),
[GetLogicalDrives](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getlogicaldrives), and
[LogonUser](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-logonuserw),
[NetUseEnum](https://learn.microsoft.com/en-us/windows/win32/api/lmuse/nf-lmuse-netuseenum),
[WNetOpenEnum](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetopenenumw), and
[WNetGetUser](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetuserw).
