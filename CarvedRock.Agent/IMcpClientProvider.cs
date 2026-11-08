using ModelContextProtocol.Client;

namespace CarvedRock.Agent;

/// <summary>
/// Supplies the MCP client the agent uses for tools and prompts. Injecting this
/// (instead of calling <see cref="McpClientHelper"/> directly) lets callers swap
/// the tool server, e.g. for an in-memory server or a fake, without touching the
/// agent itself.
/// </summary>
public interface IMcpClientProvider
{
    Task<McpClient> GetClientAsync(CancellationToken cxl);
}

/// <summary>
/// Production implementation: anonymous or token-forwarding client depending on
/// whether the current request is authenticated (see <see cref="McpClientHelper"/>).
/// </summary>
public class McpClientProvider(IConfiguration config, IHttpContextAccessor httpCtxAccessor)
    : IMcpClientProvider
{
    public Task<McpClient> GetClientAsync(CancellationToken cxl) =>
        McpClientHelper.GetMcpClient(config, httpCtxAccessor, cxl);
}
