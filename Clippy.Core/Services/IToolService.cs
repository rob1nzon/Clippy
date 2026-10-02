using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public record ToolDefinition(string Name, string Description, JsonElement Parameters);

    public interface IToolService
    {
        Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default);
        Task<string> CallToolAsync(string name, string arguments, CancellationToken cancellationToken = default);
    }
}
