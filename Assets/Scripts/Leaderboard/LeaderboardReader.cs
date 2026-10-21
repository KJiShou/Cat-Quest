using System;
using System.Collections;
using UnityEngine;

public interface ILeaderboardSource
{
    IEnumerator Load(LeaderboardKind kind, Action<LeaderboardSnapshot> complete);
    void Cancel();
}

public sealed class LeaderboardReader : ILeaderboardSource
{
    readonly AccountController account;
    readonly IAccountHttp http;
    int generation;
    public LeaderboardReader(AccountController account, IAccountHttp http = null) { this.account = account; this.http = http ?? new AccountHttp(); }
    public void Cancel() { generation++; http.Cancel(); }
    public IEnumerator Load(LeaderboardKind kind, Action<LeaderboardSnapshot> complete)
    {
        int owner = generation;
        var state = account.State;
        var result = new LeaderboardSnapshot { kind = kind };
        if (!state.configured || !state.signedIn)
        {
            result.message = state.configured ? "Sign in with Google to view the leaderboard." : "Leaderboards are not available in this build yet.";
            result.ownMessage = "Sign in from Account. Joining the leaderboard is optional."; complete(result); yield break;
        }
        string project = null, uid = null, token = null;
        yield return account.AuthorizeRanking((p, u, t) => { project = p; uid = u; token = t; });
        if (owner != generation) yield break;
        if (token == null) { result.message = "Could not verify your sign-in. Open Account to retry."; complete(result); yield break; }
        string context = account.RankingContext;
        state = account.State;
        bool participating = state.uploadEnabled && !state.pendingRemoval && !state.wrongAccount;
        result.ownMessage = state.pendingRemoval ? "Your shared record is waiting to be removed." : state.wrongAccount ? "This save belongs to another account." : !participating ? "Not participating. Enable sharing in Account to receive a rank." : "Your statistics have not been published yet.";
        yield return Fetch(http, project, uid, token, participating, result);
        token = null;
        if (owner == generation && account.RankingContext == context) complete(result);
    }
    // Same read timestamp for top entries, own snapshot and count: no mixed-revision rank.
    public static IEnumerator Fetch(IAccountHttp http, string project, string uid, string token, bool participating, LeaderboardSnapshot result)
    {
        AccountHttpResult response = null;
        yield return http.Send(LeaderboardQuery.Url(project), "POST", LeaderboardQuery.Top(result.kind), "application/json", token, r => response = r);
        if (!ReadOkay(response, result)) yield break;
        if (!LeaderboardQuery.ParseRows(response.body, project, out var top, out string time) || top.Count > LeaderboardQuery.Limit)
        { result.message = "The ranking response could not be read. Please retry."; yield break; }
        for (int i = 1; i < top.Count; i++) if (LeaderboardQuery.Compare(top[i - 1], top[i], result.kind) >= 0)
        { result.message = "The ranking order could not be verified. Please retry."; yield break; }
        result.leaders.AddRange(top); result.readTime = time; result.success = true;
        result.message = top.Count == 0 ? "No players have shared statistics yet." : "Top " + top.Count + " players - Published statistics";
        if (!participating) yield break;
        int ownIndex = top.FindIndex(row => row.playerId == uid);
        if (ownIndex >= 0) { result.own = top[ownIndex]; result.ownRank = ownIndex + 1; result.ownMessage = null; yield break; }
        yield return http.Send(LeaderboardQuery.Url(project), "POST", LeaderboardQuery.Own(uid, time), "application/json", token, r => response = r);
        if (!ReadOkay(response, result, true)) yield break;
        if (!LeaderboardQuery.ParseRows(response.body, project, out var own, out _) || own.Count > 1 || (own.Count == 1 && own[0].playerId != uid))
        { result.ownMessage = "Your rank could not be read. Please retry."; yield break; }
        if (own.Count == 0) yield break;
        yield return http.Send(LeaderboardQuery.Url(project, true), "POST", LeaderboardQuery.Rank(result.kind, own[0], time), "application/json", token, r => response = r);
        if (!ReadOkay(response, result, true)) yield break;
        if (!LeaderboardQuery.ParseRank(response.body, out long rank) || rank <= top.Count)
        { result.ownMessage = "Your rank could not be verified. Please retry."; yield break; }
        result.own = own[0]; result.ownRank = rank; result.ownMessage = null;
    }
    static bool ReadOkay(AccountHttpResult response, LeaderboardSnapshot result, bool ownOnly = false)
    {
        if (response != null && response.Success) return true;
        string error = response?.code == 403 ? "Leaderboards are unavailable. Please try again later." : response?.code == 400 || response?.code == 409
            ? "This leaderboard is being prepared. Please try again later." : "Could not load rankings. Check your connection and retry.";
        if (ownOnly) result.ownMessage = error; else result.message = error;
        return false;
    }
}
