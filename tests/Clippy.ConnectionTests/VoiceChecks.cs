using System.Net;
using System.Text;
using Clippy.Core.Services;
using Clippy.Core.ViewModels;

internal static class VoiceChecks
{
    public static async Task Run(Action<bool, string> assert)
    {
        using var pcm = new PcmRecordingBuffer();
        pcm.Append(new byte[] { 0, 0, 1, 0 }, 4);
        var wav = pcm.ToWave();
        assert(wav.Length == 48 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 4) == 40 &&
            BitConverter.ToInt16(wav, 22) == 1 && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt16(wav, 34) == 16 &&
            BitConverter.ToInt32(wav, 40) == 4, "microphone audio encoded as 16kHz mono 16-bit WAV");
        using var bounded = new PcmRecordingBuffer();
        assert(bounded.Append(new byte[PcmRecordingBuffer.MaximumBytes + 100], PcmRecordingBuffer.MaximumBytes + 100) &&
            bounded.ToWave().Length == PcmRecordingBuffer.MaximumBytes + 44, "PCM recording bounded at sixty seconds");
        var settings = new Settings();
        var handler = new SpeechServer();
        using var http = new HttpClient(handler);
        var keys = new Keys { Key = "speech-only-key" };
        var speech = new SpeechService(settings, keys, http);
        try { await speech.TranscribeAsync(wav, CancellationToken.None); throw new Exception("disabled STT uploaded audio"); }
        catch (InvalidOperationException) { assert(handler.Calls == 0, "disabled voice input sends no audio"); }
        settings.VoiceInputEnabled = true;
        settings.SpeechServerUrl = "http://192.168.1.20:8016/custom/asr";
        handler.Body = "{\"text\":\" Привет Скрепка \"}";
        assert(await speech.TranscribeAsync(wav, CancellationToken.None) == "Привет Скрепка", "Russian transcription parsed and trimmed");
        assert(handler.Url == settings.SpeechServerUrl && handler.Wav!.SequenceEqual(wav) && handler.Model == "gigaam" && handler.Format == "json" &&
            handler.MediaType == "audio/wav" && handler.FileName == "recording.wav", "full custom STT endpoint and multipart audio fields preserved");
        assert(handler.Authorization == "Bearer speech-only-key", "STT token independent from model and MCP tokens");
        keys.Key = "";
        await speech.TranscribeAsync(wav, CancellationToken.None);
        assert(handler.Authorization == null, "keyless GigaAM supported");
        var calls = handler.Calls;
        settings.SpeechServerUrl = "file:///tmp/audio";
        try { await speech.TranscribeAsync(wav, CancellationToken.None); throw new Exception("invalid endpoint accepted"); }
        catch (InvalidOperationException) { assert(handler.Calls == calls, "invalid STT endpoint rejected before upload"); }
        settings.SpeechServerUrl = "http://localhost:8000/v1/audio/transcriptions";
        handler.Status = HttpStatusCode.Unauthorized; handler.Body = "private server response";
        try { await speech.TranscribeAsync(wav, CancellationToken.None); throw new Exception("HTTP error swallowed"); }
        catch (HttpRequestException error) { assert(!error.Message.Contains("private server response"), "STT errors do not expose response bodies"); }
        handler.Status = HttpStatusCode.OK; handler.Body = "{\"result\":\"unsupported\"}";
        try { await speech.TranscribeAsync(wav, CancellationToken.None); throw new Exception("wrong JSON accepted"); }
        catch (InvalidOperationException) { assert(true, "STT requires a JSON text field"); }
        handler.Body = new string('x', 70000);
        try { await speech.TranscribeAsync(wav, CancellationToken.None); throw new Exception("unbounded response accepted"); }
        catch (InvalidOperationException) { assert(true, "STT response size bounded"); }

