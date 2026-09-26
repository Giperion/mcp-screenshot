using ScreenshotMcp.Hosting;

namespace ScreenshotMcp.Tests;

public sealed class HandshakeTests
{
    [Fact]
    public async Task Initialize_ReportsServerNameAndVersion()
    {
        await using var server = await InMemoryMcpServer.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ScreenshotMcpServer.Name, server.Client.ServerInfo.Name);
        Assert.Equal(ScreenshotMcpServer.Version, server.Client.ServerInfo.Version);
        Assert.DoesNotContain('+', ScreenshotMcpServer.Version);
    }
}
