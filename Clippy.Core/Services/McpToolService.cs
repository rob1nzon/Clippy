using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public sealed class McpToolService : IToolService, IAsyncDisposable
    {
        private readonly ISettingsService settings;
        private readonly Func<string> getKey;
        private readonly HttpClient? httpClient;
        private readonly SemaphoreSlim gate = new(1, 1);
        private McpClient? client;
        private string connectedUrl = "", connectedKey = "";
        private IReadOnlyList<ToolDefinition> tools = Array.Empty<ToolDefinition>();

        public McpToolService(ISettingsService settings, Func<string>? getKey = null, HttpClient? httpClient = null)
        {
            this.settings = settings;
            this.getKey = getKey ?? (() => "");
            this.httpClient = httpClient;
        }

        public async Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (!settings.McpEnabled) return Array.Empty<ToolDefinition>();
                var url = settings.McpServerUrl.Trim();
                var key = getKey();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    throw new InvalidOperationException("Set the MCP server HTTP endpoint in Settings, for example http://192.168.1.10:3001/mcp.");
                if (client != null && (connectedUrl != url || connectedKey != key))
                {
                    await client.DisposeAsync();
                    client = null;
                }
                if (client == null)
                {
                    var options = new HttpClientTransportOptions
                    {
                        Endpoint = uri, Name = "Clippy MCP", EnableStandaloneGetStream = false,
                        AdditionalHeaders = string.IsNullOrWhiteSpace(key) ? null : new Dictionary<string, string> { ["Authorization"] = "Bearer " + key.Trim() }
                    };
                    var transport = httpClient == null ? new HttpClientTransport(options) : new HttpClientTransport(options, httpClient, ownsHttpClient: false);
                    client = await McpClient.CreateAsync(transport, new McpClientOptions
                    {
                        ProtocolVersion = "2025-11-25",
                        ClientInfo = new Implementation { Name = "Clippy", Version = "0.3.0" }
                    }, cancellationToken: cancellationToken);
                    connectedUrl = url;
                    connectedKey = key;
                }
                var discovered = await client.ListToolsAsync(cancellationToken: cancellationToken);
                tools = discovered.Select(t => new ToolDefinition(t.Name, t.Description ?? "", t.JsonSchema.Clone())).ToArray();
                return tools;
            }
            finally { gate.Release(); }
        }

        public async Task<string> CallToolAsync(string name, string arguments, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (!settings.McpEnabled || client == null || connectedUrl != settings.McpServerUrl.Trim() || connectedKey != getKey())
                    throw new InvalidOperationException("MCP connection changed. Send a new message to reconnect.");
                if (!tools.Any(t => t.Name == name)) throw new InvalidOperationException("Unknown MCP tool: " + name);
                var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments) ?? new();
                var result = await client.CallToolAsync(name, args, cancellationToken: cancellationToken);
                var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
                if (result.StructuredContent != null) text += "\n" + result.StructuredContent.Value.GetRawText();
                if (string.IsNullOrWhiteSpace(text)) text = JsonSerializer.Serialize(result.Content);
                return result.IsError == true ? "MCP tool error: " + text : text;
            }
            finally { gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await gate.WaitAsync();
            try { if (client != null) { await client.DisposeAsync(); client = null; } }
            finally { gate.Release(); }
        }
    }
}
