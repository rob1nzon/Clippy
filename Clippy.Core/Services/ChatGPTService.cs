using Clippy.Core.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public class ChatGPTService : IChatService
    {
        private const string ClippyStart = "Hi! I'm Clippy, your Windows assistant. Would you like some assistance?";
        private const string Instruction = "You are Microsoft Clippy revived as a Windows assistant. Be helpful, concise, and speak in Clippy's friendly style.";
        private static readonly HttpClient HttpClient = new HttpClient();

        public ObservableCollection<IMessage> Messages { get; } = new ObservableCollection<IMessage>();

        private readonly ISettingsService settings;
        private readonly IKeyService keyService;

        public ChatGPTService(ISettingsService settings, IKeyService keyService)
        {
            this.settings = settings;
            this.keyService = keyService;
            Refresh();
        }

        public void Refresh()
        {
            Messages.Clear();
            Add(new ClippyMessage(ClippyStart, true));
        }

        public async Task SendAsync(IMessage message)
        {
            Add(message);

            var apiMessages = new List<object>
            {
                new { role = "system", content = Instruction }
            };

            foreach (var existingMessage in Messages)
            {
                if (string.IsNullOrWhiteSpace(existingMessage.Message))
                    continue;

                apiMessages.Add(new
                {
                    role = existingMessage is UserMessage ? "user" : "assistant",
                    content = existingMessage.Message
                });
            }

            var responseMessage = new ClippyMessage(true);
            Add(responseMessage);

            try
            {
                var baseUrl = settings.OpenAIBaseUrl?.Trim().TrimEnd('/');
                var model = settings.OpenAIModel?.Trim();
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
                    throw new InvalidOperationException("Set a valid server URL in Settings.");
                if (string.IsNullOrWhiteSpace(model))
                    throw new InvalidOperationException("Set a model name in Settings.");

                var endpoint = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                    ? baseUrl
                    : baseUrl + "/chat/completions";
                var body = JsonSerializer.Serialize(new
                {
                    model,
                    messages = apiMessages,
                    max_tokens = settings.Tokens,
                    stream = false
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                var apiKey = keyService.GetKey()?.Trim();
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await HttpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Server returned {(int)response.StatusCode}: {ReadError(responseBody)}");

                using var document = JsonDocument.Parse(responseBody);
                responseMessage.Message = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
            catch (Exception exception)
            {
                responseMessage.Message = $"Unable to chat: {exception.Message}";
            }
            finally
            {
                responseMessage.IsLatest = false;
            }
        }

        private static string ReadError(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? responseBody;
            }
            catch
            {
                return responseBody;
            }
        }

        private void Add(IMessage message)
        {
            foreach (var existingMessage in Messages)
            {
                if (existingMessage is ClippyMessage clippyMessage)
                    clippyMessage.IsLatest = false;
            }
            Messages.Add(message);
        }
    }
}
