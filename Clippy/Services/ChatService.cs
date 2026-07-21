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
        private static readonly HttpClient HttpClient = new HttpClient();

        private readonly IKeyService keyService;
        private readonly ISettingsService settingsService;

        public ChatService(IKeyService keyService, ISettingsService settingsService)
        {
            this.keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public async Task<string> SendChatAsync(IEnumerable<IMessage> messages)
        {
            using var request = CreateRequest(messages, stream: false);
            using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            EnsureSuccess(response.StatusCode, responseBody);
            return ReadChatCompletionText(responseBody);
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

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var deltaObject) &&
                    deltaObject.TryGetProperty("content", out var deltaElement))
                {
                    var delta = deltaElement.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        yield return delta;
                }
                else if (root.TryGetProperty("error", out _))
                {
                    throw new InvalidOperationException(ReadErrorMessage(root));
                }
            }
        }

        private HttpRequestMessage CreateRequest(IEnumerable<IMessage> messages, bool stream)
        {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            var apiKey = keyService.GetKey()?.Trim();
            var baseUrl = settingsService.OpenAIBaseUrl?.Trim().TrimEnd('/');
            var model = settingsService.OpenAIModel?.Trim();
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException("Set a valid OpenAI-compatible Base URL in Clippy settings.");
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("Set a model name in Clippy settings.");

            var input = messages.Select(message => new
            {
                role = ToApiRole(message.Role),
                content = message.MessageText
            });

            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = input,
                stream
            });

            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
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

        private static string ReadChatCompletionText(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
                return content.GetString() ?? string.Empty;

            throw new InvalidOperationException("The server returned no chat completion text.");
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

    }
}
