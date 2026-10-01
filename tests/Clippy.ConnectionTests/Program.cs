using System.Net;
using System.Text.Json;
using Clippy.Core.Classes;
using Clippy.Core.Enums;
using Clippy.Core.Interfaces;
using Clippy.Core.Services;
using Clippy.Core.ViewModels;
using Clippy.Services;

var settings = new Settings();
var keys = new Keys();
var handler = new Server();
using var client = new HttpClient(handler);
var chat = new ChatService(settings, keys, client);
IMessage[] messages = [new Message(Role.System, "Help"), new Message(Role.User, "Hi"), new Message(Role.Assistant, "")];

void Assert(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception("FAIL: " + name);
}

handler.Body = "{\"choices\":[{\"message\":{\"content\":\"Привет\"}}]}";
Assert(await chat.SendChatAsync(messages) == "Привет", "non-streaming Unicode response");
Assert(handler.Url == "http://192.168.1.10:8080/v1/chat/completions", "base URL gets /v1");
Assert(handler.Authorization == null, "keyless server");
using (var payload = JsonDocument.Parse(handler.RequestBody!))
{
    Assert(payload.RootElement.GetProperty("messages").GetArrayLength() == 2, "empty assistant placeholder excluded");
    Assert(payload.RootElement.GetProperty("model").GetString() == "local-model" &&
        payload.RootElement.GetProperty("max_tokens").GetInt32() == 512, "model and token settings");
}
settings.ServerUrl += "/v1/";
keys.Key = "secret";
handler.Body = ": heartbeat\n\ndata: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n" +
    "data: {\"choices\":[{\"delta\":{\"content\":\"При\"}}]}\n\n" +
    "data: {\"choices\":[{\"delta\":{\"content\":\"вет\"}}]}\n\n" +
    "data: {\"choices\":[]}\n\ndata: [DONE]\n\ndata: invalid\n";
var chunks = "";
await foreach (var chunk in chat.StreamChatAsync(messages)) chunks += chunk;
Assert(chunks == "Привет", "SSE deltas, heartbeat, empty choices and DONE");
Assert(handler.Url == "http://192.168.1.10:8080/v1/chat/completions", "no duplicate /v1");
Assert(handler.Authorization == "Bearer secret", "optional bearer key");
settings.Model = "another-model";
await foreach (var chunk in chat.StreamChatAsync(messages)) { }
Assert(handler.RequestBody!.Contains("another-model"), "saved settings apply to next request");

handler.Status = HttpStatusCode.Unauthorized;
await Throws<HttpRequestException>(() => chat.SendChatAsync(messages), "HTTP errors surface");
handler.Status = HttpStatusCode.OK;
handler.Body = "data: {\"error\":{\"message\":\"model unavailable\"}}\n\n";
await Throws<HttpRequestException>(async () => { await foreach (var chunk in chat.StreamChatAsync(messages)) { } }, "stream errors surface");
settings.ServerUrl = "file:///tmp/model";
await Throws<InvalidOperationException>(() => chat.SendChatAsync(messages), "invalid URL rejected");
settings.ServerUrl = "http://localhost:8080/v1";
settings.Model = " ";
await Throws<InvalidOperationException>(() => chat.SendChatAsync(messages), "empty model rejected");
settings.Model = "local-model";
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    await Throws<OperationCanceledException>(async () =>
    {
        await foreach (var chunk in chat.StreamChatAsync(messages, cancelled.Token)) { }
    }, "cancelled requests stop");
}

handler.Body = "data: {\"choices\":[{\"delta\":{\"content\":\"Answer\"}}]}\n\ndata: [DONE]\n";
var vm = new ClippyViewModel(chat, settings) { CurrentText = "Question" };
await vm.SendPrompt(CancellationToken.None);
Assert(vm.Messages.Last().MessageText == "Answer", "completed response retained in chat history");
handler.Status = HttpStatusCode.BadRequest;
vm.CurrentText = "Next";
await vm.SendPrompt(CancellationToken.None);
Assert(vm.MessagesVM.Last().MessageText.Contains("Connection error"), "connection error visible in chat");
Assert(vm.Messages.Last().MessageText == "", "connection errors excluded from model history");

handler.Status = HttpStatusCode.OK;
handler.Stream = new StalledStream();
using (var cancelled = new CancellationTokenSource())
{
    var read = Task.Run(async () =>
    {
        await foreach (var chunk in chat.StreamChatAsync(messages, cancelled.Token)) { }
    });
    await handler.Stream.Reading.Task.WaitAsync(TimeSpan.FromSeconds(3));
    cancelled.Cancel();
    await Throws<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)), "cancel interrupts stalled server read");
}
handler.Stream = null;
var tempDir = Path.Combine(Path.GetTempPath(), "Clippy-tests-" + Guid.NewGuid());
Directory.CreateDirectory(tempDir);
try
{
    var path = Path.Combine(tempDir, "settings.json");
    var store = new SettingsStore(path);
    Assert((string)store.Get("ServerUrl", "default") == "default", "first launch settings defaults");
    store.Values["ServerUrl"] = "http://192.168.1.20:8080/v1";
    store.Values["Tokens"] = 1024;
    store.Values["AutoPin"] = false;
    store.Save();
    var reloaded = new SettingsStore(path);
    Assert((string)reloaded.Get("ServerUrl", "") == "http://192.168.1.20:8080/v1" &&
        (int)reloaded.Get("Tokens", 512) == 1024 && !(bool)reloaded.Get("AutoPin", true), "settings survive restart");
    File.WriteAllText(path, "{\"Tokens\":\"wrong type\"}");
    Assert((int)new SettingsStore(path).Get("Tokens", 512) == 512, "invalid settings type falls back");
    File.WriteAllText(path, "invalid json");
    Assert((int)new SettingsStore(path).Get("Tokens", 512) == 512, "malformed settings fall back");
}
finally { Directory.Delete(tempDir, true); }
Console.WriteLine("All connection and settings checks passed.");

sealed class Settings : ISettingsService
{
    public bool AutoPin { get; set; }
    public bool TrayClippy { get; set; }
    public bool TranslucentBackground { get; set; }
    public bool KeyboardEnabled { get; set; }
    public int Tokens { get; set; } = 512;
    public string ServerUrl { get; set; } = "http://192.168.1.10:8080";
    public string Model { get; set; } = "local-model";
}

sealed class Keys : IKeyService
{
    public string Key = "";
    public string GetKey() => Key;
    public void SetKey(string key) => Key = key;
}

sealed class Server : HttpMessageHandler
{
    public string Body = "";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string? Url, Authorization, RequestBody;
    public StalledStream? Stream;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Url = request.RequestUri!.ToString();
        Authorization = request.Headers.Authorization?.ToString();
        RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status) { Content = Stream == null ? new StringContent(Body) : new StreamContent(Stream) };
    }
}

sealed class StalledStream : Stream
{
    public TaskCompletionSource<bool> Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Reading.TrySetResult(true);
        return pending.Task;
    }
    protected override void Dispose(bool disposing)
    {
        pending.TrySetException(new ObjectDisposedException(nameof(StalledStream)));
        base.Dispose(disposing);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
