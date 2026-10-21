// Run from an isolated Temp directory containing @firebase/rules-unit-testing and firebase.
// See SETUP.md. This runner refuses non-local emulators and non-demo project IDs.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const projectId = process.env.GCLOUD_PROJECT || 'demo-catquest-settings';
const emulatorHost = process.env.FIRESTORE_EMULATOR_HOST;
if (!/^demo-[a-z0-9-]+$/.test(projectId) || !/^127\.0\.0\.1:\d+$/.test(emulatorHost || '')) {
  throw new Error('Use a demo- project and FIRESTORE_EMULATOR_HOST=127.0.0.1:<port>.');
}
const outputDirectory = path.resolve(process.env.CATQUEST_RULES_OUTPUT || 'results');
const rulesPath = fileURLToPath(new URL('./firestore.rules', import.meta.url));
const rules = await readFile(rulesPath, 'utf8');
const require = createRequire(path.join(process.cwd(), 'package.json'));
const { initializeTestEnvironment, assertSucceeds, assertFails } = require('@firebase/rules-unit-testing');
const { doc, setDoc, getDoc, deleteDoc, getDocs, collection, query, where, orderBy, limit, endBefore, getCountFromServer, serverTimestamp, Timestamp, setLogLevel } = require('firebase/firestore');
setLogLevel('silent'); // Expected permission-denied cases are reported by the assertions below.
const watchdog = setTimeout(() => { console.error('Firestore rules verification exceeded 120 seconds.'); process.exit(1); }, 120000);
watchdog.unref();
const [host, port] = emulatorHost.split(':');
const startedAt = new Date().toISOString();
const checks = [];
let testEnv;
let fixture = 0;

async function check(name, body) {
  try {
    await body();
    checks.push({ name, passed: true });
    console.log(`PASS ${name}`);
  } catch (error) {
    checks.push({ name, passed: false, error: String(error.stack || error) });
    console.error(`FAIL ${name}: ${error.message || error}`);
  }
}

function aggregate(uid) {
  return { playerId: uid, playerName: 'Cat Player', bossStage: 1,
    totalTasksCompleted: 0, totalFocusMinutes: 0, power: 0, catLevel: 1,
    updatedAt: serverTimestamp() };
}

function owner(uid) {
  return testEnv.authenticatedContext(uid).firestore();
}

async function deniedPayload(name, modify) {
  const uid = `invalid-${++fixture}`;
  const ref = doc(owner(uid), 'leaderboard', uid);
  await check(`create rejects ${name}`, async () => {
    const data = aggregate(uid);
    modify(data);
    await assertFails(setDoc(ref, data));
    assert.equal((await getDoc(ref)).exists(), false, 'Denied create left a document');
  });
  // Independently exercise update: a create-only policy must not pass these tests.
  await assertSucceeds(setDoc(ref, aggregate(uid)));
  await check(`update rejects ${name}`, async () => {
    const data = aggregate(uid);
    modify(data);
    await assertFails(setDoc(ref, data));
    const persisted = (await getDoc(ref)).data();
    assert.equal(persisted.playerName, 'Cat Player', 'Denied update changed the valid fixture');
    assert.deepEqual(Object.keys(persisted).sort(), Object.keys(aggregate(uid)).sort());
  });
}

