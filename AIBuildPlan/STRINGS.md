# Exact text and localization contract

The baseline source archive contains all control strings and native error wrappers. This file isolates
the text that must not be paraphrased, especially the previously requested verbatim help explanation.
Native OS/provider explanations vary by Windows language; retain them rather than matching their text.
Braced values below are placeholders, not literal user-facing braces.

## Main and mapping captions

| Purpose | Exact caption/text |
| --- | --- |
| App title/heading | NetworkDriveRemapper |
| Instruction | Double-click an available letter or network drive to set its mapping. |
| Filter | Show only available |
| Column headers | Drive ; Network location / status |
| Icon accessibility values | OPEN ; REMOVE ; EDIT |
| Refresh tooltip/name | Refresh drive letters (F5) |
| Editor title | Map network drive — {deviceName} |
| Editor heading | Network mapping for {deviceName} |
| Target label | Target path |
| Free-letter note | This drive letter is available. |
| Mapped-letter note | OK replaces the current network mapping. |
| Login group | Type of login |
| Login order | Credentials ; Anonymous |
| Credential labels | Username ; Password |
| Checkbox | Open directory after mapping |
| Button order | Test connection ; Open in Explorer ; Cancel ; OK |
| Help link | Read more... |
| Help title/heading | Anonymous access and credentials |
| Help button | Close |

Bottom note, two lines:

~~~text
Open in Explorer opens the entered path directly; Explorer handles access and login.
Anonymous access depends on the server and Windows guest-access policy.
~~~

Success feedback:
- Connection succeeded. The directory can be opened and listed.
- The location was sent to Explorer. No drive letter was changed.

Drive labels:
- Available
- Taken - Hard Drive
- Taken - Removable Drive
- Taken - CD/DVD Drive
- Taken - RAM Disk
- Taken - Virtual / SUBST Drive
- Taken - Reserved Device
- Taken - Network Drive (target unavailable)
- Unavailable - Windows could not verify this letter
- Known UNC descriptions append a space and (Disconnected) when unavailable.

## Credentials-help text, preserved verbatim

There are five paragraphs. Apply the exact bold spans shown below, and code styling to the word null
in the second paragraph. Each source link is a separate native LinkLabel directly after its paragraph.

**“Everyone” permissions do not automatically allow anonymous access.** Windows normally excludes anonymous connections from the Everyone group. A share can therefore permit Everyone while still requiring a login or Guest identity.

[Microsoft documentation](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-let-everyone-permissions-apply-to-anonymous-users)

In this app, **Anonymous currently sends an explicitly empty username and password** to the Windows connection API. That differs from letting Windows use your current account: default credentials require null values. The “Anonymous” label therefore simplifies several distinct Windows authentication behaviors.

[Windows API documentation](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w)

**Arbitrary credentials succeeding suggests your server may be treating those connections as Guest.** Windows has a “Guest only” sharing policy that maps local-account network logins to Guest. That can explain why a named login succeeds while an empty-credential attempt fails—but it remains a possible explanation until we see the error and server settings.

[Guest-only policy documentation](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/network-access-sharing-and-security-model-for-local-accounts)

The test actually connects and attempts to list the directory. Success means the resulting session can access that directory; it does **not necessarily establish that the entered username/password were accepted as that named account**.

What exact Windows error number/message appears when you test Anonymous? That will help distinguish an empty-username rejection from an anonymous-access or Guest-policy restriction. No files were changed.

The final question/sentence is intentionally part of the historical text requested verbatim. Do not
remove it because it sounds conversational. Exact Unicode punctuation is preserved in the source
and reference test. The text does not claim the current free-letter default is Anonymous.

## Error 1219 review

Title: Network login conflict — Windows error 1219

Prompt: Choose how you would like to continue

Buttons in order and exact casing:
1. use existing login instead
2. drop all connections and retry with provided credentials
3. Cancel operations

Connection grid: Drive ; Server / share ; Login in use ; Status ; Open references.
Missing device is an em dash; missing cell metadata is Not reported. Unknown account summary is
Not reported by Windows. Preserve the original error verbatim in the scrolling field.

Server-drop warning title: Confirm disconnecting server connections.
Buttons Yes/No; warning icon; default No. Body template:

~~~text
All listed connections to \\{server} in your current Windows logon will be forcibly disconnected.

Unsaved changes may be lost. Open documents, running applications, file transfers, and background tasks using these shares may fail or be interrupted. Save your work and close files before continuing.

Listed drive mappings, including their remembered mappings, will be removed. Disconnected connections cannot be automatically restored because Windows does not return their passwords. Another application may reconnect afterward. Connections to other named servers and other users' logon sessions are outside this action.

Affected connections:
{affected}

Do you really want to proceed?
~~~

Each affected row is:
~~~text
• {device-or-No drive letter}: {UNC} — {login-or-Login not reported}
~~~

No leaves the review open. Only Yes chooses the disconnect-and-retry resolution.

## Error 2401 force-removal prompt

Title: Unable to remove mapping. Buttons Yes/No; warning icon; No selected by default.
Use the complete original exception message as the first block, then:

~~~text
{originalError}

Force disconnect {deviceName} mapped to {UNC}?

Forcing disconnection can interrupt applications, file transfers, and pending requests. Unsaved changes or pending writes may be lost. Save your work and close files first if possible.

Do you want to force disconnect this drive?
~~~

An ordinary removal error or failed forced retry uses the same title with OK and error icon, displaying
that failure's original message. Do not repeat the force question on a failed retry.

## Native error hints

CreateError preserves code and localized OS/provider explanation, then appends:
- 1219: Windows already has a connection to this server with another login. Close its files and disconnect that conflicting connection yourself, then retry.
- 5, 86, 1326: Check the login and share permissions. Anonymous access requires a server and Windows policy that permit guest access.
- 53, 67: Check the \\server\share path, network connectivity, and VPN.
- 2401, 2404: Close files and Explorer windows using this drive, then retry.

These hints do not override recovery classification: 1219 enters the specialized review, and only 2401
offers single-letter force. Preserve original messages in those dialogs, including hints. Full validation,
rollback, discovery and retry-error strings remain in the baseline source; do not replace actionable
failure details with a generic failure message.
