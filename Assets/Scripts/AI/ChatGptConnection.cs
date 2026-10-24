using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ChatGptModel { public string slug, name; }

// Optional network client. No Unity objects, gameplay state or rewards are accessed here.
public sealed class ChatGptConnection : IDisposable
{
    public const string Issuer = "https://auth.openai.com", Resource = "https://api.openai.com/v1";
    readonly IChatGptStorage storage;
    readonly IChatGptHttp http;
    readonly Func<long> now;
    readonly SemaphoreSlim tokenGate = new SemaphoreSlim(1, 1);
    Dictionary<string, object> session = new Dictionary<string, object>();
    public string Host { get; private set; }
    public string Model { get; private set; }
    public string Language { get; private set; } = "auto";
    public List<ChatGptModel> Models { get; private set; } = new List<ChatGptModel>();
    public string ClientId => ChatGptJson.Text(session, "client_id");
    public string Account => ChatGptJson.Text(session, "email") ?? "ChatGPT account";
    public bool Connected => !string.IsNullOrEmpty(ChatGptJson.Text(session, "refresh_token")) && HasPermission(session);
    public ChatGptConnection(IChatGptStorage storage, IChatGptHttp http, Func<long> now = null)
    {
        this.storage = storage; this.http = http; this.now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public Task Initialize() => Task.Run(() => {
        string host = storage.Read("host.json", false);
        Host = host == null ? null : ChatGptJson.Text(ChatGptJson.Object(host), "id");
        if (string.IsNullOrEmpty(Host))
        {
            Host = "urn:uuid:" + Guid.NewGuid().ToString();
            storage.Write("host.json", ChatGptJson.Encode(new Dictionary<string, object> { ["id"] = Host }), false);
        }
        string credentials = storage.Read("connection.dpapi", true);
        if (credentials != null) session = ChatGptJson.Object(credentials);
        string preferences = storage.Read("preferences.json", false);
        if (preferences != null)
        {
            var data = ChatGptJson.Object(preferences);
            Model = ChatGptJson.Text(data, "model");
            string language = ChatGptJson.Text(data, "language");
            Language = language == "en" || language == "zh" ? language : "auto";
        }
    });
    public static string Form(IDictionary<string, string> data) => string.Join("&", data.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? "")));
    Task<string> Get(string url, CancellationToken cancellation, string bearer = null) => http.Send(url, "GET", null, null, bearer, false, cancellation);
    Task<string> Post(string url, IDictionary<string, string> data, CancellationToken cancellation) =>
        http.Send(url, "POST", Form(data), "application/x-www-form-urlencoded", null, false, cancellation);
    static bool HasPermission(IDictionary<string, object> tokens) => (ChatGptJson.Text(tokens, "scope") ?? "").Split(' ').Contains("chatgpt.tokens.use.direct");
    public async Task CompleteSignIn(ChatGptBrowserAuth browser, CancellationToken cancellation)
    {
        using var cancel = cancellation.Register(browser.Dispose);
        var callback = await browser.Completion.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        string expectedSubject = ChatGptJson.Text(session, "sub"), expectedEmail = ChatGptJson.Text(session, "email");
        // Persist registration even if token exchange fails; future authorization must reuse it.
        var registration = new Dictionary<string, object> { ["client_id"] = callback["client_id"], ["sub"] = expectedSubject, ["email"] = expectedEmail };
        await Task.Run(() => storage.Write("connection.dpapi", ChatGptJson.Encode(registration), true), cancellation).ConfigureAwait(false);
        session = registration;
        Dictionary<string, object> tokens = null;
        bool accepted = false;
        try
        {
            tokens = ChatGptJson.Object(await Post(Issuer + "/api/accounts/oauth/token", new Dictionary<string, string> {
                ["grant_type"] = "authorization_code", ["client_id"] = callback["client_id"], ["code"] = callback["code"],
                ["code_verifier"] = browser.Verifier, ["redirect_uri"] = browser.RedirectUri, ["resource"] = Resource
            }, cancellation).ConfigureAwait(false));
            var jwks = ChatGptJson.Object(await Get(Issuer + "/.well-known/jwks.json", cancellation).ConfigureAwait(false));
            var claims = ValidateIdentity(ChatGptJson.Text(tokens, "id_token"), jwks, callback["client_id"], browser.Nonce, now());
            if (!string.IsNullOrEmpty(expectedSubject) && ChatGptJson.Text(claims, "sub") != expectedSubject)
                throw new ArgumentException("This registration belongs to a different ChatGPT account. Reconnect its original account.");
            if (!HasPermission(tokens) || ChatGptJson.Text(tokens, "token_type") != "Bearer" ||
                string.IsNullOrEmpty(ChatGptJson.Text(tokens, "access_token")) || string.IsNullOrEmpty(ChatGptJson.Text(tokens, "refresh_token")))
                throw new ArgumentException("ChatGPT plan usage was not granted. Local tasks are still available.");
            tokens["client_id"] = callback["client_id"]; tokens["sub"] = ChatGptJson.Text(claims, "sub");
            tokens["email"] = ChatGptJson.Text(claims, "email"); tokens["expires_at"] = now() + ChatGptJson.Number(tokens, "expires_in");
            cancellation.ThrowIfCancellationRequested();
            storage.Write("connection.dpapi", ChatGptJson.Encode(tokens), true); session = tokens; accepted = true;
        }
        finally
        {
            if (!accepted && tokens != null && !string.IsNullOrEmpty(ChatGptJson.Text(tokens, "refresh_token")))
                try { await Revoke(ChatGptJson.Text(tokens, "refresh_token"), callback["client_id"], CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { throw new ArgumentException("Sign-in failed and remote revocation was not confirmed. Disconnect Cat Quest in ChatGPT settings."); }
        }
    }
    public static Dictionary<string, object> ValidateIdentity(string token, Dictionary<string, object> jwks, string clientId, string nonce, long time)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > 32768) throw new ArgumentException();
            var parts = token.Split('.'); if (parts.Length != 3) throw new ArgumentException();
            var header = ChatGptJson.Object(Encoding.UTF8.GetString(ChatGptBrowserAuth.Unbase64(parts[0])));
            var claims = ChatGptJson.Object(Encoding.UTF8.GetString(ChatGptBrowserAuth.Unbase64(parts[1])));
            if (ChatGptJson.Text(header, "alg") != "RS256") throw new ArgumentException();
            string kid = ChatGptJson.Text(header, "kid");
            var keys = ((List<object>)jwks["keys"]).OfType<Dictionary<string, object>>()
                .Where(k => !string.IsNullOrEmpty(kid) && ChatGptJson.Text(k, "kid") == kid && ChatGptJson.Text(k, "kty") == "RSA").ToList();
            if (keys.Count != 1) throw new ArgumentException();
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = ChatGptBrowserAuth.Unbase64(ChatGptJson.Text(keys[0], "n")), Exponent = ChatGptBrowserAuth.Unbase64(ChatGptJson.Text(keys[0], "e")) });
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), ChatGptBrowserAuth.Unbase64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new ArgumentException();
            claims.TryGetValue("aud", out var aud);
            bool audience = aud is string text ? text == clientId : aud is List<object> list && list.Contains(clientId);
            bool multi = aud is List<object> audiences && audiences.Count > 1;
            string azp = ChatGptJson.Text(claims, "azp");
            if (!audience || ChatGptJson.Text(claims, "iss") != Issuer || ChatGptJson.Text(claims, "nonce") != nonce ||
                string.IsNullOrEmpty(ChatGptJson.Text(claims, "sub")) || ChatGptJson.Number(claims, "exp") <= time ||
                ChatGptJson.Number(claims, "nbf") > time + 60 || (multi && azp != clientId) || (azp != null && azp != clientId)) throw new ArgumentException();
            return claims;
        }
        catch (Exception error) when (error is ArgumentException || error is FormatException || error is CryptographicException || error is KeyNotFoundException || error is InvalidCastException)
        { throw new ArgumentException("ChatGPT identity validation failed. Reconnect in AI settings."); }
    }
    async Task<string> Token(CancellationToken cancellation, bool forceRefresh = false)
    {
        await tokenGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (!Connected) throw new ArgumentException("Connect ChatGPT in AI settings.");
            if (forceRefresh || ChatGptJson.Number(session, "expires_at") <= now() + 60)
            {
                var renewed = ChatGptJson.Object(await Post(Issuer + "/api/accounts/oauth/token", new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token", ["client_id"] = ClientId,
                    ["refresh_token"] = ChatGptJson.Text(session, "refresh_token"), ["resource"] = Resource
                }, cancellation).ConfigureAwait(false));
                var candidate = new Dictionary<string, object>(session);
                foreach (var item in renewed) candidate[item.Key] = item.Value;
                candidate["expires_at"] = now() + ChatGptJson.Number(renewed, "expires_in");
                // Keep the latest rotating token in memory even if disk is temporarily unavailable.
                session = candidate;
                storage.Write("connection.dpapi", ChatGptJson.Encode(candidate), true);
                if (!HasPermission(candidate)) throw new ArgumentException("ChatGPT plan usage is no longer authorized.");
            }
            return ChatGptJson.Text(session, "access_token");
        }
        finally { tokenGate.Release(); }
    }
    public async Task LoadModels(CancellationToken cancellation)
    {
        string bearer = await Token(cancellation).ConfigureAwait(false);
        var catalog = ChatGptJson.Object(await Get(Resource + "/models", cancellation, bearer).ConfigureAwait(false));
        var models = ((List<object>)catalog["models"]).OfType<Dictionary<string, object>>()
            .Where(m => ChatGptJson.Text(m, "visibility") == "list" && !string.IsNullOrEmpty(ChatGptJson.Text(m, "slug")))
            .Select(m => new ChatGptModel { slug = ChatGptJson.Text(m, "slug"), name = ChatGptJson.Text(m, "display_name") ?? ChatGptJson.Text(m, "slug") }).ToList();
        if (models.Count == 0) throw new ArgumentException("No models are available for this ChatGPT account.");
        Models = models;
        if (!models.Any(m => m.slug == Model)) await SavePreferences(models[0].slug).ConfigureAwait(false);
    }
    public Task SavePreferences(string model, string language = null) => Task.Run(() => {
        language = language ?? Language;
        if (language != "auto" && language != "en" && language != "zh") throw new ArgumentException("Invalid chat language.");
        storage.Write("preferences.json", ChatGptJson.Encode(new Dictionary<string, object> { ["model"] = model, ["language"] = language }), false);
        Model = model; Language = language;
    });
    public string LanguageInstruction => InstructionForLanguage(Language);
    public static string InstructionForLanguage(string language) => language == "en" ? " Reply and write proposed task text in English, regardless of prior conversation language. Preserve existing task names when referring to them." :
        language == "zh" ? " Reply and write proposed task text in Simplified Chinese, regardless of prior conversation language. Preserve existing task names when referring to them." :
        " Match the language of the latest user message, not older replies or task data. For mixed-language messages use the dominant language of that message.";
    public async Task<string> Chat(object[] messages, CancellationToken cancellation, string turnLanguage = null)
    {
        if (string.IsNullOrEmpty(Model)) throw new ArgumentException("Select a model in AI settings.");
        string model = Model, bearer = await Token(cancellation).ConfigureAwait(false);
        string body = ChatGptJson.Encode(new Dictionary<string, object> {
            ["model"] = model, ["store"] = false, ["stream"] = true,
            ["instructions"] = AiChatProtocol.Instructions + InstructionForLanguage(turnLanguage ?? Language), ["input"] = messages
        });
        return await http.Send(Resource + "/responses", "POST", body, "application/json", bearer, true, cancellation).ConfigureAwait(false);
    }
    async Task Revoke(string refresh, string client, CancellationToken cancellation)
    {
        await Post(Issuer + "/api/accounts/oauth/revoke", new Dictionary<string, string> {
            ["token"] = refresh, ["token_type_hint"] = "refresh_token", ["client_id"] = client
        }, cancellation).ConfigureAwait(false);
    }
    public async Task Disconnect(CancellationToken cancellation)
    {
        await tokenGate.WaitAsync(cancellation).ConfigureAwait(false);
        bool revoked = true;
        try
        {
            var prior = session;
            try
            {
                if (!string.IsNullOrEmpty(ChatGptJson.Text(prior, "refresh_token")))
                    await Revoke(ChatGptJson.Text(prior, "refresh_token"), ChatGptJson.Text(prior, "client_id"), cancellation).ConfigureAwait(false);
            }
            catch (Exception) { revoked = false; }
            finally
            {
                session = new Dictionary<string, object> { ["client_id"] = ChatGptJson.Text(prior, "client_id"),
                    ["sub"] = ChatGptJson.Text(prior, "sub"), ["email"] = ChatGptJson.Text(prior, "email") };
                Models = new List<ChatGptModel>(); storage.Write("connection.dpapi", ChatGptJson.Encode(session), true);
            }
        }
        finally { tokenGate.Release(); }
        if (!revoked) throw new ArgumentException("Disconnected locally. Remote revocation was not confirmed; disconnect Cat Quest in ChatGPT settings.");
    }
    public void Dispose() { (http as IDisposable)?.Dispose(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public async Task RefreshForVerification(CancellationToken cancellation) { await Token(cancellation, true).ConfigureAwait(false); }
#endif
}
