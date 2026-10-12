using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public sealed class SpeechService
    {
        private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(2) };
        private readonly ISettingsService settings;
        private readonly IKeyService keys;
        private readonly HttpClient client;
        public SpeechService(ISettingsService settings, IKeyService keys, HttpClient? client = null)
        { this.settings = settings; this.keys = keys; this.client = client ?? DefaultClient; }

        public static Uri ValidateEndpoint(string url)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Укажите полный HTTP URL GigaAM, например http://192.168.1.10:8000/v1/audio/transcriptions.");
            return uri;
        }

        public async Task<string> TranscribeAsync(byte[] wav, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!settings.VoiceInputEnabled) throw new InvalidOperationException("Голосовой ввод выключен.");
            var endpoint = ValidateEndpoint(settings.SpeechServerUrl);
            if (string.IsNullOrWhiteSpace(settings.SpeechModel)) throw new InvalidOperationException("Укажите имя STT-модели в настройках.");
            if (wav.Length <= 44 || wav.Length > PcmRecordingBuffer.MaximumBytes + 44 ||
                Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                throw new ArgumentException("Пустая или некорректная запись WAV.");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            var key = keys.GetKey();
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(wav);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "recording.wav");
            form.Add(new StringContent(settings.SpeechModel.Trim()), "model");
            form.Add(new StringContent("json"), "response_format");
            request.Content = form;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"GigaAM вернул HTTP {(int)response.StatusCode}. Проверьте URL, ключ и совместимость API.");
            using var stream = await response.Content.ReadAsStreamAsync();
            using var registration = token.Register(() => stream.Dispose());
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            try
            {
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    if (body.Length + count > 65536) throw new InvalidOperationException("Слишком большой ответ GigaAM.");
                    body.Write(buffer, 0, count);
                }
            }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(body.ToArray());
            if (!json.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Ожидается ответ GigaAM в формате JSON с полем text.");
            return text.GetString()?.Trim() ?? "";
        }
    }
}
