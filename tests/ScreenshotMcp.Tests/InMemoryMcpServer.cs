using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ScreenshotMcp.Hosting;

namespace ScreenshotMcp.Tests;

/// <summary>
/// Runs the real server registration over in-memory pipes and connects an MCP client to it.
/// </summary>
internal sealed class InMemoryMcpServer : IAsyncDisposable
{
    private readonly IHost _host;

    private InMemoryMcpServer(IHost host, McpClient client)
    {
        _host = host;
        Client = client;
    }

    public McpClient Client { get; }

    public static async Task<InMemoryMcpServer> StartAsync(CancellationToken cancellationToken)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Services
            .AddScreenshotMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        var host = builder.Build();
        await host.StartAsync(cancellationToken);

        var transport = new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        return new InMemoryMcpServer(host, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
