const fs = require('node:fs');
const path = require('node:path');

const releaseTag = /^build-[0-9a-f]{40}$/;
const shaPattern = /^[0-9a-f]{40}$/;
const assetName = 'NetworkDriveRemapper.zip';

// Read Git history rather than only the push tip or the size-limited commits payload.
async function selectCommits(context, exec) {
  const { before, deleted, commits } = context.payload;
  if (deleted) return [];
  let selected;
  if (context.eventName === 'push' && before && !/^0+$/.test(before)) {
    if (!shaPattern.test(before) || !shaPattern.test(context.sha)) {
      throw new Error('Invalid push commit SHA.');
    }
    const result = await exec.getExecOutput('git', [
      'rev-list', '--reverse', '--topo-order', `${before}..${context.sha}`,
    ]);
    selected = result.stdout.trim().split(/\r?\n/).filter(Boolean);
  } else {
    // A new branch has no previous tip. Build the supplied new commits, without
    // rebuilding the repository's entire historical ancestry. Manual runs build the tip.
    selected = context.eventName === 'push' && commits?.length
      ? commits.map(commit => commit.id)
      : [context.sha];
  }
  if (selected.some(sha => !shaPattern.test(sha))) throw new Error('Invalid commit SHA.');
  return [...new Set(selected)];
}

async function findRelease(github, repo, tag) {
  try {
    return (await github.rest.repos.getReleaseByTag({ ...repo, tag })).data;
  } catch (error) {
    if (error.status !== 404) throw error;
    return null;
  }
}

// This is only called after a ZIP has been uploaded and its draft published.
async function pruneReleases(github, repo, core) {
  const releases = await github.paginate(github.rest.repos.listReleases, {
    ...repo, per_page: 100,
  });
  const automatic = releases
    .filter(release => releaseTag.test(release.tag_name) && !release.draft)
    .sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at) || b.id - a.id);
  for (const release of automatic.slice(3)) {
    core.info(`Removing old release and ZIP: ${release.tag_name}`);
    await github.rest.repos.deleteRelease({ ...repo, release_id: release.id });
    try {
      await github.rest.git.deleteRef({ ...repo, ref: `tags/${release.tag_name}` });
    } catch (error) {
      if (error.status !== 404) throw error;
    }
  }
}

async function publishRelease({ github, context, core, sha, zipPath }) {
  const repo = context.repo;
  const tag = `build-${sha}`;
  let release = await findRelease(github, repo, tag);
  if (release && !release.draft) {
    if (!release.assets.some(asset => asset.name === assetName && asset.state === 'uploaded' && asset.size > 0)) {
      throw new Error(`Published release ${tag} has no complete ZIP.`);
    }
    core.info(`Already published: ${tag}`);
    await pruneReleases(github, repo, core);
    return;
  }

  const data = fs.readFileSync(zipPath);
  try {
    // Replace an unfinished draft left by an interrupted run, never a published release.
    if (release) await github.rest.repos.deleteRelease({ ...repo, release_id: release.id });
    release = (await github.rest.repos.createRelease({
      ...repo,
      tag_name: tag,
      target_commitish: sha,
      name: `Build ${sha.slice(0, 12)}`,
      body: `Compiled and tested from commit ${sha}.\n\nExtract the ZIP and run NetworkDriveRemapper.exe. Requires the .NET 8 Desktop Runtime (x64).\n\nOnly the three newest successful automatic releases are retained.`,
      draft: true,
      prerelease: false,
    })).data;
    await github.rest.repos.uploadReleaseAsset({
      ...repo,
      release_id: release.id,
      name: assetName,
      data,
      headers: { 'content-type': 'application/zip', 'content-length': data.length },
    });
    await github.rest.repos.updateRelease({
      ...repo, release_id: release.id, draft: false, make_latest: 'true',
    });
  } catch (error) {
    // A failed upload must leave the previous three successful downloads intact.
    if (release) {
      try {
        await github.rest.repos.deleteRelease({ ...repo, release_id: release.id });
      } catch (cleanupError) {
        core.warning(`Could not remove unfinished draft ${tag}: ${cleanupError.message}`);
      }
    }
    throw error;
  }
  await pruneReleases(github, repo, core);
}

async function run({ github, context, core, exec }) {
  const commits = await selectCommits(context, exec);
  const failures = [];
  for (const sha of commits) {
    core.startGroup(`Build and release ${sha}`);
    try {
      const existing = await findRelease(github, context.repo, `build-${sha}`);
      let zipPath;
      if (!existing || existing.draft) {
        // Each commit gets a clean tree so old DLLs cannot leak into later ZIPs.
        const tree = path.join(process.env.RUNNER_TEMP, `release-${sha}`);
        await exec.exec('git', ['worktree', 'add', '--detach', tree, sha]);
        await exec.exec('pwsh', [
          '-NoProfile', '-File', path.join(tree, 'scripts', 'build.ps1'), '-Publish',
        ], { cwd: tree });
        zipPath = path.join(tree, 'artifacts', assetName);
      }
      await publishRelease({ github, context, core, sha, zipPath });
    } catch (error) {
      failures.push(sha);
      core.error(`${sha}: ${error.message}`);
    } finally {
      core.endGroup();
    }
  }
  if (failures.length) core.setFailed(`Could not release ${failures.length} commit(s): ${failures.join(', ')}`);
}

module.exports = { selectCommits, pruneReleases, publishRelease, run };
