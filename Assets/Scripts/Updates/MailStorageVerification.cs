#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public static class MailStorageVerification
{
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        string path = Path.Combine(folder, "mail.sqlite3");
        string backup = Path.Combine(folder, "mail.catquestbackup");
        string restoredPath = Path.Combine(folder, "restored.sqlite3");
        var repo = new SqlitePlayerRepository(path);
        try { check((await repo.InitializeAsync()).Succeeded, "Isolated save fixture created"); }
        finally { await repo.CloseAsync(); }
        using (var db = new SQLite.SQLiteConnection(path))
        {
            db.Execute("DROP TABLE cq_mail_state");
            db.Execute("PRAGMA user_version=2");
        }

        repo = new SqlitePlayerRepository(path);
        repo.FaultPoint = stage => { if (stage == "schema-upgrade-rows") throw new IOException("Injected schema interruption"); };
        try { check(!(await repo.InitializeAsync()).Succeeded, "Schema upgrade interruption is reported"); }
        finally { await repo.CloseAsync(); }
        using (var db = new SQLite.SQLiteConnection(path, SQLite.SQLiteOpenFlags.ReadOnly))
            check(db.ExecuteScalar<int>("PRAGMA user_version") == 2 && db.GetTableInfo("cq_mail_state").Count == 0,
                "Mail schema upgrade rolls back cleanly");

        repo = new SqlitePlayerRepository(path);
        var service = new SqlitePlayerService(repo, new SystemTaskClock());
        bool offline = false;
        string feed = Feed("welcome-mail", 100, 550, 2);
        Task<string> Fetch() => offline ? Task.FromException<string>(new IOException("Offline fixture")) : Task.FromResult(feed);
        var now = DateTimeOffset.Parse("2026-11-19T17:30:00Z");
        try
        {
            check((await service.InitializeAsync()).Succeeded && Directory.GetFiles(folder, "*.before-schema-2-to-3-*.sqlite3").Length > 0,
                "Version two upgrades with a retained SQLite backup");
            var inbox = new MailInbox(service, path, Fetch, () => now);
            check(await inbox.RefreshAsync() && inbox.Letters().Length == 1, "Reward catalogue data is fetched and cached");
            var letter = inbox.Letters().Single();
            check((await service.ReadLetterAsync(letter.Key)).Succeeded, "Opening a letter persists read state");

            offline = true;
            var cached = new MailInbox(service, path, Fetch, () => now);
            await cached.LoadCacheAsync();
            check(cached.Letters().Single().id == letter.id && !(await cached.ClaimAsync(letter)).Succeeded && service.FishCoins == 0,
                "Offline letters are readable and require online approval to claim");
            offline = false;

            feed = @"{""format"":1,""letters"":[]}";
            check(!(await inbox.ClaimAsync(letter)).Succeeded && inbox.Letters().Length == 0 && service.Experience == 0,
                "Withdrawn reward cannot be claimed");
            feed = Feed(letter.id, 101, 550, 2);
            check(!(await inbox.ClaimAsync(letter)).Succeeded && service.Experience == 0, "Changed attachments require review");
            feed = Feed(letter.id, 100, 550, 2, "2026-12-19T00:00:00Z");
            check(!(await inbox.ClaimAsync(letter)).Succeeded && inbox.Letters().Length == 0, "Future mail is hidden and cannot be claimed");
            feed = Feed(letter.id, 100, 550, 2);
            check(await inbox.RefreshAsync(), "Original campaign is available again");

            var daily = await repo.ReadAsync(tx => tx.ReadDayTotals(TaskService.DateKey(service.LocalNow)));
            int levelEvents = 0;
            service.LevelUp += _ => levelEvents++;
            repo.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected commit failure"); };
            var failed = await inbox.ClaimAsync(letter);
            var failedState = await service.ReadMailStatesAsync(new[] { letter.Key });
            check(!failed.Succeeded && failedState.Value[letter.Key] == 1 && service.Experience == 0, "Commit failure leaves read state and avoids partial rewards");
            repo.FaultPoint = null;
            var claims = await Task.WhenAll(inbox.ClaimAsync(letter), inbox.ClaimAsync(letter));
            check(claims.All(result => result.Succeeded) && claims.Count(result => result.Value) == 1
                && service.Experience == 550 && service.FishCoins > 100 && service.Tickets >= 2 && levelEvents == 1,
                "Concurrent retry grants one reward and applies the existing level-up path");
            var afterDaily = (await repo.ReadAsync(tx => tx.ReadDayTotals(TaskService.DateKey(service.LocalNow)))).Value;
            check(afterDaily.TaskExperience == daily.Value.TaskExperience && afterDaily.TaskFishCoins == daily.Value.TaskFishCoins
                && afterDaily.TaskTickets == daily.Value.TaskTickets && afterDaily.FocusSeconds == daily.Value.FocusSeconds,
                "Mail rewards do not consume daily task or Focus allowances");

            check((await repo.ExportBackupAsync(backup)).Succeeded, "Portable backup captures mail and balances");
            check((await repo.ExecuteAsync(tx => {
                var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
                profile.fishCoins = int.MaxValue;
                tx.Put(PlayerRecordKind.Profile, profile);
                return true;
            })).Succeeded, "Overflow fixture prepared");
            feed = Feed("overflow-mail", 1, 1, 0);
            check(await inbox.RefreshAsync(), "Overflow campaign refreshed");
            var overflow = await inbox.ClaimAsync(inbox.Letters().Single());
            var savedBalance = await repo.ReadAllForVerificationAsync();
            check(!overflow.Succeeded && savedBalance.Value.fishCoins == int.MaxValue && savedBalance.Value.experience == 550,
                "Balance overflow rejects the entire attachment");
        }
        finally { await service.CloseAsync(); }

        var restored = new SqlitePlayerRepository(restoredPath);
        service = new SqlitePlayerService(restored, new SystemTaskClock());
        try
        {
            check((await service.InitializeAsync()).Succeeded && (await service.TransferBackupAsync(backup, export: false)).Succeeded, "Mailbox backup restores to a fresh database");
            feed = Feed("welcome-mail", 100, 550, 2);
            var inbox = new MailInbox(service, restoredPath, Fetch, () => now);
            check(await inbox.RefreshAsync(), "Campaign still exists after restore");
            var duplicate = await inbox.ClaimAsync(inbox.Letters().Single());
            check(duplicate.Succeeded && !duplicate.Value && service.Experience == 550, "Backup restore preserves the duplicate-claim receipt");
        }
        finally { await service.CloseAsync(); }

        var giftRepo = new SqlitePlayerRepository(Path.Combine(folder, "version-gifts.sqlite3"));
        var gifts = new SqlitePlayerService(giftRepo, new SystemTaskClock());
        try
        {
            check((await gifts.InitializeAsync()).Succeeded, "Version gift fixture initializes");
            check((await gifts.ObserveMailVersionAsync("1.4.4")).Succeeded && (await gifts.ReadVersionGiftsAsync()).Value.Length == 0,
                "A baseline installation does not generate an update gift");
            check(!(await gifts.ClaimVersionGiftAsync("1.4.5")).Succeeded, "An uninstalled version cannot pay a gift");
            check((await gifts.ObserveMailVersionAsync("1.4.5")).Succeeded && (await gifts.ReadVersionGiftsAsync()).Value.Single().ToString() == "1.4.5.0",
                "Launching an upgraded build unlocks one gift");
            var reward = SqlitePlayerService.VersionGift;
            check(reward.experience == 888 && reward.fishCoins == 888 && reward.tickets == 8,
                "Update gift contains 888 EXP, 888 Fish Coins and 8 Boss Tickets");
            var retry = await Task.WhenAll(gifts.ClaimVersionGiftAsync("1.4.5"), gifts.ClaimVersionGiftAsync("1.4.5.0"));
            check(retry.All(result => result.Succeeded) && retry.Count(result => result.Value) == 1 && gifts.Experience == 888,
                "An installed update gift pays once across equivalent version spellings");
            check((await gifts.ObserveMailVersionAsync("1.4.4")).Succeeded && (await gifts.ObserveMailVersionAsync("1.4.5")).Succeeded
                && (await gifts.ReadVersionGiftsAsync()).Value.Length == 1, "Downgrade and restart do not create new gifts");
            check((await gifts.ObserveMailVersionAsync("1.4.6")).Succeeded && (await gifts.ReadVersionGiftsAsync()).Value.Length == 2,
                "The next installed upgrade delivers its own separate gift");
        }
        finally { await gifts.CloseAsync(); }

        repo = new SqlitePlayerRepository(path);
        try
        {
            check((await repo.InitializeAsync()).Succeeded, "Original mailbox reopens");
            var saved = await repo.ReadAsync(tx => tx.ReadMailState("reward:welcome-mail"));
            check(saved.Succeeded && saved.Value == 2, "Claim receipt survives restart");
        }
        finally { await repo.CloseAsync(); }
    }

    static string Feed(string id, int fish, int experience, int tickets, string utc = "2026-11-01T00:00:00Z")
        => "{\"format\":1,\"letters\":[{\"id\":\"" + id + "\",\"title\":\"A reward letter\",\"body\":\"Enjoy your supplies.\","
            + "\"publishedUtc\":\"" + utc + "\",\"fishCoins\":" + fish + ",\"experience\":" + experience + ",\"bossTickets\":" + tickets + "}]}";
}
#endif
