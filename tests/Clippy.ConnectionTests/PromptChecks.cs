using System.Text.Json;
using Clippy.Core;
using Clippy.Core.Enums;
using Clippy.Core.Services;
using Clippy.Core.ViewModels;

internal static class PromptChecks
{
    public static async Task Run(Action<bool, string> assert)
    {
        var settings = new Settings { SystemPrompt = "Отвечай кратко на русском." };
        var server = new Server { Body = "data: {\"choices\":[{\"delta\":{\"content\":\"Ответ\"}}]}\n\ndata: [DONE]\n" };
        using var http = new HttpClient(server);
        var vm = new ClippyViewModel(new ChatService(settings, new Keys(), http), settings);
        assert(vm.Messages[0].MessageText == settings.SystemPrompt && vm.MessagesVM.Count == 0, "custom system prompt initializes invisible instructions");
        vm.CurrentText = "Первый вопрос"; await vm.SendPrompt(CancellationToken.None);
        settings.SystemPrompt = "Отвечай подробно.\nС примерами.";
        vm.CurrentText = "Второй вопрос"; await vm.SendPrompt(CancellationToken.None);
        using (var payload = JsonDocument.Parse(server.RequestBody!))
        {
            var messages = payload.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            assert(messages.Count(m => m.GetProperty("role").GetString() == "system") == 1 &&
                messages[0].GetProperty("content").GetString() == settings.SystemPrompt, "saved multiline prompt replaces rather than duplicates instructions");
            assert(messages.Any(m => m.GetProperty("content").ValueKind == JsonValueKind.String && m.GetProperty("content").GetString() == "Первый вопрос"), "prompt changes preserve conversation history");
        }
        settings.SystemPrompt = " ";
        vm.CurrentText = "Без инструкции"; await vm.SendPrompt(CancellationToken.None);
        using (var payload = JsonDocument.Parse(server.RequestBody!))
            assert(!payload.RootElement.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("role").GetString() == "system"), "blank prompt omits the system message");
        settings.SystemPrompt = Constants.DEFAULT_SYSTEM_PROMPT;
        vm.RefreshChatCommand.Execute(null);
        assert(vm.Messages.Count == 1 && vm.Messages[0].MessageText == Constants.DEFAULT_SYSTEM_PROMPT && vm.MessagesVM.Count == 0, "reset chat uses current saved default without greeting");
        Console.WriteLine("All prompt checks passed.");
    }
}
