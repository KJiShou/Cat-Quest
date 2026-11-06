// Run with Node.js. Tests the actual Unity JavaScript bridge with isolated storage.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const library = {};
let value = null, blocked = false, full = false;
const context = {
  LibraryManager: { library }, mergeInto: Object.assign,
  UTF8ToString: value => value, stringToNewUTF8: value => value,
  localStorage: {
    getItem() { if (blocked) throw new Error('SecurityError'); return value; },
    setItem(key, next) { if (blocked || full) throw new Error('QuotaExceededError'); value = next; }
  }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../Assets/Plugins/WebGL/CatQuestBrowser.jslib'), 'utf8'), context);
assert.equal(JSON.parse(library.CatQuest_ReadSave()).value, '');
const first = JSON.stringify({ format: 1, primary: '{"version":5,"username":"测试猫"}', backup: null });
assert.equal(library.CatQuest_WriteSave('', first), 0);
assert.equal(JSON.parse(library.CatQuest_ReadSave()).value, first);
full = true;
assert.match(library.CatQuest_WriteSave(first, 'replacement'), /not saved/);
assert.equal(value, first);
full = false;
const second = JSON.stringify({ format: 1, primary: '{"version":5,"fishCoins":10}', backup: 'previous' });
assert.equal(library.CatQuest_WriteSave(first, second), 0);
assert.match(library.CatQuest_WriteSave(first, 'stale'), /another tab/);
assert.equal(value, second);
blocked = true;
assert.match(JSON.parse(library.CatQuest_ReadSave()).error, /blocked/);
assert.match(library.CatQuest_WriteSave(second, 'blocked'), /not saved/);
assert.equal(value, second);
console.log('PASS: UTF-8 read/write, quota failure preserves save, retry, stale writer rejected, blocked storage fails closed.');
