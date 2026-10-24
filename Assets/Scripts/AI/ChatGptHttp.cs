using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public interface IChatGptHttp
{
    Task<string> Send(string url, string method, string body, string contentType, string bearer, bool stream, CancellationToken cancellation);
}

public sealed class ChatGptHttp : IChatGptHttp, IDisposable
{
    readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<string> Send(string url, string method, string body, string contentType, string bearer, bool stream, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, contentType);
        if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                throw new ArgumentException(status == 401 ? "ChatGPT authorization expired. Reconnect in AI settings." :
                    status == 429 ? "ChatGPT usage limit reached. Manage usage in AI settings." :
                    status == 403 ? "ChatGPT access is unavailable for this account or app." :
                    "ChatGPT request failed (HTTP " + status + "). Try again later.");
            }
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(input, new UTF8Encoding(false, true));
            var text = new StringBuilder(); var events = stream ? new ChatGptEventStream() : null;
            var buffer = new char[1024];
            while (true)
            {
                // .NET Standard StreamReader lacks the cancellation overload: cancel the underlying stream.
                using var registration = deadline.Token.Register(() => input.Dispose());
                deadline.Token.ThrowIfCancellationRequested();
                int count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0) break;
                string chunk = new string(buffer, 0, count);
                if (events != null)
                {
                    events.Push(chunk); if (events.Completed) return events.Text;
                }
                // Account model catalogs include capability metadata and can exceed 256 KiB.
                // Keep JSON bounded separately from the much smaller inference event stream.
                else { text.Append(chunk); if (text.Length > ChatGptJson.MaximumDocumentCharacters) throw new ArgumentException("ChatGPT response was too large."); }
            }
            if (stream) throw new ArgumentException("ChatGPT stream ended before completion. Try again.");
            return text.ToString();
        }
        catch (Exception error) when (error is HttpRequestException || error is IOException || error is System.Net.WebException ||
            error is System.Net.Sockets.SocketException || error is ObjectDisposedException || error is OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
            throw new ArgumentException("Cannot reach ChatGPT or the request timed out. Your local tasks are available.");
        }
    }
    public void Dispose() => client.Dispose();
}
