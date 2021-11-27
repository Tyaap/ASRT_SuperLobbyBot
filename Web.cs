using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Timers;
using System.Threading.Tasks;
using System.Net.Http;
using System;
using System.Diagnostics;

namespace SLB
{
    static class Web
    {
        // environment variables
        private static int ENV_PORT => int.Parse(Environment.GetEnvironmentVariable("PORT"));

        // constants
        private const string HOST_ADDRESS = "https://super-lobby-bot.herokuapp.com/";
        private const int PING_INTERVAL = 60000;

        // web components
        private static IHost host;
        private static HttpClient client;
        private static Timer pingTimer;
        private static bool pingTimerStopped;

        // message
        public static string message = "Super Lobby Bot is starting...";


        public static async Task Start()
        {
            Console.WriteLine("Web.Start()");
            
            host = CreateHostBuilder().Build();
            await host.StartAsync();

            client = new HttpClient();

            pingTimer = new Timer(PING_INTERVAL) { AutoReset = true };
            pingTimer.Elapsed += Ping;
            pingTimerStopped = false;
            pingTimer.Start();
        }

        public static async Task WaitForShutdown()
        {
            await host.WaitForShutdownAsync();
        }

        public static void Stop()
        {
            Console.WriteLine("Web.Stop()");

            pingTimerStopped = true;
            pingTimer?.Stop();
            pingTimer?.Dispose();
            pingTimer = null;

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

        private static void Ping(object caller, ElapsedEventArgs e)
        {
            try
            {
                client.GetStringAsync(HOST_ADDRESS);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Web.Ping() Exception!\n" + ex);
            }

            // reset timer
            if (!pingTimerStopped)
            {
                pingTimer.Start();
            }
        }

        public static string MemoryStats()
        {
            Process process = Process.GetCurrentProcess();
            return 
                "Memory Stats\n" +
                "Working set: " + PrettifyByte(process.WorkingSet64) + "\n" +
                "Private memory: " + PrettifyByte(process.PrivateMemorySize64) + "\n" +
                "Virtual memory: " + PrettifyByte(process.VirtualMemorySize64) + "\n";

        }

        private static string PrettifyByte(long allocatedMemory)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (allocatedMemory >= 1024 && order < sizes.Length - 1)
            {
                order++;
                allocatedMemory = allocatedMemory / 1024;
            }
            return $"{allocatedMemory:0.##} {sizes[order]}";
        }
    }
}