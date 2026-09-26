using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ScreenshotMcp.Hosting;

public static class ScreenshotMcpServer
{
    public const string Name = "screenshot";

    public static string Version { get; } =
        typeof(ScreenshotMcpServer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// Registers the MCP server and its tools. The caller picks the transport
    /// (stdio in the app, in-memory streams in tests).
    /// </summary>
    public static IMcpServerBuilder AddScreenshotMcpServer(this IServiceCollection services) =>
        services
            .AddMcpServer(options => options.ServerInfo = new Implementation { Name = Name, Version = Version })
            .WithToolsFromAssembly(typeof(ScreenshotMcpServer).Assembly);
}
