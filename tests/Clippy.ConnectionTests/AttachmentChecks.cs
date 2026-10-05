using System.Text.Json;
using Clippy.Core.Classes;
using Clippy.Core.Enums;
using Clippy.Core.Services;
using Clippy.Core.ViewModels;

internal static class AttachmentChecks
{
    public static async Task Run(Action<bool, string> assert)
    {
        var settings = new Settings();
        var server = new Server { Body = "data: {\"choices\":[{\"delta\":{\"content\":\"Вижу экран.\"}}]}\n\ndata: [DONE]\n" };
        using var http = new HttpClient(server);
        var chat = new ChatService(settings, new Keys(), http);
        var vm = new ClippyViewModel(chat, settings);
        assert(vm.Messages.Count == 1 && vm.Messages[0].Role == Role.System && vm.MessagesVM.Count == 0,
            "new chat has no greeting or visible system message");
        var jpeg = new byte[] { 255, 216, 255, 217 };
        vm.CurrentScreenshot = jpeg;
        await vm.SendPrompt(CancellationToken.None);
        using (var payload = JsonDocument.Parse(server.RequestBody!))
        {
            var content = payload.RootElement.GetProperty("messages")[1].GetProperty("content");
            assert(content[0].GetProperty("text").GetString()!.Length > 0 && content[1].GetProperty("image_url").GetProperty("url").GetString() ==
                "data:image/jpeg;base64," + Convert.ToBase64String(jpeg), "screenshot-only request supplies prompt and image without enabling automatic advice");
        }
        assert(vm.CurrentScreenshot == null && ((Message)vm.Messages[1]).ScreenshotJpeg == null && vm.MessagesVM[0].MessageText.Contains("📷"),
            "sent screenshot released from draft and history with visible attachment marker");
        vm.CurrentText = "Следующий вопрос";
        await vm.SendPrompt(CancellationToken.None);
        using (var payload = JsonDocument.Parse(server.RequestBody!))
            assert(payload.RootElement.GetProperty("messages").EnumerateArray().All(m => m.GetProperty("content").ValueKind == JsonValueKind.String),
                "screenshot not resent on subsequent chat messages");
        vm.CurrentText = "Объясни ошибку";
        vm.CurrentScreenshot = jpeg;
        await vm.SendPrompt(CancellationToken.None);
        using (var payload = JsonDocument.Parse(server.RequestBody!))
            assert(payload.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content")[0].GetProperty("text").GetString() == "Объясни ошибку",
                "typed prompt preserved alongside screenshot");
        vm.CurrentScreenshot = jpeg;
        vm.IsCapturingScreenshot = true;
        var count = vm.Messages.Count;
        await vm.SendPrompt(CancellationToken.None);
        assert(!vm.SendPromptCommand.CanExecute(null) && vm.Messages.Count == count, "send blocked while screenshot is being captured");
        vm.IsCapturingScreenshot = false;
        server.Status = System.Net.HttpStatusCode.BadRequest;
        await vm.SendPrompt(CancellationToken.None);
        assert(((Message)vm.Messages[vm.Messages.Count - 2]).ScreenshotJpeg == null, "screenshot released after failed request");
        vm.CurrentScreenshot = jpeg;
        vm.RefreshChatCommand.Execute(null);
        assert(vm.CurrentScreenshot == null && vm.MessagesVM.Count == 0 && vm.Messages.Count == 1 && vm.ConversationRevision == 1,
            "reset clears attachment without restoring greeting");
        Console.WriteLine("All attachment checks passed.");
    }
}
