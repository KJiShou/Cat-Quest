# Actual 1.2.1 release upgrade: outstanding isolated-environment gate

The actual old release executable has **not** been launched by this verification. On 2026-11-16 the host had no available Windows Sandbox/Hyper-V/VMware/VirtualBox command, and the user confirmed that no separate test environment is available. Do not count the disposable-parent updater test as this gate.

The existing release managed assembly was read-only inspected and its SHA-256 was rechecked:

`3117962145F56F395F9F52F22CD591D0A97C26BA53DB9592022A3723FEA71C1E`

The old `--focus-smoke --smoke-dir` path isolates JSON, but it does not isolate Unity PlayerPrefs. The release also rejects `PrepareSmokeUpdate` with “Update fixtures are unavailable in release builds”. Redirecting only AppData or the JSON directory does not solve this. Neither patching the old assembly nor running it against the current Windows account establishes a safe, unmodified-release test.

## Required environment and evidence

Use a disposable Windows VM or an independently provisioned Windows test account with no real Cat Quest data, credentials or preferences. Keep the original host save and running old application inaccessible to the test. For a VM, disable network and shared clipboard; transfer only the release binaries, new verification package, test scripts and synthetic fixtures. No VM or account was installed or created on the host.

1. Record OS/profile isolation, hashes of the unchanged old executable and managed assembly, and the empty test storage/profile before launch.
2. Run the actual old Unity release with an absolute isolated smoke directory. Use its supported smoke controls to perform task/Focus operations and close it normally. Preserve the JSON **written by that old process**, its backup, source hashes, process identity and result logs. A JSON produced only by the current code's compatibility service is insufficient for this step.
3. Restart that same old release against the generated test data. Exercise its normal updater preparation if a local fixture is supported. If it is not, explicitly distinguish that missing UI route from a coordinator-driven invocation of the real updater waiting for the actual old process to exit. Do not publish or replace a public update manifest to make the test work.
4. Run the real updater into a disposable installation directory containing the old build. Record the actual old PID/exit, updater request, package hash, update result and launched new PID. Adapt the isolated assertions in `Tools/Verify-SqliteUpdateHandoff.ps1`; its existing disposable parent is not an actual old Unity process.
5. Compare all migrated fields, counts, ordered records, snapshots and balances with the frozen old output. Check SQLite integrity/foreign keys, perform a new task or Focus settlement, close and restart the new application, and verify the new progress remains in SQLite.
6. Modify only the retained test JSON after successful migration and restart again: SQLite must remain authoritative. Test a failed update against a separate copy and verify source save hashes remain unchanged. Retain the pre-update installation, source data, reports and screenshots until review.

The current automated updater/new-player handoff is useful evidence for installation, migration and restart, but it does not fill the unexecuted actual-old-release preparation/exit route. This remains an outstanding verification requirement. The maintainer authorized publishing v1.4.0 after this limitation was disclosed on 2026-11-16; that authorization does not establish a test pass.
