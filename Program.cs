using System;
using System.Threading.Tasks;

namespace SLB
{
    static class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Super Lobby Bot: Hello!");

            await Web.Start();            
            Stats.Restore();
            await Discord.Start();
            Steam.Start();
            await Web.WaitForShutdown();
            Shutdown();

            Console.WriteLine("Super Lobby Bot: Goodbye!");
        }

        static void Shutdown()
        {
            Console.WriteLine("Program.Shutdown()");
            Stats.SaveDataset();
            Steam.Stop();
            Discord.Stop();
            Web.Stop();
        }
    }
}
