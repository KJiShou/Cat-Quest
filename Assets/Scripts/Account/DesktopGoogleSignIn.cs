using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Only socket/byte work runs off-thread. The controller opens the browser and uses Unity APIs.
public sealed class DesktopGoogleSignIn : IDisposable
{
    public sealed class Callback { public string code, error; }
    public readonly string Verifier, Nonce, RedirectUri, AuthorizationUrl;
    public Task<Callback> Completion { get; }
    readonly TcpListener listener;
    readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    readonly string state, path;
    int disposed;

    public DesktopGoogleSignIn(string clientId)
    {
        Verifier = RandomToken(); Nonce = RandomToken(); state = RandomToken();
        path = "/catquest-oauth/" + RandomToken() + "/";
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(4);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        RedirectUri = "http://127.0.0.1:" + port + path;
        AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Escape(clientId) +
            "&redirect_uri=" + Escape(RedirectUri) + "&response_type=code&scope=" + Escape("openid email") +
            "&code_challenge_method=S256&code_challenge=" + Challenge(Verifier) + "&state=" + Escape(state) +
            "&nonce=" + Escape(Nonce) + "&prompt=select_account";
        cancellation.CancelAfter(TimeSpan.FromMinutes(2));
        Completion = Receive();
    }

    public static string Escape(string value) => Uri.EscapeDataString(value ?? "");
    public static string Challenge(string verifier)
    {
        using var sha = SHA256.Create();
        return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
    }
    static string RandomToken()
    {
        var bytes = new byte[32]; using var rng = RandomNumberGenerator.Create(); rng.GetBytes(bytes);
        return Base64Url(bytes);
    }
    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static bool Same(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int different = 0; for (int i = 0; i < a.Length; i++) different |= a[i] ^ b[i]; return different == 0;
    }

    public static bool TryReadCallback(string target, string expectedPath, string expectedState, out Callback callback)
    {
        callback = null;
        if (target == null || target.Length > 8192) return false;
        int split = target.IndexOf('?');
        if (split < 0 || target.Substring(0, split) != expectedPath) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string item in target.Substring(split + 1).Split('&'))
            {
                int equals = item.IndexOf('=');
                string key = Uri.UnescapeDataString(equals < 0 ? item : item.Substring(0, equals));
                string value = equals < 0 ? "" : Uri.UnescapeDataString(item.Substring(equals + 1).Replace('+', ' '));
                if (values.ContainsKey(key)) return false;
                values.Add(key, value);
            }
        }
        catch (UriFormatException) { return false; }
        if (!values.TryGetValue("state", out var returnedState) || !Same(returnedState, expectedState)) return false;
        if (values.TryGetValue("error", out _))
        { callback = new Callback { error = "Google sign-in was cancelled or declined." }; return true; }
        if (!values.TryGetValue("code", out string code) || string.IsNullOrWhiteSpace(code) || code.Length > 4096) return false;
        callback = new Callback { code = code }; return true;
    }

    async Task<Callback> Receive()
    {
        using var registration = cancellation.Token.Register(() => listener.Stop());
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                using var stream = client.GetStream();
                var buffer = new byte[8192]; int count = 0; string request = "";
                try
                {
                    while (count < buffer.Length && !request.Contains("\r\n\r\n"))
                    {
                        int read = await stream.ReadAsync(buffer, count, buffer.Length - count, deadline.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        count += read; request = Encoding.ASCII.GetString(buffer, 0, count);
                    }
                    string first = request.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                    string[] parts = first.Split(' ');
                    Callback callback = null;
                    bool valid = parts.Length == 3 && parts[0] == "GET" && request.Contains("\r\n\r\n") &&
                        TryReadCallback(parts[1], path, state, out callback);
                    string body = valid ? "Google sign-in received. You can return to Cat Quest and close this tab." : "This sign-in request is invalid.";
                    byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 " + (valid ? "200 OK" : "400 Bad Request") +
                        "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                    await stream.WriteAsync(response, 0, response.Length, deadline.Token).ConfigureAwait(false);
                    if (valid) return callback;
                }
                catch (IOException) { }
                catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) break; }
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally { listener.Stop(); }
        return new Callback { error = "Google sign-in timed out or was cancelled. Try again." };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel(); listener.Stop();
        // Receive owns its token registration until completion; release the timer/source afterward.
        Completion.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
