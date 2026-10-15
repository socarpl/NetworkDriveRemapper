# NetworkDriveRemapper: AI build reference

Begin with [START_HERE.md](START_HERE.md). It explains the baseline, reading order, source-reproduction
and independent-reconstruction modes, package contents, and validation boundaries.

Give another LLM the entire folder or its companion ZIP and the instructions in
[REBUILD_PROMPT.md](REBUILD_PROMPT.md). The documents describe the final implemented application,
while the assets, exact strings, source snapshot, and tests make the description concrete.

This documentation package adds no production application behavior. Its source baseline is recorded in
[reference/BASELINE.json](reference/BASELINE.json), not the later commit that adds this folder.

## Maintaining this package

Treat this as a snapshot, not a live link to the parent repository. When application behavior changes,
review every specification ID/workflow, refresh the source archive, visible test copies and relevant
assets/screenshots, update baseline metadata and validation counts, and regenerate MANIFEST.json from
all files except the manifest itself. Record any newly unverified behavior explicitly. Do not blindly
relabel an old source archive as a new commit.

Run [Validate-Package.ps1](Validate-Package.ps1) after updating. Package hashes prove integrity and
agreement with the included snapshot; they are not a substitute for application acceptance testing.