        var recorder = new FakeRecorder(wav);
        var uploads = 0; var inserted = "";
        var voice = new VoiceInputController(settings, recorder, (_, _) => { uploads++; return Task.FromResult("Новый текст"); });
        voice.Transcribed += text => inserted = text;
        settings.VoiceInputEnabled = false;
        await voice.StartAsync();
        assert(recorder.Starts == 0 && uploads == 0, "voice opt-out never opens microphone");
        settings.VoiceInputEnabled = true;
        var running = voice.StartAsync();
        await voice.StartAsync();
        assert(voice.IsRecording && voice.IsBusy && recorder.Starts == 1, "only one explicit recording starts");
        voice.StopRecording(); await running;
        assert(uploads == 1 && inserted == "Новый текст" && !voice.IsBusy && !voice.IsRecording, "stop transcribes once without sending chat");
        assert(recorder.LastResult!.All(b => b == 0), "audio bytes cleared after transcription");
        inserted = "";
        var cancelled = voice.StartAsync();
        voice.Cancel(); await cancelled;
        assert(uploads == 1 && inserted == "" && !voice.IsBusy, "cancel discards recording before upload");
        var changedEndpoint = voice.StartAsync();
        settings.SpeechServerUrl = "http://other-server/custom";
        voice.StopRecording(); await changedEndpoint;
        assert(uploads == 1, "endpoint change prevents upload to a new destination");
        var late = new TaskCompletionSource<string>();
        var lateRecorder = new FakeRecorder(wav);
        var lateVoice = new VoiceInputController(settings, lateRecorder, (_, _) => late.Task);
        lateVoice.Transcribed += text => inserted = text;
        var lateRun = lateVoice.StartAsync();
        lateVoice.StopRecording(); await Task.Yield();
        settings.VoiceInputEnabled = false;
        lateVoice.Cancel(); late.SetResult("Late response"); await lateRun;
        assert(inserted == "" && !lateVoice.IsBusy, "late transcript discarded after opt-out");
        assert(VoiceInputController.AppendToDraft("Существующий ввод", "Новый текст") == "Существующий ввод Новый текст" &&
            VoiceInputController.AppendToDraft(new string('я', 995), "много текста").Length == 1000, "dictation preserves draft and input length limit");
        var vm = new ClippyViewModel(new ChatService(settings, new Keys()), settings) { CurrentText = "Не отправлять", IsVoiceInputBusy = true };
        await vm.SendPrompt(CancellationToken.None);
        assert(!vm.SendPromptCommand.CanExecute(null) && vm.Messages.Count == 1 && vm.CurrentText == "Не отправлять", "chat send blocked during recording and transcription");
        Console.WriteLine("All voice input checks passed.");
    }

    private sealed class FakeRecorder(byte[] wav) : IVoiceRecorder
    {
        public int Starts;
        public byte[]? LastResult;
        private TaskCompletionSource<byte[]>? pending;
        public Task<byte[]> RecordAsync(CancellationToken token)
        {
            Starts++;
            var current = new TaskCompletionSource<byte[]>();
            pending = current;
            token.Register(() => current.TrySetCanceled(token));
            return current.Task;
        }
        public void Stop() { LastResult = wav.ToArray(); pending!.TrySetResult(LastResult); }
    }

    private sealed class SpeechServer : HttpMessageHandler
    {
        public int Calls;
        public string Body = "{\"text\":\"Привет\"}";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Url, Authorization, Model, Format, MediaType, FileName;
        public byte[]? Wav;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            Url = request.RequestUri!.ToString(); Authorization = request.Headers.Authorization?.ToString();
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                if (name == "file") { Wav = await part.ReadAsByteArrayAsync(token); MediaType = part.Headers.ContentType!.MediaType; FileName = part.Headers.ContentDisposition.FileName!.Trim('"'); }
                if (name == "model") Model = await part.ReadAsStringAsync(token);
                if (name == "response_format") Format = await part.ReadAsStringAsync(token);
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
