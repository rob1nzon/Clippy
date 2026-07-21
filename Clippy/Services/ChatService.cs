using Clippy.Core.Enums;
using Clippy.Core.Interfaces;
using Clippy.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Services
{
    /// <summary>
    /// OpenAI Responses API implementation used by Clippy's chat view model.
    /// </summary>
    public sealed class ChatService : IChatService
    {
        private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";
        private const string Model = "gpt-5-mini";
        private static readonly HttpClient HttpClient = new HttpClient();

        private readonly IKeyService keyService;

        public ChatService(IKeyService keyService)
        {
            this.keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
        }

        public async Task<string> SendChatAsync(IEnumerable<IMessage> messages)
        {
            using var request = CreateRequest(messages, stream: false);
            using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            EnsureSuccess(response.StatusCode, responseBody);
            return ReadOutputText(responseBody);
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            IEnumerable<IMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(messages, stream: true);
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                EnsureSuccess(response.StatusCode, responseBody);
            }

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var data = line.Substring("data:".Length).TrimStart();
                if (data == "[DONE]")
                    yield break;

                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                    continue;

                var eventType = typeElement.GetString();
                if (eventType == "response.output_text.delta" &&
                    root.TryGetProperty("delta", out var deltaElement))
                {
                    var delta = deltaElement.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        yield return delta;
                }
                else if (eventType == "error")
                {
                    throw new InvalidOperationException(ReadErrorMessage(root));
                }
                else if (eventType == "response.failed")
                {
                    throw new InvalidOperationException(ReadResponseFailure(root));
                }
            }
        }

        private HttpRequestMessage CreateRequest(IEnumerable<IMessage> messages, bool stream)
        {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            var apiKey = keyService.GetKey()?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Add your OpenAI API key in Clippy settings first.");

            var input = messages.Select(message => new
            {
                role = ToApiRole(message.Role),
                content = message.MessageText
            });

            var body = JsonSerializer.Serialize(new
            {
                model = Model,
                input,
                stream
            });

            var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return request;
        }

        private static string ToApiRole(Role role) => role switch
        {
            Role.System => "system",
            Role.Assistant => "assistant",
            Role.User => "user",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported chat role.")
        };

        private static string ReadOutputText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("output", out var output))
                throw new InvalidOperationException("OpenAI returned no output.");

            var text = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var value))
                    {
                        text.Append(value.GetString());
                    }
                }
            }

            if (text.Length == 0)
                throw new InvalidOperationException("OpenAI returned no text output.");

            return text.ToString();
        }

        private static void EnsureSuccess(HttpStatusCode statusCode, string responseBody)
        {
            if ((int)statusCode >= 200 && (int)statusCode < 300)
                return;

            var message = TryReadApiError(responseBody);
            throw new HttpRequestException($"OpenAI request failed ({(int)statusCode}): {message}");
        }

        private static string TryReadApiError(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? responseBody;
                }
            }
            catch (JsonException)
            {
            }

            return string.IsNullOrWhiteSpace(responseBody) ? "Empty response." : responseBody;
        }

        private static string ReadErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("message", out var message))
                return message.GetString() ?? "OpenAI streaming error.";

            if (root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out message))
            {
                return message.GetString() ?? "OpenAI streaming error.";
            }

            return "OpenAI streaming error.";
        }

        private static string ReadResponseFailure(JsonElement root)
        {
            if (root.TryGetProperty("response", out var response) &&
                response.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "OpenAI response failed.";
            }

            return "OpenAI response failed.";
        }
    }
}
