using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ChatGptBrowserAuth : IDisposable
{
    public readonly string Verifier = Random(), Nonce = Random(), State = Random(), RedirectUri, AuthorizationUrl, ExistingClientId;
    readonly TcpListener listener;
    readonly CancellationTokenSource stop = new CancellationTokenSource();
    int disposed;
    public Task<Dictionary<string, string>> Completion { get; }
    public ChatGptBrowserAuth(string host, string existingClientId)
    {
        ExistingClientId = existingClientId;
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(4);
        RedirectUri = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/auth/callback";
        var values = new Dictionary<string, string> {
            ["client_id"] = string.IsNullOrEmpty(existingClientId) ? "dynamic_agent_client" : existingClientId,
            ["ext_agent_host_id"] = host, ["response_type"] = "code", ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            ["resource"] = ChatGptConnection.Resource, ["state"] = State, ["nonce"] = Nonce,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(Verifier)
        };
        if (string.IsNullOrEmpty(existingClientId)) values["agent_name_hint"] = "Cat Quest";
        AuthorizationUrl = ChatGptConnection.Issuer + "/api/accounts/authorize?" + ChatGptConnection.Form(values);
        stop.CancelAfter(TimeSpan.FromMinutes(2)); Completion = Receive();
    }
    public static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Unbase64(string value)
    {
        string text = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
    }
    static string Random() { var bytes = new byte[32]; using var rng = RandomNumberGenerator.Create(); rng.GetBytes(bytes); return Base64(bytes); }
    public static string Challenge(string verifier) { using var sha = SHA256.Create(); return Base64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))); }
    public static Dictionary<string, string> ParseCallback(string target, string state, string existingClientId)
    {
        if (target == null || target.Length > 8192 || !target.StartsWith("/auth/callback?", StringComparison.Ordinal)) throw new ArgumentException("Invalid callback.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in target.Substring(15).Split('&'))
        {
            int split = part.IndexOf('='); if (split < 1) throw new ArgumentException("Invalid callback.");
            string key = Uri.UnescapeDataString(part.Substring(0, split)), value = Uri.UnescapeDataString(part.Substring(split + 1).Replace('+', ' '));
            if (values.ContainsKey(key)) throw new ArgumentException("Duplicate callback parameter."); values.Add(key, value);
        }
        if (!values.TryGetValue("state", out string returned) || returned != state) throw new ArgumentException("Invalid callback state.");
        if (values.ContainsKey("error")) throw new OperationCanceledException("ChatGPT authorization was declined.");
        if (!values.TryGetValue("code", out string code) || string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw new ArgumentException("Missing authorization code.");
        values.TryGetValue("client_id", out string issued);
        if (!string.IsNullOrEmpty(existingClientId))
        {
            if (!string.IsNullOrEmpty(issued) && issued != existingClientId) throw new ArgumentException("Client registration changed.");
            values["client_id"] = existingClientId;
        }
        else if (string.IsNullOrEmpty(issued) || !issued.StartsWith("oaiapp_", StringComparison.Ordinal)) throw new ArgumentException("No client registration returned.");
        return values;
    }
    async Task<Dictionary<string, string>> Receive()
    {
        using var registration = stop.Token.Register(() => listener.Stop());
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(2000);
                using var stream = client.GetStream();
                var buffer = new byte[16384]; int count = 0; string request = "";
                try
                {
                    while (count < buffer.Length && !request.Contains("\r\n\r\n"))
                    {
                        int read = await stream.ReadAsync(buffer, count, buffer.Length - count, timeout.Token).ConfigureAwait(false);
                        if (read == 0) break; count += read; request = Encoding.ASCII.GetString(buffer, 0, count);
                    }
                    var first = request.Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                    Dictionary<string, string> callback = null;
                    if (first.Length == 3 && first[0] == "GET" && request.Contains("\r\n\r\n"))
                        try { callback = ParseCallback(first[1], State, ExistingClientId); } catch (ArgumentException) { }
                    string body = callback != null ? "Authorization received. Return to Cat Quest." : "Invalid authorization callback.";
                    byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 " + (callback != null ? "200 OK" : "400 Bad Request") +
                        "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                    await stream.WriteAsync(response, 0, response.Length, timeout.Token).ConfigureAwait(false);
                    if (callback != null) return callback;
                }
                catch (IOException) { }
                catch (OperationCanceledException) { if (!timeout.IsCancellationRequested || stop.IsCancellationRequested) throw; }
            }
        }
        catch (SocketException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        finally { listener.Stop(); }
        throw new OperationCanceledException("ChatGPT sign-in timed out or was cancelled.");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel(); listener.Stop(); Completion.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
