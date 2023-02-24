using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace SLB
{
    static class Web
    {
        // environment variables
        private static int ENV_PORT => int.Parse(Environment.GetEnvironmentVariable("PORT"));

        // constants
        private const string HOST_ADDRESS = "https://super-lobby-bot.onrender.com/";
        private const int PING_INTERVAL = 60000;

        // web components
        private static IHost host;
        private static HttpClient client;
        private static Timer pingTimer;
        private static object pingTimerLock = new object();

        // message
        public static string message = "Super Lobby Bot is starting...";


        public static async Task Start()
        {
            Console.WriteLine("Web.Start()");
            
            host = CreateHostBuilder().Build();
            await host.StartAsync();

            client = new HttpClient();

            pingTimer = new Timer(Ping, null, PING_INTERVAL, -1);
        }

        public static async Task WaitForShutdown()
        {
            await host.WaitForShutdownAsync();
        }

        public static void Stop()
        {
            Console.WriteLine("Web.Stop()");

            lock (pingTimerLock)
            {
                pingTimer?.Dispose();
                pingTimer = null;
            }

            client?.Dispose();
            client = null;

            host?.Dispose();
            host = null;
        }

        private static IHostBuilder CreateHostBuilder() =>
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

        private static void Ping(object caller)
        {
            lock (pingTimerLock)
            {
                if (pingTimer == null)
                {
                    return; // timer disposed
                }

                try
                {
                    client.GetStringAsync(HOST_ADDRESS);
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Web.Ping() Exception!\n" + ex);
                }

                // reset timer
                pingTimer.Change(PING_INTERVAL, -1);
            }
        }
    }
}