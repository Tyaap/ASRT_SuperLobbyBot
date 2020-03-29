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
        const string HOST_ADDRESS = "http://localhost:5000/"; // "http://super-lobby-bot.herokuapp.com:20/"
        const int PING_INTERVAL = 60000;
        static Timer pingTimer;
        static HttpClient client;

        public static void Run()
        {
            CreateHostBuilder().Build().RunAsync();
            client = new HttpClient();
            pingTimer = new Timer(Ping, null, PING_INTERVAL, PING_INTERVAL);
        }

        public static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        public static void Ping(object state) 
        {
            Console.Write("Pinging web API...");
            client.GetStringAsync(HOST_ADDRESS);
            Console.WriteLine("Done!");
        }
    }
}