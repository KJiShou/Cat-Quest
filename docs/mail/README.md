# Send Cat Quest mail

Windows players with the mailbox can receive global mail without signing in. Update letters come from the normal Windows update feed. After a newer build has started, the inbox offers a separate once-per-version gift of 8 Boss Tickets, 888 EXP, and 888 Fish Coins. This gift can be claimed offline because SQLite has already recorded the installed upgrade. The first build with the mailbox records the starting version for that save. Reward letters come from [catalog.json](catalog.json) on the main branch, independently of game releases.

Open a letter and select **Delete**, then **Confirm delete**. Deletion applies to that save, including after refresh or restart. Deleting an unclaimed letter forfeits its attachments. Claimed rewards stay in the save.

1. Copy the object in [example.json](example.json) into the catalogue's `letters` array. Give each new campaign a permanent lowercase ID such as `community-thanks-001`.
2. Set `publishedUtc` in UTC as `YYYY-MM-DDTHH:MM:SSZ`. Set all three attachments to integers between 0 and 100,000. At least one must be positive. The file supports up to 100 letters and 128 KiB.
3. From the repo root, run `python Tools/validate_mail_catalog.py`. The validator checks the proposed file and compares published IDs against the local history of `origin/main` when available. A recent local copy of that history is required for a reliable pre-publish check.
4. Merge the catalogue change to `main` to send the letter. Players check for mail on start and when opening the mailbox. GitHub delivery and caching can delay when an edit appears.

A published ID and its attachment amounts are permanent. Title and body corrections can keep the same ID. Remove a letter from the catalogue to stop future claims after the change reaches players. A withdrawn letter does not reclaim rewards that already went into a player's local save. Never reuse a withdrawn ID for a different campaign.

The client verifies the current catalogue at claim time. Previously downloaded mail can be read offline. Reward redemption is once per local save and shares the same transaction as the balance. Transferring a Cat Quest backup transfers both balances and mail receipts together.
