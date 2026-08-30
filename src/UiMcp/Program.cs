using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using UiMcp.Hosting;

namespace UiMcp;

internal static class Program
{
    private const string ServerInstructions =
        "Use ui_open to show or focus the shared window, ui_render to replace the display with a " +
        "catalog-constrained JSON tree, ui_status to inspect the current window state, and ui_close " +
        "to hide the window. ui_render accepts tree and data as either JSON objects or JSON strings.";

    /// <summary>
    /// Reported over MCP. Read off the assembly, never written as a literal here - the single
    /// source is &lt;Version&gt; in Directory.Build.props. A hardcoded version in Windows-mcp rotted
    /// through three releases, and a server that misreports its own version is precisely what makes
    /// a stale-bundle deploy invisible.
    /// </summary>
    internal static string ServerVersion { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var builder = Host.CreateApplicationBuilder(args);

        // STDOUT IS THE MCP TRANSPORT. Anything written there that is not a JSON-RPC frame corrupts
        // the protocol, and the failure surfaces as an unhelpful client-side parse error rather than
        // as "your log line broke it". Every provider is cleared and the console logger is pinned to
        // stderr for that reason. This is not a style preference; it is a correctness constraint.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Singleton: there is ONE shared surface. The whole point is that Starship, the Github agent
        // and EVO all draw to the same window rather than each getting a private one.
        builder.Services.AddSingleton<IUiSurface, UiSurface>();

        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new()
                {
                    Name = "ui-mcp",
                    Title = "ui-mcp",
                    Version = ServerVersion,
                    Description = "Hosts a native Windows window and renders catalog-constrained JSON UI trees.",
                    WebsiteUrl = "https://github.com/danielsimonjr/ui-mcp"
                };
                o.ServerInstructions = ServerInstructions;
            })
            .WithStdioServerTransport()
            // Discovers [McpServerTool] methods by source generator. Deliberately EMPTY at scaffold
            // time: this build exists to prove the host wiring serves a real MCP session before any
            // tool or window code depends on it. ui_open / ui_render / ui_status / ui_close land in
            // the tools task, after the catalog and validator are green.
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
        return 0;
    }
}
