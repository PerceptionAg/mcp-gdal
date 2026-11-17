using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using InFarm.Mcp.Gdal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Standard output carries the MCP protocol, so help and version text go to standard error.
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine(CommandLine.HelpText);
    return 0;
}

if (args.Contains("--version"))
{
    Console.Error.WriteLine(ServerHost.Version);
    return 0;
}

try
{
    using var host = ServerHost.Build(args);
    await host.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or FormatException)
{
    Console.Error.WriteLine($"infarm-mcp-gdal could not start: {ex.Message}");
    return 1;
}

namespace InFarm.Mcp.Gdal
{
    public static class ServerHost
    {
        public static string Version { get; } =
            typeof(ServerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        // Enumerations travel as snake_case strings, and non-finite numbers do not abort serialisation.
        public static JsonSerializerOptions SerializerOptions { get; } = new(McpJsonUtilities.DefaultOptions)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        public static IHost Build(string[] args)
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
            builder.Configuration
                .AddEnvironmentVariables(CommandLine.EnvironmentPrefix)
                .AddCommandLine(CommandLine.Normalize(args), CommandLine.SwitchMappings.ToDictionary());

            var settings = builder.Configuration.Get<GdalServerOptions>() ?? new GdalServerOptions();
            builder.Logging.SetMinimumLevel(settings.LogLevel);
            builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

            AddGdalServices(builder.Services, builder.Configuration);
            builder.Services
                .AddMcpServer(server =>
                {
                    server.ServerInfo = new() { Name = "infarm-mcp-gdal", Title = "InFarm GDAL MCP Server", Version = Version };
                    server.ServerInstructions = Instructions;
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly(typeof(ServerHost).Assembly, SerializerOptions);

            var host = builder.Build();

            // GDAL is initialised before the transport starts so that a broken installation fails at startup.
            host.Services.GetRequiredService<GdalRuntime>();
            return host;
        }

        public static IServiceCollection AddGdalServices(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<GdalServerOptions>(configuration);
            services.AddSingleton<GdalRuntime>();
            services.AddSingleton<PathPolicy>();
            services.AddSingleton<GdalRunner>();
            services.AddSingleton<GdalToolContext>();
            return services;
        }

        private const string Instructions = """
            This server exposes GDAL and OGR. Dataset paths are absolute local paths or GDAL virtual paths such as /vsimem/, /vsizip/, /vsicurl/ and /vsis3/.
            Inspect a dataset with gdal_dataset_info, gdal_raster_info or gdal_vector_info before processing it.
            Processing tools write a new output and refuse to replace an existing one unless overwrite=true is passed.
            Use gdal_driver_info to see the creation options of a format, and gdal_algorithm_list, gdal_algorithm_describe and gdal_algorithm_run
            for any operation of the unified 'gdal' command that has no dedicated tool.
            Coordinates use x=longitude/easting and y=latitude/northing unless a tool parameter selects authority axis order.
            """;
    }
}
