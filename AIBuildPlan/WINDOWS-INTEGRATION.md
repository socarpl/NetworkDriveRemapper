# Windows integration and implementation contracts

The baseline source in reference/Source-baseline.zip is the authority for interop declarations, struct
layout, buffer limits, and exact method implementations. The copied [function reference](reference/DEVELOPMENT.md)
describes every function. This document records the contracts an independent reconstruction must preserve.

## Layers and models

| Layer/model | Responsibility |
| --- | --- |
| NetworkDriveRemapper.Core | Input validation, immutable inventory/connection snapshots, mapping/removal/recovery policy, Windows adapter and Unicode P/Invoke. |
| NetworkDriveRemapper | Native WinForms, owned dialogs, resource loading, icon drawing, asynchronous event handlers. |
| INetworkDriveSystem | Injectable boundary used by DriveMappingService; UI never calls native imports directly. |
| DriveEntry | Letter, Free/Network/Occupied kind, description, optional NetworkPath, IsPersistent. DeviceName is X:. CanMap requires Free or Network with a known path. No credentials in snapshots. |
| ConnectionOptions | Normalized path, named-vs-anonymous choice, username, password and explicit UseExistingLogin flag, transient only. ToString returns path without secrets. |
| NetworkConnection | Optional local device, remote path, optional login, readable status, optional reference count. Identity combines local/path/login. |
| ServerConnectionReport | Target path, resources, warnings; exact server extraction; one distinct nonblank reported account among Connected entries permits reuse; a nonempty warning-free list permits reviewed disconnection. Unknown account fields are not invented. |

## Native operations

| API | Required use |
| --- | --- |
| GetLogicalDrives | Read A-Z occupancy bitmask. Failure to enumerate is an error, not an empty inventory. |
| GetDriveTypeW | Classify local/removable/CD/RAM/network roots without listing share contents. |
| QueryDosDeviceW | Protect DOS reservations/SUBST devices missing from ordinary logical-drive enumeration. |
| WNetGetConnectionW | Read current UNC; retry required buffer size on ERROR_MORE_DATA. Keep useful returned path for disconnected remembered resources. |
| HKCU\Network\{letter}\RemotePath | Read-only fallback for remembered mappings/persistence. A real local volume overrides stale remembered registry data. Do not edit registry keys directly for mapping removal. |
| WNetAddConnection2W | Map exact letter/UNC, or create temporary deviceless share connection in isolated testing. NETRESOURCEW disk type is 1. |
| WNetCancelConnection2W | Device-only normal unmap with profile update and force=false; scoped explicit confirmed force rules below. |
| WNetGetLastErrorW | Retrieve provider explanation on native error 1208. |
| LogonUserW | Type 9 (NEW_CREDENTIALS), provider 3, owning SafeAccessTokenHandle for ordinary isolated tests. Network access afterward validates the supplied credentials. |
| WindowsIdentity.RunImpersonated | Run temporary share connection and directory verification in that new outbound logon, then release resources. |
| NetUseEnum / NetApiBufferFree | Paginated USE_INFO_2 current-user SMB metadata. Never dereference/copy the native password pointer. Free each native buffer in finally. |
| WNetOpenEnumW / WNetEnumResourceW / WNetCloseEnum | Supplement connected provider resources, including deviceless resources; grow buffers safely, close enumeration handle in finally. |
| WNetGetUserW | Report the account associated with a device or UNC resource; resize too-small buffers; return unknown when Windows cannot report it. No password retrieval. |

All interop string calls are Unicode. Keep Win32 BOOL marshaling, DWORD widths and sequential NETRESOURCEW /
USE_INFO_2 layouts correct for process architecture. WNet return values are direct Windows error codes,
not HRESULTs or unconditional Marshal.GetLastWin32Error. LogonUser/GetLogicalDrives use their own
BOOL/last-error contracts. Free every handle/buffer/enumerator even after failure.

Constants: success=0, CONNECT_UPDATE_PROFILE=1, CONNECT_TEMPORARY=4, RESOURCE_CONNECTED=1,
ERROR_MORE_DATA=234, ERROR_NO_MORE_ITEMS=259, ERROR_NOT_CONNECTED=2250,
ERROR_CONNECTION_UNAVAIL=1201. See NativeMethods.cs in the source snapshot for every declaration.

## Discovery precedence and status

For each A-Z entry:
1. If a known local device type exists logically or as DOS device, mark Occupied and describe type.
   A DOS target beginning \??\ is described as Virtual / SUBST Drive.
