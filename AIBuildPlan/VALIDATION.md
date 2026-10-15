# Validation record

## Source baseline

Source commit: **4cad088acd05d4b2206042c7ee7b526868f3d6c2**.

The included source ZIP was extracted to a separate empty validation directory and its own
scripts/build.ps1 was run with -RenderUi under .NET SDK 9.0.203. Build result: **zero warnings,
zero errors; 33/33 regression checks passed**. UI renders were produced from that extracted source.
No production application source was changed to create this documentation package.

The suite uses fake mapping/disconnection/Explorer/browser effects, plus read-only native inventory
and connection metadata checks. This is not live destructive SMB acceptance or proof of execution on
both supported Windows versions. See ACCEPTANCE.md for the remaining deployment matrix.

## Package integrity checks

Validate-Package.ps1 checks all packaged file lengths and SHA-256 hashes, local Markdown links,
baseline build inputs in the source ZIP, the 33-check index, source-equal visible test copies, exact
asset copies, and all five verbatim credentials-help paragraphs.

Git line-ending conversion is disabled within this folder so the manifest remains valid after clone
or checkout. MANIFEST.json intentionally excludes itself; these hashes detect inconsistency, not
independently authenticated provenance.

The companion ZIP is a convenience copy of this folder, with AIBuildPlan as its top-level directory.
Run Validate-Package.ps1 after extraction to confirm its contents. The same validation was used for
the folder and extracted handoff ZIP during delivery.

## Screenshot evidence

Eleven screenshots are included and described in UI.md. They use fake share/device data. The new-drive
image verifies live account-prefill behavior before replacing the account text with the clearly
documented EXAMPLE-PC\SampleUser example. Do not interpret those example values as application defaults.
