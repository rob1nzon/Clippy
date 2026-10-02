using System.Text.Json;
using Clippy.Core.Services;

internal static class ScreenAdviceChecks
{
    public static async Task Run(Action<bool, string> assert)
    {
        var settings = new Settings { McpEnabled = true };
        var server = new Server { Body = "{\"choices\":[{\"message\":{\"content\":\"Используй поиск по документу.\"}}]}" };
        using var http = new HttpClient(server);
        var chat = new ChatService(settings, new Keys { Key = "vision-key" }, http);
        var jpeg = new byte[] { 255, 216, 255, 217 };
        try { await chat.AnalyzeScreenAsync(jpeg, CancellationToken.None); throw new Exception("Disabled vision sent a request."); }
        catch (InvalidOperationException) { assert(server.RequestBody == null, "disabled screen advice sends no image"); }
        settings.ScreenAdviceEnabled = true;
        assert(await chat.AnalyzeScreenAsync(jpeg, CancellationToken.None) == "Используй поиск по документу.", "vision response parsed");
        using (var request = JsonDocument.Parse(server.RequestBody!))
        {
            var root = request.RootElement;
            var messages = root.GetProperty("messages");
            assert(messages.GetArrayLength() == 2 && messages[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString() ==
                "data:image/jpeg;base64," + Convert.ToBase64String(jpeg), "screenshot uses OpenAI-compatible image content without chat history");
            assert(!root.TryGetProperty("tools", out _) && !root.GetProperty("stream").GetBoolean() && root.GetProperty("max_tokens").GetInt32() == 128,
                "screen advice has bounded tokens and no MCP tools even when MCP enabled");
            assert(server.Authorization == "Bearer vision-key", "vision uses configured model authentication");
        }
        try { await chat.AnalyzeScreenAsync(new byte[0], CancellationToken.None); throw new Exception("Empty screenshot accepted."); }
        catch (ArgumentException) { assert(true, "empty screenshot rejected"); }

        var time = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var captures = 0; var requests = 0; var fail = false;
        var answer = "Один короткий совет.";
        var controller = new ScreenAdviceController(settings, (_, _) =>
        {
            requests++;
            return fail ? Task.FromException<string>(new Exception("private server response")) : Task.FromResult(answer);
        }, () => time);
        Task<byte[]?> Capture(CancellationToken token) { captures++; return Task.FromResult<byte[]?>(jpeg); }
        await controller.RunAsync(Capture, true);
        assert(captures == 0, "opt-in waits a full interval before first automatic capture");
        await controller.RunAsync(Capture, false, true);
        assert(captures == 0, "hidden or busy UI blocks even a manual capture");
        time = time.AddMinutes(16);
        await controller.RunAsync(Capture, true);
        assert(captures == 1 && requests == 1 && controller.Tip == answer, "scheduled screen advice displayed");
        await controller.RunAsync(Capture, true);
        assert(captures == 1, "automatic capture rate limited");
        time = time.AddMinutes(16);
        await controller.RunAsync(Capture, true);
        assert(captures == 2 && requests == 1, "identical screenshot not resent automatically");
        controller.Dismiss();
        await controller.RunAsync(Capture, true, true);
        assert(requests == 2 && controller.Tip == "", "dismissed repeated advice does not reappear");
        answer = "SKIP";
        await controller.RunAsync(Capture, true, true);
        assert(controller.Tip == "", "SKIP suppresses unhelpful advice");
        answer = new string('я', 400);
        await controller.RunAsync(Capture, true, true);
        assert(controller.Tip.Length == 240, "advice bubble length bounded");
        fail = true;
        await controller.RunAsync(Capture, true, true);
        var beforeFailureRetry = captures;
        time = time.AddMinutes(16);
        await controller.RunAsync(Capture, true);
        assert(captures == beforeFailureRetry && !controller.Status.Contains("private server response"), "failed vision pauses automatic requests without leaking response bodies");
        fail = false; answer = "Другой совет.";
        await controller.RunAsync(Capture, true, true);
        assert(captures == beforeFailureRetry + 1 && controller.Tip == answer, "manual retry resumes advice after error");

        var pending = new TaskCompletionSource<byte[]?>();
        var running = controller.RunAsync(_ => pending.Task, true, true);
        var beforeOverlap = captures;
        await controller.RunAsync(Capture, true, true);
        assert(controller.IsRunning && captures == beforeOverlap, "overlapping captures suppressed");
        var beforeDisable = requests;
        settings.ScreenAdviceEnabled = false;
        controller.Reset();
        pending.SetResult(jpeg);
        await running;
        await controller.RunAsync(Capture, true, true);
        assert(requests == beforeDisable && !controller.IsRunning && controller.Tip == "", "disable cancels pending capture and clears advice before upload");

        settings.ScreenAdviceEnabled = true;
        var pendingResponse = new TaskCompletionSource<string>();
        var cancellationController = new ScreenAdviceController(settings, (_, _) => pendingResponse.Task, () => time);
        var inFlight = cancellationController.RunAsync(Capture, true, true);
        settings.ScreenAdviceEnabled = false;
        cancellationController.Reset();
        pendingResponse.SetResult("Late advice");
        await inFlight;
        assert(cancellationController.Tip == "", "late vision response discarded after opt-out");
        settings.ScreenAdviceEnabled = true;
        var lockedController = new ScreenAdviceController(settings, (_, _) => throw new Exception("Must not upload"), () => time);
        await lockedController.RunAsync(_ => Task.FromResult<byte[]?>(null), true, true);
        assert(lockedController.Status.StartsWith("Paused:") && lockedController.Tip == "", "unavailable desktop skips upload");
        Console.WriteLine("All screen advice checks passed.");
    }
}
