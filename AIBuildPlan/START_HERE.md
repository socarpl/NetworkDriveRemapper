# Rebuild NetworkDriveRemapper

This folder is a portable specification and reference package for recreating the Windows Forms application.
Read this file first. The final accepted application name is **NetworkDriveRemapper**.

## Baseline and authority

- Source baseline: **4cad088acd05d4b2206042c7ee7b526868f3d6c2**.
- The package describes implemented behavior at that commit, including all later decisions in our development process up to the current-account defaults.
- It is self-contained: no parent repository, conversation history, generated-image service, live share, or original computer is needed to read it.
- [reference/BASELINE.json](reference/BASELINE.json) identifies the snapshot. [MANIFEST.json](MANIFEST.json) records file sizes and SHA-256 hashes.
- [VALIDATION.md](VALIDATION.md) records the independent source rebuild, package checks, and validation limits.
- [reference/Source-baseline.zip](reference/Source-baseline.zip) contains the complete committed source, build configuration, original assets, tests, and documentation. It excludes .git, binaries, private credentials, and this new documentation folder.

For the closest reproduction, extract the source archive into an empty directory and build it unchanged using [BUILD.md](BUILD.md). Source reproduction is more reliable than asking a model to invent an equivalent implementation. Reproducible source does not imply byte-identical output across SDK versions, machine paths, fonts, and Windows versions.

For an independent reconstruction, implement the requirements below, retain the supplied assets and tests, and compare the results. If a written requirement and screenshot appear inconsistent, use the current requirement and baseline source. Screenshots illustrate native rendering; do not hard-code the example usernames or shares. Historical decisions are context, not competing requirements. If prose and the source truly disagree, report the specific mismatch instead of silently inventing behavior.

## Read in this order

1. [SPECIFICATION.md](SPECIFICATION.md): product requirements and non-negotiable defaults.
2. [UI.md](UI.md): windows, layout measurements, icon rules, keyboard behavior, screenshot provenance.
3. [BEHAVIOR.md](BEHAVIOR.md): operation sequences and error-state transitions.
4. [WINDOWS-INTEGRATION.md](WINDOWS-INTEGRATION.md): native APIs, credential semantics, ownership and cleanup.
5. [STRINGS.md](STRINGS.md): exact captions, warnings, help wording and external links.
   [ASSETS.md](ASSETS.md) identifies exact files, embedded names and destination paths.
6. [ACCEPTANCE.md](ACCEPTANCE.md): executable checks and live-environment acceptance scenarios.
7. [BUILD.md](BUILD.md): architecture, build/publish commands, Git and documentation requirements.
8. [DECISIONS.md](DECISIONS.md): development sequence and rationale, including superseded behavior.
9. [REBUILD_PROMPT.md](REBUILD_PROMPT.md): instructions to give the implementing LLM.

The copied [function reference](reference/DEVELOPMENT.md) and [application README](reference/APP-README.md) provide additional detail. Reference test sources are visible in [reference/tests/NetworkDriveRemapper.Tests](reference/tests/NetworkDriveRemapper.Tests/Program.cs); the full archive is the authority for compilation paths.

## Contents and handoff

- assets/: actual four current action PNGs and the application ICO, original PNG, and provenance.
- screenshots/: eleven baseline UI references, with a synthetic account in the new-drive illustration.
- reference/: source ZIP, visible regression-test sources, copied documentation, baseline metadata, and 33-check index.
- Validate-Package.ps1: read-only integrity checker for every packaged file.

Run the validator with Windows PowerShell:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Validate-Package.ps1
~~~

Give another LLM the entire AIBuildPlan folder or the companion AIBuildPlan.zip, then use REBUILD_PROMPT.md. Do not provide only this entry point: the actual artwork, detailed requirements, and tests are essential.

## Validation boundaries

The baseline passed 33 automated regression checks and native read-only metadata smoke checks. Destructive/mapping/Explorer/browser effects in the regression suite are simulated. Full live SMB authentication, forced disconnect, reconnect after sign-in, and acceptance on both Windows 10 and Windows 11 remain deployment checks. The package validator verifies package integrity, not live network access. Keep that distinction in any reconstruction report.
