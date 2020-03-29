using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Net.Http;
using System;

namespace SLB
{
    static class Web
    {
        // Regularly ping the web API
        const string HOST_ADDRESS = "http://super-lobby-bot.herokuapp.com/"; // "http://localhost:5000/";
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
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        public static void Ping(object state) 
        {
            Console.WriteLine("Pinging web API...");
            client.GetStringAsync(HOST_ADDRESS);
            Console.WriteLine("Pinged web API!");
        }

        public static string InputRequest(string webMessage)
        {
            Console.WriteLine("Waiting for web input: " + webMessage);
            // Update web message, wait for response.
            message = webMessage;
            waitingForResponse = true;
            waitHandle.WaitOne();
            Console.WriteLine("Recieved web input!");
            waitingForResponse = false;
            return response;
        }
    }
}