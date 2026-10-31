using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace MissionEditor.Mcp;

/// <summary>Local stdio MCP server; stdout is reserved for protocol messages.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        string? root = null;
        bool writes = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--workspace" when i + 1 < args.Length: root = args[++i]; break;
                    case "--allow-writes": writes = true; break;
                    case "--version": Console.Error.WriteLine(version); return 0;
                    case "--help": Console.Error.WriteLine("MissionEditor.Mcp --workspace <Missionsverzeichnis> [--allow-writes]\nStdio-MCP; standardmäßig nur Lesen und Entwürfe. Export nur als neue .ini-Datei."); return 0;
                    default: throw new ArgumentException($"Unbekannte oder unvollständige Option: {args[i]}");
                }
            }
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("--workspace <Missionsverzeichnis> muss explizit angegeben werden.");
            var workspace = new MissionWorkspace(root, writes);
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(workspace);
            builder.Services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "Freelancer Mission Editor", Version = version.Split('+')[0] })
                .WithStdioServerTransport().WithTools<MissionTools>();
            await builder.Build().RunAsync();
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
