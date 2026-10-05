using System.Net;
using System.Text.Json;
using Clippy.Core.Classes;
using Clippy.Core.Enums;
using Clippy.Core.Interfaces;
using Clippy.Core.Services;
using Clippy.Core.ViewModels;
using Clippy.Services;
using System.Text;

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
    Assert(!(bool)store.Get("ScreenAdviceEnabled", false) && (int)store.Get("ScreenAdviceIntervalMinutes", 15) == 15,
        "screen advice defaults off with fifteen minute interval");
    store.Values["ScreenAdviceEnabled"] = true;
    store.Values["ScreenAdviceIntervalMinutes"] = 30;
    store.Values["ServerUrl"] = "http://192.168.1.20:8080/v1";
    store.Values["Tokens"] = 1024;
    store.Values["AutoPin"] = false;
    store.Save();
    var reloaded = new SettingsStore(path);
    Assert((bool)reloaded.Get("ScreenAdviceEnabled", false) && (int)reloaded.Get("ScreenAdviceIntervalMinutes", 15) == 30,
        "screen advice consent and interval survive restart");
    Assert((string)reloaded.Get("ServerUrl", "") == "http://192.168.1.20:8080/v1" &&
        (int)reloaded.Get("Tokens", 512) == 1024 && !(bool)reloaded.Get("AutoPin", true), "settings survive restart");
    File.WriteAllText(path, "{\"Tokens\":\"wrong type\"}");
    Assert((int)new SettingsStore(path).Get("Tokens", 512) == 512, "invalid settings type falls back");
    File.WriteAllText(path, "invalid json");
    Assert((int)new SettingsStore(path).Get("Tokens", 512) == 512, "malformed settings fall back");
}
finally { Directory.Delete(tempDir, true); }
Assert(CornerPlacement.Calculate(0, 0, 1920, 1040, 1, 124, 116) == (1796, 924, 124, 116), "mascot meets bottom-right work area");
Assert(CornerPlacement.Calculate(0, 0, 1920, 1040, 1.5, 124, 116) == (1734, 866, 186, 174), "150% display scaling uses physical pixels once");
Assert(CornerPlacement.Calculate(48, 0, 1872, 1080, 1, 380, 1000) == (1540, 80, 380, 1000), "side taskbar excluded");
Assert(CornerPlacement.Calculate(0, 0, 1280, 720, 2, 380, 1000) == (520, 0, 760, 720), "chat height clamped on small displays");
Assert(CornerPlacement.Calculate(0, 40, 1280, 984, 1, 224, 216) == (1056, 808, 224, 216), "large mascot and top taskbar");
Console.WriteLine("All connection, settings and placement checks passed.");

var mcpSettings = new Settings { McpEnabled = true };
var mcpHandler = new McpServer();
using var mcpHttp = new HttpClient(mcpHandler);
await using var mcp = new McpToolService(mcpSettings, () => "mcp-secret", mcpHttp);
var discovered = await mcp.ListToolsAsync();
Assert(discovered.Count == 1 && discovered[0].Name == "echo", "MCP initialization and tools discovery");
Assert(mcpHandler.SawSession && mcpHandler.SawProtocol && mcpHandler.SawToken, "MCP session, protocol and independent bearer token");
Assert(await mcp.CallToolAsync("echo", "{\"text\":\"hello\"}") == "echo: hello", "MCP tool result returned");
await Throws<InvalidOperationException>(() => mcp.CallToolAsync("unknown", "{}"), "unlisted MCP tool rejected");

var toolResponse = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_123\",\"function\":{\"name\":\"ec\",\"arguments\":\"{\\\"text\\\":\"}}]}}]}\n\n" +
    "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"ho\",\"arguments\":\"\\\"hello\\\"}\"}}]}}]}\n\ndata: [DONE]\n";
