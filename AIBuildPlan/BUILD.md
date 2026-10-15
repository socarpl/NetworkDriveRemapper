# Build, delivery and documentation

## Source reproduction

Initialize local Git and record an empty commit in a new empty working directory BEFORE extracting
[reference/Source-baseline.zip](reference/Source-baseline.zip) there.
The archive root is the solution root; it is not another nested AIBuildPlan folder. Preserve its relative
paths. It includes all build inputs and the original README/function documentation. It contains no .git
history or binaries. If working from the original repository, checkout the baseline commit listed in
reference/BASELINE.json instead.

Requirements:
- Windows desktop session; .NET SDK compatible with global.json, baseline 9.0.203 and latestFeature roll-forward within 9.0.
- Projects target .NET 8 Windows; framework-dependent users need the matching .NET 8 Desktop Runtime (supplied build x64).
- No third-party NuGet packages. Restore still requires the relevant SDK/reference packs to be installed.
- Do not upgrade the framework/SDK or add packages as an unreported reconstruction change.

From the extracted solution root:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -RenderUi -Publish
~~~

For an existing interactive PowerShell session, invoking & .\scripts\build.ps1 is equivalent.
The script uses workspace-local .dotnet-home, disables optional workload resolution, restores environment
variables on exit, builds Release with warnings as errors, and runs the dependency-free regression runner.

Outputs:
- artifacts/publish/NetworkDriveRemapper.exe plus its neighboring framework-dependent files/docs.
- artifacts/NetworkDriveRemapper.zip for distribution; extract the full ZIP before running.
- artifacts/ui/ optional offscreen form renders.
- None of bin/, obj/, artifacts/, .dotnet-home/ or live credentials belong in source history.

## Architecture to preserve for an independent implementation

~~~text
NetworkDriveRemapper.sln
Directory.Build.props
global.json
src/NetworkDriveRemapper.Core/
src/NetworkDriveRemapper/
tests/NetworkDriveRemapper.Tests/
scripts/build.ps1
assets/app-icon/
icon_edit2.png
icon_open.png
icon_remove.png
icon_refresh.png
README.md
docs/DEVELOPMENT.md
~~~

Keep the original namespace/project structure if reusing the reference test project. Its internal UI
seams depend on InternalsVisibleTo("NetworkDriveRemapper.Tests"). Keep the named controls/events from
the baseline, or adapt tests transparently with a documented mapping; do not weaken assertions.

Core: nullable, implicit usings, deterministic build, GenerateDocumentationFile and TreatWarningsAsErrors.
WinForms executable: UseWindowsForms, asInvoker manifest, PerMonitorV2, STA startup.
Test runner: native WinForms message loop, no external test packages, fake mapping boundary.

Set ApplicationIcon to the supplied ICO and embed the same ICO for MainForm.Icon. Embed all current
action PNGs with fixed logical names. Include assets in the solution; do not regenerate approximations.
Keep the ICO and source PNG tracked in Git, plus their generation notes.

## Git and maintenance process

For a new reconstruction workspace:
1. Initialize local Git and record the empty baseline BEFORE creating application source.
2. Commit completed coherent changes with descriptive messages and push each to a local bare remote.
3. Suggested remote: local -> artifacts/local-git-remote.git. Do not create an online remote without authorization.
4. Keep all code, solution files, current assets, build scripts, README, function documentation, tests and final specifications tracked.
5. Verify clean status after delivery.

The reference source ZIP itself contains no Git repository; initialize one only when creating a new
workspace, not while reading this documentation folder inside an existing repository.

## Function documentation

Document every class/function with C# XML comments or PowerShell comment-based help. Explain native
ownership/cleanup, credentials, async guards, rollback and confirmation mechanics in code comments.
Update README.md for users and docs/DEVELOPMENT.md for implementation/function reference. Record
known limitations and validation evidence honestly; do not claim live acceptance from fake tests.

## Evidence to deliver

Provide executable folder and ZIP, all code/assets, test result count, inspected UI renders, README and
function documentation, Git commit/remote status, and a list of remaining live-environment checks.
Do not apply destructive network actions simply to demonstrate the reconstruction.
