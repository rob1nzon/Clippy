using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public interface IVoiceRecorder
    {
        Task<byte[]> RecordAsync(CancellationToken token);
        void Stop();
    }

    public sealed class VoiceInputController : ObservableObject
    {
        private readonly ISettingsService settings;
        private readonly IVoiceRecorder recorder;
        private readonly Func<byte[], CancellationToken, Task<string>> transcribe;
        private CancellationTokenSource? active;
        private bool isBusy, isRecording;
        private string status = "";
        public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }
        public bool IsRecording { get => isRecording; private set => SetProperty(ref isRecording, value); }
        public string Status { get => status; private set => SetProperty(ref status, value); }
        public event Action<string>? Transcribed;

        public VoiceInputController(ISettingsService settings, IVoiceRecorder recorder, Func<byte[], CancellationToken, Task<string>> transcribe)
        { this.settings = settings; this.recorder = recorder; this.transcribe = transcribe; }

        public void StopRecording() { if (IsRecording) recorder.Stop(); }
        public void Cancel()
        {
            active?.Cancel();
            Status = "";
        }

        public async Task StartAsync()
        {
            if (!settings.VoiceInputEnabled || IsBusy) return;
            var endpoint = settings.SpeechServerUrl;
            var model = settings.SpeechModel;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            active = cancellation;
            byte[]? wav = null;
            IsBusy = true;
            IsRecording = true;
            try
            {
                SpeechService.ValidateEndpoint(endpoint);
                Status = "● Запись голоса. Нажмите микрофон ещё раз для распознавания (максимум 60 секунд).";
                wav = await recorder.RecordAsync(cancellation.Token);
                IsRecording = false;
                cancellation.Token.ThrowIfCancellationRequested();
                if (!settings.VoiceInputEnabled || settings.SpeechServerUrl != endpoint || settings.SpeechModel != model) return;
                if (wav.Length <= 44) { Status = "Запись пуста. Попробуйте ещё раз."; return; }
                Status = "Распознаю голос через GigaAM…";
                var text = await transcribe(wav, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!settings.VoiceInputEnabled || settings.SpeechServerUrl != endpoint || settings.SpeechModel != model) return;
                if (string.IsNullOrWhiteSpace(text)) { Status = "Речь не распознана. Попробуйте ещё раз."; return; }
                Transcribed?.Invoke(text);
                Status = "Текст добавлен в поле ввода. Проверьте его перед отправкой.";
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { Status = ""; }
            catch (Exception)
            {
                Status = "Ошибка голосового ввода. Проверьте микрофон, разрешения Windows, URL GigaAM и совместимость API.";
            }
            finally
            {
                if (wav != null) Array.Clear(wav, 0, wav.Length);
                active = null;
                IsRecording = false;
                IsBusy = false;
            }
        }

        public static string AppendToDraft(string draft, string transcription, int maximum = 1000)
        {
            var result = string.IsNullOrEmpty(draft) ? transcription.Trim() : draft + " " + transcription.Trim();
            if (result.Length <= maximum) return result;
            var length = maximum;
            if (length > 0 && char.IsHighSurrogate(result[length - 1])) length--;
            return result.Substring(0, length);
        }
    }
}
