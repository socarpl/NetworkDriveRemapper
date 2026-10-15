# Prompt for the implementing LLM

Copy the following instructions and attach the complete AIBuildPlan folder (or its ZIP):

---

Recreate NetworkDriveRemapper for Windows 10 and Windows 11 using the attached AIBuildPlan.

Start with START_HERE.md and validate the package. Read the full specification, workflows, Windows
integration, UI, strings, acceptance and build documents before changing application code. Use the
baseline commit metadata to distinguish current requirements from superseded decisions.

Default to the most faithful reproduction: initialize local Git and record an empty commit in a new
empty workspace, then extract reference/Source-baseline.zip there and build and verify the
supplied implementation. Preserve source/asset behavior. If I explicitly request an independent
implementation instead, use the specification as its contract and the supplied source/tests as
reference evidence. Tell me which mode you used; do not quietly substitute a redesigned application.

Requirements:
1. C# native WinForms, net8.0-windows, STA, PerMonitorV2, asInvoker; no unnecessary dependencies.
2. Exact application identity, assets, control labels, dialog text and icon order/margins.
3. All drive classification, filtering, default credentials, Explorer, mapping/rollback/removal
   and error-5/1219/2401 flows described in the specification.
4. Match native credential semantics, null-vs-empty values, current-logon scope and cleanup.
5. No guessed passwords, automatic elevation, silent server-wide disconnect, or automatic forced replacement.
6. Preserve grid state without whole-grid disabled flashes or rebuilding unchanged rows.
7. Read-only/fake automated checks must not modify my real mappings or launch real browsers/Explorer.
8. Retain the supplied meaningful 33 regression checks and demonstrate the acceptance scenarios.
9. Include original source PNG/ICO assets in the solution and Git. Do not regenerate or replace them.
10. Document every function, provide README.md and detailed implementation documentation, and keep
    descriptive commits pushed to a local bare remote for each completed change.
11. Deliver the published folder and ZIP, test/build evidence, inspected renders and a clear list
    of live-network/OS checks that remain unverified.

Use Windows desktop build instructions from BUILD.md. Do not claim compatibility or live SMB behavior
was tested just because fake tests passed. If you find a genuine conflict between prose and baseline
source, report the exact mismatch and use the baseline as implemented behavior pending an explicit
change request. Do not treat historical conversation/decision entries as newer requirements.

Before finishing, compare every SPECIFICATION.md ID and ACCEPTANCE.md scenario with the result.
Report failures or limitations explicitly rather than weakening the requirements.

---
