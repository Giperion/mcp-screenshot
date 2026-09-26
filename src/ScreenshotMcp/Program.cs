using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenshotMcp.Hosting;

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
{
    Console.Error.WriteLine("screenshot MCP requires Windows 10 version 1809 or later.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log line goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

builder.Services.AddScreenshotMcpServer().WithStdioServerTransport();

await builder.Build().RunAsync();
return 0;