try {
  testEnv = await initializeTestEnvironment({ projectId, firestore: { host, port: Number(port), rules } });
  await testEnv.clearFirestore();
  const uid = 'owner';
  const db = owner(uid);
  const ref = doc(db, 'leaderboard', uid);
  const foreign = doc(owner('other-player'), 'leaderboard', uid);
  const anonymous = doc(testEnv.unauthenticatedContext().firestore(), 'leaderboard', uid);

  await check('owner creates all eight exact fields with server timestamp and minimum metrics', async () => {
    await assertSucceeds(setDoc(ref, aggregate(uid)));
    const data = (await assertSucceeds(getDoc(ref))).data();
    assert.deepEqual(Object.keys(data).sort(), Object.keys(aggregate(uid)).sort());
    assert(data.updatedAt instanceof Timestamp);
    assert(Math.abs(Date.now() - data.updatedAt.toMillis()) < 30000);
  });
  await check('owner updates metrics and Unicode display name with fresh server timestamp', async () => {
    await assertSucceeds(setDoc(ref, { ...aggregate(uid), playerName: '小猫 Quest', bossStage: 6,
      totalTasksCompleted: 500, totalFocusMinutes: 1200, power: 9999, catLevel: 20 }));
    assert.equal((await getDoc(ref)).data().playerName, '小猫 Quest');
  });
  await check('valid 20-character name and int32 upper bounds accepted', async () => {
    await assertSucceeds(setDoc(ref, { ...aggregate(uid), playerName: '12345678901234567890',
      bossStage: 2147483647, power: 2147483647, catLevel: 2147483647,
      totalTasksCompleted: 2147483648, totalFocusMinutes: 2147483648 }));
  });
  await check('single Unicode character and internal Unicode spaces accepted', async () => {
    await assertSucceeds(setDoc(ref, { ...aggregate(uid), playerName: '猫' }));
    await assertSucceeds(setDoc(ref, { ...aggregate(uid), playerName: 'Cat\u2003Player' }));
  });
  await check('signed-in player may read another published aggregate', () => assertSucceeds(getDoc(foreign)));
  await check('foreign UID cannot update owner document', () => assertFails(setDoc(foreign, aggregate(uid))));
  await check('foreign UID cannot delete owner document', () => assertFails(deleteDoc(foreign)));
  await check('foreign UID cannot create another owner document', () =>
    assertFails(setDoc(doc(owner('foreign-creator'), 'leaderboard', 'absent-owner'), aggregate('absent-owner'))));
  await check('anonymous cannot read', () => assertFails(getDoc(anonymous)));
  await check('anonymous cannot update', () => assertFails(setDoc(anonymous, aggregate(uid))));
  await check('anonymous cannot delete', () => assertFails(deleteDoc(anonymous)));
  await check('anonymous cannot create', () => assertFails(setDoc(
    doc(testEnv.unauthenticatedContext().firestore(), 'leaderboard', 'anonymous'), aggregate('anonymous'))));
  await check('signed-in ranking reader may list published aggregates', () => assertSucceeds(getDocs(collection(db, 'leaderboard'))));
  await check('own snapshot query allowed', () => assertSucceeds(getDocs(query(collection(db, 'leaderboard'), where('playerId', '==', uid)))));
  await check('anonymous ranking list denied', () => assertFails(getDocs(collection(testEnv.unauthenticatedContext().firestore(), 'leaderboard'))));
  await check('other private collection read denied', () => assertFails(getDocs(collection(db, 'privateTasks'))));
  await check('other collection write denied', () => assertFails(setDoc(doc(db, 'privateTasks', uid), { title: 'Private fixture' })));
  await check('leaderboard child-document write denied', () => assertFails(setDoc(doc(db, 'leaderboard', uid, 'tasks', 'private'), { title: 'Private fixture' })));

  for (const field of Object.keys(aggregate(uid))) {
    await deniedPayload(`missing ${field}`, data => { delete data[field]; });
  }
  // Every excluded privacy category is rejected, including otherwise valid aggregate fields.
  for (const field of ['taskTitles', 'descriptions', 'schedules', 'tags', 'goals', 'responsibilities', 'aiPrompts', 'localHistory', 'unexpected']) {
    await deniedPayload(`extra/private field ${field}`, data => { data[field] = ['Private fixture']; });
  }
  await deniedPayload('foreign playerId value', data => { data.playerId = 'another-owner'; });
  await deniedPayload('non-string playerId', data => { data.playerId = 7; });
  const invalidNames = [
    ['empty display name', ''], ['21-character display name', '123456789012345678901'],
    ['non-string display name', 7], ['markup display name', '<cat>'],
    ['newline display name', 'Cat\nPlayer'], ['null control in display name', 'Cat\u0000Player'],
    ['DEL control in display name', 'Cat\u007fPlayer'], ['C1 control in display name', 'Cat\u0085Player'],
    ['whitespace-only display name', '   '], ['leading whitespace in display name', ' Cat'],
    ['trailing whitespace in display name', 'Cat '], ['Unicode whitespace-only display name', '\u00a0'],
    ['Unicode leading whitespace in display name', '\u2003Cat'], ['Unicode trailing whitespace in display name', 'Cat\u3000'],
    ['C1 upper-bound control in display name', 'Cat\u009fPlayer']
  ];
  for (const [name, value] of invalidNames) await deniedPayload(name, data => { data.playerName = value; });
  for (const field of ['bossStage', 'totalTasksCompleted', 'totalFocusMinutes', 'power', 'catLevel']) {
    for (const [name, value] of [['negative', -1], ['fractional', 1.5], ['string', '1'], ['boolean', true], ['null', null]]) {
      await deniedPayload(`${name} ${field}`, data => { data[field] = value; });
    }
  }
  for (const field of ['bossStage', 'catLevel']) await deniedPayload(`zero ${field}`, data => { data[field] = 0; });
  for (const field of ['bossStage', 'power', 'catLevel']) await deniedPayload(`overflowing int32 ${field}`, data => { data[field] = 2147483648; });
  await deniedPayload('client-chosen timestamp', data => { data.updatedAt = Timestamp.fromMillis(946684800000); });
  await deniedPayload('future timestamp', data => { data.updatedAt = Timestamp.fromMillis(4102444800000); });
  await deniedPayload('string timestamp', data => { data.updatedAt = '2026-01-01T00:00:00Z'; });
  await deniedPayload('null timestamp', data => { data.updatedAt = null; });
  await check('owner deletes document', async () => {
    await assertSucceeds(deleteDoc(ref));
    assert.equal((await getDoc(ref)).exists(), false);
  });
  await check('owner delete of already absent document is harmless', () => assertSucceeds(deleteDoc(ref)));
  await testEnv.clearFirestore();
  const players = Array.from({ length: 85 }, (_, i) => ({ ...aggregate(`rank-${String(i).padStart(3, '0')}`),
    bossStage: 1 + i % 7, totalTasksCompleted: (i * 13) % 100, totalFocusMinutes: 84 - i,
    power: (i % 5) * 100, catLevel: 1 + i % 9 }));
  for (const player of players) await setDoc(doc(owner(player.playerId), 'leaderboard', player.playerId), player);
  for (const metric of ['bossStage', 'totalTasksCompleted', 'totalFocusMinutes', 'power']) {
    const orders = [orderBy(metric, 'desc'), ...(metric === 'power' ? [orderBy('catLevel', 'desc')] : []), orderBy('playerId', 'asc')];
    const expected = [...players].sort((a,b) => b[metric]-a[metric] || (metric === 'power' ? b.catLevel-a.catLevel : 0) || a.playerId.localeCompare(b.playerId));
    await check(`${metric}: independent top 50 and deterministic ties`, async () => {
      const ranked = await assertSucceeds(getDocs(query(collection(db, 'leaderboard'), ...orders, limit(50))));
      assert.deepEqual(ranked.docs.map(d => d.id), expected.slice(0,50).map(d => d.playerId));
    });
    for (const position of [0, 1, 49, 50, 73, 84]) {
      await check(`${metric}: exact own rank ${position+1}, including outside top 50`, async () => {
        const player = expected[position];
        const cursor = [player[metric], ...(metric === 'power' ? [player.catLevel] : []), player.playerId];
        const count = await assertSucceeds(getCountFromServer(query(collection(db, 'leaderboard'), ...orders, endBefore(...cursor))));
        assert.equal(count.data().count + 1, position + 1);
      });
    }
  }
  // Optionally exercise the exact JSON emitted by the compiled C# reader.
  if (process.env.CATQUEST_QUERY_CONTRACTS) {
    const contracts = JSON.parse(await readFile(process.env.CATQUEST_QUERY_CONTRACTS, 'utf8')).items;
    const post = async (count, body) => {
      const response = await fetch(`http://${emulatorHost}/v1/projects/${projectId}/databases/(default)/documents:${count ? 'runAggregationQuery' : 'runQuery'}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer owner' },
        body: JSON.stringify(body), signal: AbortSignal.timeout(10000)
      });
      assert(response.ok, `REST ${response.status}: ${await response.clone().text()}`);
      return response.json();
    };
    for (const contract of contracts) await check(`${contract.kind}: compiled REST contract and shared readTime`, async () => {
      const topBody = JSON.parse(contract.top);
      const metric = topBody.structuredQuery.orderBy[0].field.fieldPath;
      const expected = [...players].sort((a,b) => b[metric]-a[metric] || (metric === 'power' ? b.catLevel-a.catLevel : 0) || a.playerId.localeCompare(b.playerId));
      const top = await post(false, topBody);
      assert.deepEqual(top.filter(r => r.document).map(r => r.document.fields.playerId.stringValue), expected.slice(0,50).map(p => p.playerId));
      const readTime = top.at(-1).readTime;
      assert(readTime);
      const player = expected[73];
      const ownBody = JSON.parse(contract.own);
      ownBody.readTime = readTime;
      ownBody.structuredQuery.where.fieldFilter.value.stringValue = player.playerId;
      const own = await post(false, ownBody);
      assert.equal(own[0].document.fields.playerId.stringValue, player.playerId);
      const rankBody = JSON.parse(contract.rank);
      rankBody.readTime = readTime;
      rankBody.structuredAggregationQuery.structuredQuery.endAt.values = [
        { integerValue: String(player[metric]) }, ...(metric === 'power' ? [{ integerValue: String(player.catLevel) }] : []), { stringValue: player.playerId }
      ];
      const rank = await post(true, rankBody);
      assert.equal(Number(rank[0].result.aggregateFields.ahead.integerValue) + 1, 74);
    });
  }
} catch (error) {
  checks.push({ name: 'runner setup or fixture failure', passed: false, error: String(error.stack || error) });
  console.error(error);
} finally {
  await mkdir(outputDirectory, { recursive: true });
  if (testEnv) {
    for (const [endpoint, filename] of [['ruleCoverage', 'rule-coverage.json'], ['ruleCoverage.html', 'rule-coverage.html']]) {
      try {
        const response = await fetch(`http://${emulatorHost}/emulator/v1/projects/${projectId}:${endpoint}`, { signal: AbortSignal.timeout(10000) });
        assert(response.ok, `Coverage endpoint returned ${response.status}`);
        await writeFile(path.join(outputDirectory, filename), await response.text());
      } catch (error) {
        checks.push({ name: `capture ${endpoint}`, passed: false, error: String(error) });
      }
    }
    await testEnv.cleanup();
  }
  const report = { startedAt, finishedAt: new Date().toISOString(), projectId, emulatorHost,
    rulesPath, rulesSha256: createHash('sha256').update(rules).digest('hex'),
    checks: checks.length, passed: checks.filter(check => check.passed).length,
    failed: checks.filter(check => !check.passed).length, results: checks };
  await writeFile(path.join(outputDirectory, 'results.json'), JSON.stringify(report, null, 2));
  console.log(`Firestore rules: ${report.passed}/${report.checks} passed; ${report.failed} failed. Results: ${outputDirectory}`);
  clearTimeout(watchdog);
  process.exitCode = report.failed ? 1 : 0;
}