2. Otherwise a known WNet or remembered UNC becomes Network; record persistence and append
   (Disconnected) whenever the WNet lookup was not successful.
3. Known logical/DOS/network reservations without a target are Occupied with readable status.
4. Unexpected lookup failures are Unavailable, never proof of availability.
5. Only absence established by the expected native not-found/not-connected results is Free.

Labels are listed in STRINGS.md. No network directory enumeration is needed for the main grid.

## Credential semantics

| Mode | Managed values / native mapping | Directory test |
| --- | --- | --- |
| Anonymous | Empty username and empty password, explicitly. Do not silently substitute null/current credentials. | New outbound logon; deviceless temporary WNet connection with the empty values; directory check. |
| Credentials | Trimmed nonblank username; password whitespace preserved, blank allowed; native values passed as provided. | Split DOMAIN\user for LogonUser; keep UPN/unqualified forms; isolated outbound logon. |
| Existing-login recovery | Reported account; UseExistingLogin=true; visible password cleared and disabled; WNet password is NULL, not empty string. | Reread reported login, then directory-check in the existing desktop session without creating an isolated test logon. |

WindowsIdentity.GetCurrent().Name supplies the free-letter prefill and is disposed after reading.
A local computer/account authority is different from a network workgroup. Prefill does not obtain a
password, change policies, validate that the account exists on the server, or imply session reuse.

Temporary tests connect only the UNC share root, then verify the complete requested directory.
Use File.GetAttributes and begin Directory.EnumerateFileSystemEntries, disposing the enumerator.
Directory.Exists alone masks access errors and is insufficient. Close the temporary connection in
finally, without force; token disposal must always follow.

## Mapping, persistence and restoration

Normal Map uses profile update and the requested options. Unmap removes the letter's profile through
the Windows API. Restore uses old UNC, old persistence, NULL username and NULL password; do not reuse
the new target's credentials. Native errors must report whether old mapping restoration succeeded.
The app does not request password storage in Credential Manager and does not serialize credentials.
Windows may prompt again at a later sign-in.

## Error classification

- Access-denied help: native code 5 or UnauthorizedAccessException, walking InnerException.
- Credential conflict: native 1219 or HRESULT 0x800704C3, walking wrappers.
- Single-letter force offer: Win32Exception.NativeErrorCode==2401 only, walking wrappers.
- Do not match English messages or treat 2404 as 2401.
- Native message template: {action} failed (Windows error {code}): {OS/provider explanation}{hint}.
  OS text can be localized; preserve original exception text in review/confirmation dialogs.

## Force scope

Normal tests, replacements and initial removals pass force=false. Only:
1. A Yes after a single-letter native-2401 warning permits one force=true retry for that same, freshly checked mapping.
2. A Yes after the 1219 server warning permits the reviewed server batch, with identity/list/target rechecks.

Do not require elevation or invoke administrative SMB PowerShell cmdlets for this workflow.
Connection metadata is limited to what the current user's APIs can report. Discovery failures become
warnings; they must not be silently ignored to enable an unsafe bulk drop.

Merge resources by local name and trimmed remote path, retaining account/reference information and
Connected status when available. Sort display by remote then local name. Compare UNC server names
case-insensitively and ignore a trailing DNS dot; do not resolve aliases or widen scope to similar names.

## UI threading, Explorer and resource ownership

WinForms stays on STA; native work runs under Task.Run and continuations return through the UI context.
Busy controls cannot be closed midway through replacement. Grid actions are guarded without disabling
the grid, and image resources are disposed after their controls.

Explorer launches through ProcessStartInfo("explorer.exe"), UseShellExecute=false, one ArgumentList
entry. OpenDrive supplies X:\; dialog OpenLocation supplies raw textbox text. No password or arbitrary
shell command is assembled. External documentation URLs use UseShellExecute=true for the default
browser; fixed HTTPS URLs live in STRINGS.md.

## Primary API references

These links are background references recorded from the implementation; following them is not required
to build from the source snapshot.

- [WNetAddConnection2W](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w)
- [WNetCancelConnection2W](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetcancelconnection2w)
- [WNetGetConnectionW](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetconnectionw)
- [LogonUserW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-logonuserw)
- [NetUseEnum](https://learn.microsoft.com/en-us/windows/win32/api/lmuse/nf-lmuse-netuseenum)
- [WNetOpenEnumW](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetopenenumw)
- [WNetGetUserW](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetgetuserw)
