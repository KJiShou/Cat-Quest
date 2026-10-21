using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class AccountHttpResult
{
    public long code;
    public string body;
    public bool transportError;
    public bool Success => !transportError && code >= 200 && code < 300;
    public string Message => transportError ? "Cannot connect. Your local progress is safe; retry when online." :
        code == 401 ? "Your sign-in expired. Sign in again." :
        code == 403 ? "Account access is unavailable. Please retry later." :
        code == 429 ? "The service is busy. Please retry later." :
        code >= 500 ? "The account service is unavailable. Please retry later." :
        "The account request could not finish. Please try signing in again.";
}

public interface IAccountHttp
{
    IEnumerator Send(string url, string method, string body, string contentType, string bearer, Action<AccountHttpResult> complete);
    void Cancel();
}

public sealed class AccountHttp : IAccountHttp
{
    UnityWebRequest active;
    public IEnumerator Send(string url, string method, string body, string contentType, string bearer, Action<AccountHttpResult> complete)
    {
        using var request = new UnityWebRequest(url, method);
        active = request;
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = 20;
        request.redirectLimit = 0; // Authenticated requests must not follow a redirect to another origin.
        if (body != null)
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", contentType);
        }
        if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
        try
        {
            yield return request.SendWebRequest();
            complete(new AccountHttpResult { code = request.responseCode, body = request.downloadHandler.text,
                transportError = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError });
        }
        finally { if (active == request) active = null; }
    }
    public void Cancel() { active?.Abort(); }
}

public static class AccountRest
{
    [Serializable] sealed class JsonString { public string value; }
    [Serializable] public sealed class Value { public string stringValue, integerValue, timestampValue; }
    [Serializable] public sealed class Fields
    {
        public Value playerId, playerName, bossStage, totalTasksCompleted, totalFocusMinutes, power, catLevel, updatedAt;
    }
    [Serializable] public sealed class Document { public string name, updateTime; public Fields fields; }
    [Serializable] sealed class CommitResult { public WriteResult[] writeResults; }
    [Serializable] sealed class WriteResult { public string updateTime; }
    [Serializable] public sealed class GoogleTokens { public string id_token, access_token; }
    [Serializable] public sealed class GoogleClaims { public string aud, iss, nonce; public long exp; }
    [Serializable] public sealed class FirebaseSignIn { public string localId, idToken, refreshToken, expiresIn, email; }
    [Serializable] public sealed class FirebaseRefresh { public string user_id, id_token, refresh_token, expires_in; }

    public static T Parse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonUtility.FromJson<T>(json); } catch (ArgumentException) { return null; }
    }
    public static string Quote(string value)
    {
        string wrapped = JsonUtility.ToJson(new JsonString { value = value ?? "" });
        return wrapped.Substring(9, wrapped.Length - 10);
    }
    public static string Form(string value) => Uri.EscapeDataString(value ?? "");
    public static string DocumentPath(string project, string uid) => "projects/" + project + "/databases/(default)/documents/leaderboard/" + uid;
    public static string DocumentUrl(string project, string uid) => "https://firestore.googleapis.com/v1/" + DocumentPath(project, Form(uid));
    public static string CommitUrl(string project) => "https://firestore.googleapis.com/v1/projects/" + project + "/databases/(default)/documents:commit";
    public static string IdentityUrl(string key) => "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" + Form(key);
    public static string RefreshUrl(string key) => "https://securetoken.googleapis.com/v1/token?key=" + Form(key);
    public static string FirebaseSignInBody(string googleIdToken, string projectId) =>
        "{\"postBody\":" + Quote("id_token=" + Form(googleIdToken) + "&providerId=google.com") +
        ",\"requestUri\":" + Quote("https://" + projectId + ".firebaseapp.com") + ",\"returnSecureToken\":true,\"returnIdpCredential\":false}";
    static string Text(string value) => "{\"stringValue\":" + Quote(value) + "}";
    static string Number(long value) => "{\"integerValue\":" + Quote(value.ToString(CultureInfo.InvariantCulture)) + "}";
    public static string BuildAggregateCommit(string project, AccountAggregate aggregate, string expectedUpdateTime)
    {
        return "{\"writes\":[{\"update\":{\"name\":" + Quote(DocumentPath(project, aggregate.playerId)) + ",\"fields\":{" +
            "\"playerId\":" + Text(aggregate.playerId) + ",\"playerName\":" + Text(aggregate.playerName) +
            ",\"bossStage\":" + Number(aggregate.bossStage) + ",\"totalTasksCompleted\":" + Number(aggregate.totalTasksCompleted) +
            ",\"totalFocusMinutes\":" + Number(aggregate.totalFocusMinutes) + ",\"power\":" + Number(aggregate.power) +
            ",\"catLevel\":" + Number(aggregate.catLevel) + "}},\"updateTransforms\":[{\"fieldPath\":\"updatedAt\",\"setToServerValue\":\"REQUEST_TIME\"}]," +
            "\"currentDocument\":" + (string.IsNullOrEmpty(expectedUpdateTime) ? "{\"exists\":false}" : "{\"updateTime\":" + Quote(expectedUpdateTime) + "}") + "}]}";
    }
    public static string CommitUpdateTime(string body) => Parse<CommitResult>(body)?.writeResults is { Length: > 0 } results ? results[0].updateTime : null;
    public static bool TryRemote(string body, string uid, out AccountRemoteSnapshot remote)
    {
        remote = null;
        var document = Parse<Document>(body); var f = document?.fields;
        bool NumberValue(Value value, out long number) => long.TryParse(value?.integerValue, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        if (f == null || f.playerId?.stringValue != uid || !AccountAggregate.ValidName(f.playerName?.stringValue) ||
            !DateTimeOffset.TryParse(document.updateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !NumberValue(f.bossStage, out long stage) || stage < 1 || stage > int.MaxValue ||
            !NumberValue(f.power, out long power) || power < 0 || power > int.MaxValue ||
            !NumberValue(f.catLevel, out long level) || level < 1 || level > int.MaxValue ||
            !NumberValue(f.totalTasksCompleted, out long tasks) || !NumberValue(f.totalFocusMinutes, out long minutes) ||
            string.IsNullOrEmpty(f.updatedAt?.timestampValue)) return false;
        remote = new AccountRemoteSnapshot { exists = true, updateTime = document.updateTime,
            aggregate = new AccountAggregate { playerId = uid, playerName = f.playerName.stringValue, bossStage = (int)stage,
                power = (int)power, catLevel = (int)level, totalTasksCompleted = tasks, totalFocusMinutes = minutes } };
        return true;
    }
    public static bool ValidGoogleToken(string token, string clientId, string nonce)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var parts = token.Split('.'); if (parts.Length != 3) return false;
        try
        {
            string encoded = parts[1].Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            var claims = Parse<GoogleClaims>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            // Firebase verifies the signature; this also binds the returned credential to our login attempt.
            return claims != null && claims.aud == clientId && claims.nonce == nonce &&
                (claims.iss == "https://accounts.google.com" || claims.iss == "accounts.google.com") &&
                claims.exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        catch (FormatException) { return false; }
    }
}
