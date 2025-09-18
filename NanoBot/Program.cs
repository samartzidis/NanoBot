using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Text;
using NanoBot.Configuration;
using NanoBot.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NanoBot.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server.Features;
using NanoBot.Util;

namespace NanoBot;

public class Program
{
    public static async Task Main(string[] args)
    {            
        Console.OutputEncoding = Encoding.UTF8;

        var host = Host.CreateDefaultBuilder(args)
            .UseSerilog((context, configuration) =>
                configuration.ReadFrom.Configuration(context.Configuration)
                    .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.ffffff} [{Level}] [{SourceContext}] {Message}{NewLine:l}{Exception:l}")
                    .WriteTo.File(
                        path: "log.txt", // Rolling file naming
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.ffffff} [{Level}] {Message}{NewLine:l}{Exception:l}",
                        fileSizeLimitBytes: 1 * 1024 * 1024, // 1MB
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: 3,
                        shared: true)
                    .Enrich.FromLogContext())
            .ConfigureHostConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true);
                configurationBuilder.AddYamlFile("appsettings.local.yaml", optional: true, reloadOnChange: true);
                configurationBuilder.AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true);                
            })
            .ConfigureAppConfiguration(configurationBuilder =>
            {

            })
            .ConfigureServices((context, services) =>
            {
                // Register ISystemService as background service
                services.AddSingleton<ISystemService, SystemService>();
                services.AddHostedService<ISystemService>(provider => provider.GetRequiredService<ISystemService>());

                // Register more application services
                services.AddSingleton(typeof(IDynamicOptions<>), typeof(DynamicOptions<>));
                services.AddSingleton<IAgentFactoryService, AgentFactoryService>();
                services.AddSingleton<IVoiceService, VoiceService>();
                services.AddSingleton<IAlsaControllerService, AlsaControllerService>();
                services.AddSingleton<IEventBus, EventBus>();
                services.AddSingleton<IValidateOptions<AppConfig>, AppConfigValidator>(); // Add automatic configuration validation                

                // Bind AppConfig section to a strongly typed class
                services.Configure<AppConfig>(context.Configuration);               

                // Enable selected drivers                

                services.AddSingleton<IPowerConfDeviceService, PowerConfDeviceService>();
                services.AddHostedService<IPowerConfDeviceService>(provider => provider.GetRequiredService<IPowerConfDeviceService>());         
                
                services.AddSingleton<IGpioDeviceService, GpioDeviceService>();
                services.AddHostedService<IGpioDeviceService>(provider => provider.GetRequiredService<IGpioDeviceService>());
                               
                services
                    .AddControllers()
                    .AddJsonOptions(options =>
                    {
                        // Serialize enums as strings
                        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                    })
                    .AddApplicationPart(typeof(Program).Assembly);

                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                    {
                        Title = "NanoBot Web API",
                        Version = "v1"
                    });
                });
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                });

                webBuilder.Configure((ctx, app) =>
                {
                    app.UseRouting();                    
                    app.UseDefaultFiles(); // Enable default files (e.g., index.html)
                    app.UseStaticFiles(); // Serve static files from wwwroot

                    app.UseCors(builder => {
                        builder
                            .AllowAnyOrigin() // Allows any origin to connect
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });

                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.DisplayRequestDuration();
                        c.EnableTryItOutByDefault();
                    });

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();

                        // SPA fallback
                        endpoints.MapFallbackToFile("index.html");
                    });
                });
            })
            .Build();

        // Register shutdown handling
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var bus = host.Services.GetRequiredService<IEventBus>();
        lifetime.ApplicationStopping.Register(() =>
        {
            logger.LogInformation("Application is stopping...");
            bus.Publish<ShutdownEvent>(null);

        });
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            logger.LogInformation("Ctrl+C pressed. Shutting down...");
            bus.Publish<ShutdownEvent>(null);

            eventArgs.Cancel = true; // Prevent the process from terminating immediately
            lifetime.StopApplication(); // Signal graceful shutdown
        };

        // Start the host
        await host.StartAsync();            

        // Show Kestrel endpoint info
        var serverAddressesFeature = host.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>();
        if (serverAddressesFeature != null)
        {
            var listenAddresses = string.Join(", ", serverAddressesFeature.Addresses);
            logger.LogInformation($"Listening on: {@listenAddresses}");
        }

        // Wait for host to exit
        await host.WaitForShutdownAsync();
    }
}