var answerResponse = "data: {\"choices\":[{\"delta\":{\"content\":\"Tool answer\"}}]}\n\ndata: [DONE]\n";
var modelServer = new Server();
using var modelHttp = new HttpClient(modelServer);
var toolChat = new ChatService(mcpSettings, new Keys(), modelHttp, mcp);
modelServer.Responses.Enqueue(toolResponse);
modelServer.Responses.Enqueue(answerResponse);
var approved = false;
toolChat.ApproveToolCall = (name, args, ct) =>
{
    approved = name == "echo" && args == "{\"text\":\"hello\"}";
    return Task.FromResult(true);
};
Assert(await toolChat.SendChatAsync(messages) == "Tool answer" && approved, "fragmented model tool call requests approval");
using (var payload = JsonDocument.Parse(modelServer.RequestBody!))
{
    var wireMessages = payload.RootElement.GetProperty("messages");
    var last = wireMessages[wireMessages.GetArrayLength() - 1];
    Assert(last.GetProperty("role").GetString() == "tool" && last.GetProperty("tool_call_id").GetString() == "call_123" &&
        last.GetProperty("content").GetString() == "echo: hello", "MCP result fed back to model with matching call ID");
    Assert(payload.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString() == "echo", "MCP schemas sent to llama.cpp");
}
var callsBefore = mcpHandler.ToolCalls;
modelServer.Responses.Enqueue(toolResponse);
modelServer.Responses.Enqueue(answerResponse);
toolChat.ApproveToolCall = (_, _, _) => Task.FromResult(false);
await toolChat.SendChatAsync(messages);
Assert(mcpHandler.ToolCalls == callsBefore && modelServer.RequestBody!.Contains("User declined"), "declined tool never executes");
mcpSettings.McpEnabled = false;
Assert((await mcp.ListToolsAsync()).Count == 0, "disabled MCP has no tools");
await Throws<InvalidOperationException>(() => mcp.CallToolAsync("echo", "{}"), "disabled MCP cannot execute tools");
Console.WriteLine("All MCP checks passed.");
await ScreenAdviceChecks.Run(Assert);
await AttachmentChecks.Run(Assert);
if (OperatingSystem.IsWindows()) WindowRegionChecks.Run(Assert);

sealed class Settings : ISettingsService
{
    public bool AutoPin { get; set; }
    public bool TrayClippy { get; set; }
    public bool TranslucentBackground { get; set; }
    public bool KeyboardEnabled { get; set; }
    public int Tokens { get; set; } = 512;
    public int ClippySize { get; set; } = 100;
    public bool McpEnabled { get; set; }
    public string McpServerUrl { get; set; } = "http://localhost:3001/mcp";
    public string ServerUrl { get; set; } = "http://192.168.1.10:8080";
    public string Model { get; set; } = "local-model";
    public bool ScreenAdviceEnabled { get; set; }
    public int ScreenAdviceIntervalMinutes { get; set; } = 15;
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
    public Queue<string> Responses = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Url = request.RequestUri!.ToString();
        Authorization = request.Headers.Authorization?.ToString();
        RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var body = Responses.Count > 0 ? Responses.Dequeue() : Body;
        return new HttpResponseMessage(Status) { Content = Stream == null ? new StringContent(body) : new StreamContent(Stream) };
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

sealed class McpServer : HttpMessageHandler
{
    public bool SawSession, SawProtocol, SawToken;
    public int ToolCalls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
        SawToken |= request.Headers.Authorization?.ToString() == "Bearer mcp-secret";
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var method = root.GetProperty("method").GetString();
        if (!root.TryGetProperty("id", out var id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
        if (method != "initialize")
        {
            SawSession |= request.Headers.TryGetValues("Mcp-Session-Id", out var values) && values.Contains("test-session");
            SawProtocol |= request.Headers.Contains("Mcp-Protocol-Version");
        }
        object result;
        switch (method)
        {
            case "initialize":
                result = new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(),
                    capabilities = new { tools = new { } }, serverInfo = new { name = "test-mcp", version = "1.0" } };
                break;
            case "tools/list":
                result = new { tools = new[] { new { name = "echo", description = "Echo text",
                    inputSchema = new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" } } } } };
                break;
            case "tools/call":
                ToolCalls++;
                result = new { content = new[] { new { type = "text", text = "echo: " + root.GetProperty("params").GetProperty("arguments").GetProperty("text").GetString() } } };
                break;
            default: throw new Exception("Unexpected MCP method " + method);
        }
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json")
        };
        if (method == "initialize") response.Headers.Add("Mcp-Session-Id", "test-session");
        return response;
    }
}
