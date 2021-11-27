using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Timers;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace SLB
{
    static class Web
    {
        // environment variables
        static int ENV_PORT = int.Parse(Environment.GetEnvironmentVariable("PORT"));

        // constants
        const string HOST_ADDRESS = "https://super-lobby-bot.herokuapp.com/";
        const int PING_INTERVAL = 60000;

        // web components
        static IHost host;
        static HttpClient client;
        static Timer pingTimer;
        public static string message = "Super Lobby Bot is starting...";


        public static async Task Start()
        {
            Console.WriteLine("Web.Start()");
            host = CreateHostBuilder().Build();
            await host.StartAsync();
            client = new HttpClient();
            pingTimer = new Timer(PING_INTERVAL) { AutoReset = true };
            pingTimer.Elapsed += Ping;
            pingTimer.Start();
        }

        public static async Task WaitForShutdown()
        {
            await host.WaitForShutdownAsync();
        }

        public static void Stop()
        {
            Console.WriteLine("Web.Stop()");
            pingTimer?.Stop();
            pingTimer?.Dispose();
            pingTimer = null;
            client?.Dispose();
            host?.Dispose();
        }

        public static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
            .UseConsoleLifetime()
            .ConfigureLogging((logging) =>
            {
                // clear default logging providers
                logging.ClearProviders();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://*:" + ENV_PORT);
                webBuilder.UseStartup<Startup>();
            });

        public static void Ping(object caller, ElapsedEventArgs e)
        {
            try
            {
                client.GetStringAsync(HOST_ADDRESS);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Web.Ping() Exception!\n" + ex);
            }
        }
    }
}