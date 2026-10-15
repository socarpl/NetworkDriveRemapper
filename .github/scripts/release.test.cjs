const assert = require('node:assert/strict');
const { selectCommits, pruneReleases, publishRelease, run } = require('./release.cjs');

// Keep the checks runnable with plain Node.js, including older local installations.
const checks = [];
const test = (name, run) => checks.push({ name, run });

const sha = n => n.toString(16).padStart(40, '0');
const repo = { owner: 'example', repo: 'app' };
const context = { repo, sha: sha(5), eventName: 'push', payload: { before: sha(2) } };
const release = (n, extra = {}) => ({
  id: n, tag_name: `build-${sha(n)}`, draft: false,
  published_at: `2026-10-${String(n).padStart(2, '0')}T12:00:00Z`,
  assets: [{ name: 'NetworkDriveRemapper.zip', state: 'uploaded', size: 100 }], ...extra,
});

function fixture(releases = [], failUpload = false) {
  const calls = [];
  const core = Object.fromEntries(['info', 'warning', 'error', 'setFailed', 'startGroup', 'endGroup']
    .map(method => [method, message => calls.push([method, message])]));
  const github = {
    paginate: async (method, args) => {
      assert.equal(args.per_page, 100);
      return method(args);
    },
    rest: {
      repos: {
        listReleases: async () => releases,
        getReleaseByTag: async ({ tag }) => {
          const found = releases.find(item => item.tag_name === tag);
          if (!found) throw Object.assign(new Error('not found'), { status: 404 });
          return { data: found };
        },
        createRelease: async args => {
          calls.push(['create', args]);
          const item = release(5, { draft: true, tag_name: args.tag_name });
          releases.push(item);
          return { data: item };
        },
        uploadReleaseAsset: async args => {
          calls.push(['upload', args]);
          if (failUpload) throw new Error('upload failed');
        },
        updateRelease: async args => {
          calls.push(['publish', args]);
          releases.find(item => item.id === args.release_id).draft = false;
        },
        deleteRelease: async args => {
          calls.push(['delete', args.release_id]);
          const index = releases.findIndex(item => item.id === args.release_id);
          if (index !== -1) releases.splice(index, 1);
        },
      },
      git: { deleteRef: async args => calls.push(['deleteTag', args.ref]) },
    },
  };
  return { github, core, calls, context, sha: sha(5), zipPath: __filename };
}

test('selects every commit in a multi-commit push in Git order', async () => {
  const exec = { getExecOutput: async (command, args) => {
    assert.equal(command, 'git');
    assert.deepEqual(args, ['rev-list', '--reverse', '--topo-order', `${sha(2)}..${sha(5)}`]);
    return { stdout: `${sha(3)}\r\n${sha(4)}\r\n${sha(5)}\r\n` };
  } };
  assert.deepEqual(await selectCommits(context, exec), [sha(3), sha(4), sha(5)]);
});

test('new branches use new commits and manual runs build only the selected tip', async () => {
  const payload = { before: '0'.repeat(40), commits: [{ id: sha(4) }, { id: sha(5) }] };
  assert.deepEqual(await selectCommits({ ...context, payload }, {}), [sha(4), sha(5)]);
  assert.deepEqual(await selectCommits({ ...context, eventName: 'workflow_dispatch', payload: {} }, {}), [sha(5)]);
  assert.deepEqual(await selectCommits({ ...context, payload: { deleted: true } }, {}), []);
});

test('rejects malformed SHAs before invoking Git', async () => {
  await assert.rejects(selectCommits({ ...context, sha: '--bad' }, {}), /Invalid push/);
});

test('keeps newest three regardless of API order and deletes matching release tags', async () => {
  const f = fixture([release(2), release(5), release(1), release(4), release(3)]);
  await pruneReleases(f.github, repo, f.core);
  assert.deepEqual(f.calls.filter(call => call[0] === 'delete'), [['delete', 2], ['delete', 1]]);
  assert.deepEqual(f.calls.filter(call => call[0] === 'deleteTag'), [
    ['deleteTag', `tags/build-${sha(2)}`], ['deleteTag', `tags/build-${sha(1)}`],
  ]);
});

test('preserves manual releases, unrelated tags, and drafts during retention', async () => {
  const f = fixture([
    release(1, { tag_name: 'v1.0' }), release(2, { tag_name: 'build-manual' }),
    release(3, { draft: true }), release(4), release(5), release(6),
  ]);
  await pruneReleases(f.github, repo, f.core);
  assert.equal(f.calls.length, 0);
});

test('uploads and publishes successfully before deleting an older download', async () => {
  const f = fixture([release(1), release(2), release(3)]);
  await publishRelease(f);
  const operations = f.calls.filter(call => ['create', 'upload', 'publish', 'delete'].includes(call[0]));
  assert.deepEqual(operations.map(call => call[0]), ['create', 'upload', 'publish', 'delete']);
  assert.equal(operations[0][1].draft, true);
  assert.equal(operations[0][1].target_commitish, sha(5));
  assert.equal(operations[2][1].make_latest, 'true');
  assert.equal(operations[3][1], 1);
});

test('upload failure removes only its draft and preserves all previous releases', async () => {
  const f = fixture([release(1), release(2), release(3)], true);
  await assert.rejects(publishRelease(f), /upload failed/);
  assert.deepEqual(f.calls.filter(call => call[0] === 'delete'), [['delete', 5]]);
  assert.equal(f.calls.some(call => call[0] === 'deleteTag'), false);
});

test('reruns reuse published ZIPs without publishing duplicates', async () => {
  const f = fixture([release(1), release(2), release(3), release(5)]);
  await publishRelease({ ...f, zipPath: undefined });
  assert.equal(f.calls.some(call => ['create', 'upload', 'publish'].includes(call[0])), false);
  assert.deepEqual(f.calls.filter(call => call[0] === 'delete'), [['delete', 1]]);
});

test('missing ZIP on a published release fails without deleting previous downloads', async () => {
  const f = fixture([release(1), release(2), release(5, { assets: [] })]);
  await assert.rejects(publishRelease(f), /no complete ZIP/);
  assert.equal(f.calls.length, 0);
});

test('a failed build still attempts subsequent commits and reports failure', async () => {
  const f = fixture([release(5)]);
  const previousTemp = process.env.RUNNER_TEMP;
  process.env.RUNNER_TEMP = process.cwd();
  try {
    await run({ ...f, exec: {
      getExecOutput: async () => ({ stdout: `${sha(4)}\n${sha(5)}\n` }),
      exec: async () => { throw new Error('build failed'); },
    } });
  } finally {
    if (previousTemp === undefined) delete process.env.RUNNER_TEMP;
    else process.env.RUNNER_TEMP = previousTemp;
  }
  assert.ok(f.calls.some(call => call[0] === 'info' && call[1].includes('Already published')));
  const failure = f.calls.find(call => call[0] === 'setFailed');
  assert.ok(failure[1].includes(sha(4)));
  assert.equal(failure[1].includes(sha(5)), false);
});

(async () => {
  for (const check of checks) {
    try {
      await check.run();
      console.log(`PASS ${check.name}`);
    } catch (error) {
      console.error(`FAIL ${check.name}`, error);
      process.exitCode = 1;
    }
  }
})();
