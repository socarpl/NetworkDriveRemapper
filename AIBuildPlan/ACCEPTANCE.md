# Acceptance and evidence

## Existing executable suite

The baseline contains **33 named checks**. The full index is in
[reference/acceptance-test-index.json](reference/acceptance-test-index.json). Visible sources:
[Program.cs](reference/tests/NetworkDriveRemapper.Tests/Program.cs),
[FakeNetworkDriveSystem.cs](reference/tests/NetworkDriveRemapper.Tests/FakeNetworkDriveSystem.cs),
[test project](reference/tests/NetworkDriveRemapper.Tests/NetworkDriveRemapper.Tests.csproj).

These three files are copies for reading/reuse, not a standalone build tree in this documentation
folder. Their relative ProjectReference paths assume the reconstructed solution layout. Use the
complete source archive or place them under tests/NetworkDriveRemapper.Tests in the new solution.

Run from the extracted/rebuilt solution root:

~~~powershell
& .\scripts\build.ps1 -RenderUi -Publish
~~~

Acceptance requires zero warnings/errors, 33/33 passing baseline checks, successful publish, and visual
comparison with UI.md. Keep meaningful original tests; passing a reduced replacement suite is not equivalent.

## Scenario matrix

| Scenario | Expected result / evidence |
| --- | --- |
| A-Z inventory | Ordered 26 letters; assigned local/network letters never reported free. |
| Filter | Initially off; on hides both occupied and mapped entries, including remembered mappings. |
| Local/unknown letters | No editor/removal action; no native mutations if service called with invalid snapshot. |
| Free-letter editor | Empty target, Credentials first/selected, actual qualified Windows identity, blank masked password, enabled fields, open-after checkbox true. |
| Mapped-letter editor | Target prefilled, Anonymous initially selected, blank disabled fields; Credentials still visually first. |
| Radio toggling | Exactly one mode; Anonymous disables fields and ignores their values; returning Credentials retains typed/prefilled text. |
| Ordinary Test | Exact normalized path/account/password forwarded; no desktop letter change; success for accessible empty directory. |
| Direct Explorer | Current raw textbox text passed unchanged even for local paths, unreachable UNC and missing username; no test or credential validation. |
| Cancel | No new map/unmap; editor closes; password cleared. |
| OK free | Uses ordinary named default account with blank password until edited; persistent Map; optional Explorer checkbox honored both ways. |
| Replacement | read -> test -> read -> unmap(false) -> map; same-path login changes still replace. |
| Failed preflight/unmap | Previous mapping remains; no subsequent Map/Restore after unmap failure. |
| Failed mapping | Old mapping restored when possible; both outcomes/errors reported if rollback fails; no invented recovery on formerly free letter. |
| Concurrent changes | Changed kind/path/persistence or arrival of local volume stops before mutation; retry cannot overwrite unrelated owner. |
| Row actions | Correct clicked letter, not previous selection; mapped order OPEN/REMOVE/EDIT, free only EDIT, occupied none; Enter/Space behavior. |
| Icon geometry | Each current icon shrinks/grows with bounds, fixed 2px physical margins at 16/48px rows and 1.5x controls; Refresh no frame. |
| Grid stability | OPEN does not reread inventory; editor-return/Refresh/removal reuse rows, selected cell, heights and scroll index; no grid EnabledChanged flashes. |
| Normal REMOVE | Only one matching letter removed; persistence removed; unrelated mapping retained; same row becomes Available with only EDIT. |
| Removal error 2401, No | One non-forced attempt, one warning preserving original error, no forced retry, return to usable main window. |
| Removal error 2401, Yes | Attempts (same letter,false), then (same letter,true); scoped warning; successful row update. |
| Ownership changes during warning | No forced call; show changed-owner error; retain new mapping. |
| Forced retry fails | One ordinary failure; no second force prompt, even for recurring 2401. |
| Other removal errors | 5, 2404 and message-only 2401 do not offer force. |
| Access-denied help | Typed/wrapped code 5 or UnauthorizedAccessException reveals Read more...; unrelated errors do not; stale input/actions clear it. |
| Help modal | Exact five paragraphs/emphasis/three URLs; owner disabled by native modality; external opener used; Close returns to editor. |
| 1219 recognition | Native/wrapped/HRESULT accepted, text-only match rejected; ordinary message box initially suppressed. |
| 1219 review | Original error, server/accounts/resources and discovery limits shown; three choices fit at minimum size. |
| Existing login | Reported account replaces input, password cleared/disabled, existing-session mode uses NULL password and desktop test; no password recovery. |
| Server drop No | Same options modal remains open, no disconnect. |
| Server drop Yes | Only reviewed exact server/current-logon connections dropped after unchanged complete listing; retry original credentials. |
| Cancel recovery | Closes recovery AND mapping editor back to main; no new disconnect. |
| Incomplete/changed lists | Disable or reject bulk drop; reject unknown/ambiguous reusable identity rather than invent one. |
| Partial/reopened connections | Report partial progress/remaining resources; do not map automatically. |
| Application icon | All seven native executable icon resources match supplied ICO; main title/taskbar use embedded same artwork. |

The 33-check suite groups several scenarios per test. The application-icon comparison was an additional
manual resource verification at icon integration, not a claim of a 34th named test.

## What is automated and what is not

Mapping, removal, force flags, Explorer, browser, and recovery mutations in tests use fake adapters or
injected callbacks. Native smoke checks read inventory/current-logon connection metadata only.
Offscreen forms run under a real root Application.Run loop; do not replace it with an unbounded
DoEvents loop. PumpUntil is bounded test support, not a production async pattern.

The package itself was checked for file integrity, source/test/asset agreement, links and archive
contents. Documentation-only packaging does not require another destructive network test.

## Live deployment acceptance (not yet demonstrated by fake tests)

Use explicitly authorized disposable shares/accounts and spare letters on BOTH Windows 10 and 11:
- Fresh correct/incorrect named credentials, anonymous policy allowed/denied, UPN and local/domain accounts.
- Existing desktop sessions, real 1219, existing-login reuse, deviceless resources and alias behavior.
- Persistent reconnect after sign-in and real Explorer authentication.
- Real open-file 2401, No/Yes behavior, and force failure where reproducible; save all work first.
- Real scoped bulk drop, incomplete-provider results, concurrently reopening connections and failed restoration.
- 100/150/200% DPI, keyboard navigation, small-screen scrolling, taskbar/title/Explorer icon rendering.

Record OS/runtime/SDK, account context, requested action and observed outcome. Do not imply that a
successful directory test proves the named password was accepted as that account: Guest mapping may
explain access. No app change should weaken warnings or silently force a disconnect to make tests pass.
