using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Net.Http;
using System;

namespace SLB
{
    static class Web
    {
        // Regularly ping the web API
        const string HOST_ADDRESS = "https://super-lobby-bot.herokuapp.com/";
        const int PING_INTERVAL = 60000;
        static Timer pingTimer;
        static HttpClient client;
        public static EventWaitHandle waitHandle;

        public static bool waitingForResponse;
        public static string message = "Super Lobby Bot is starting...";
        public static string response;

        public static void Run()
        {
            CreateHostBuilder().Build().RunAsync();
            client = new HttpClient();
            pingTimer = new Timer(Ping, null, PING_INTERVAL, PING_INTERVAL);
            waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        public static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
            .ConfigureLogging((logging) =>
                {
                    // clear default logging providers
                    logging.ClearProviders();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        public static void Ping(object state)
        {
            Console.WriteLine("Pinging web API...");
            client.GetStringAsync(HOST_ADDRESS);
        }

        public static string InputRequest(string webMessage)
        {
            Console.WriteLine("Waiting for web input: " + webMessage);
            // Update web message, wait for response.
            message = webMessage;
            waitingForResponse = true;
            waitHandle.WaitOne();
            Console.WriteLine("Recieved web input!");
            return response;
        }
    }
}