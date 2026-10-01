using Clippy.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public sealed class ChatService : IChatService
    {
        private static readonly HttpClient DefaultClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private readonly ISettingsService settings;
        private readonly IKeyService keys;
        private readonly HttpClient client;

        public ChatService(ISettingsService settings, IKeyService keys, HttpClient? client = null)
        {
            this.settings = settings;
            this.keys = keys;
            this.client = client ?? DefaultClient;
        }

        private HttpRequestMessage CreateRequest(IEnumerable<IMessage> messages, bool stream)
        {
            var url = settings.ServerUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Set a valid HTTP server URL in Settings, for example http://192.168.1.10:8080/v1.");
            if (string.IsNullOrWhiteSpace(settings.Model))
                throw new InvalidOperationException("Set the server's model name or alias in Settings.");
            if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url += "/v1";
            var request = new HttpRequestMessage(HttpMethod.Post, url + "/chat/completions");
            var key = keys.GetKey();
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = settings.Model.Trim(),
                messages = messages.Where(m => !string.IsNullOrWhiteSpace(m.MessageText))
                    .Select(m => new { role = m.Role.ToString().ToLowerInvariant(), content = m.MessageText }).ToArray(),
                max_tokens = settings.Tokens,
                stream
            }), Encoding.UTF8, "application/json");
            return request;
        }

        private static async Task CheckResponse(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Model server returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
        }

        public async Task<string> SendChatAsync(IEnumerable<IMessage> messages)
        {
            using var request = CreateRequest(messages, false);
            using var response = await client.SendAsync(request);
            await CheckResponse(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        public async IAsyncEnumerable<string> StreamChatAsync(IEnumerable<IMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(messages, true);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await CheckResponse(response);
            using var body = await response.Content.ReadAsStreamAsync();
            // Closing the stream interrupts ReadLineAsync on netstandard when cancelled.
            using var registration = cancellationToken.Register(() => body.Dispose());
            using var reader = new StreamReader(body);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line;
                try { line = await reader.ReadLineAsync(); }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                { throw new OperationCanceledException(cancellationToken); }
                if (line == null) yield break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line.Substring(5).Trim();
                if (data == "[DONE]") yield break;
                if (data.Length == 0) continue;
                using var json = JsonDocument.Parse(data);
                if (json.RootElement.TryGetProperty("error", out var error))
                    throw new HttpRequestException("Model server error: " + error);
                if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                if (choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    yield return content.GetString()!;
            }
        }
    }
}
