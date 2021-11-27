using System;
using System.Threading.Tasks;

namespace SLB
{
    static class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Super Lobby Bot: Hello!");

            try
            {
                await Web.Start();     
                Stats.Restore();
                await Discord.Start();
                Steam.Start();
                await Web.WaitForShutdown();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Main() Exception! \n" + ex);
            }
            finally
            {   
                await Shutdown();
            }

            Console.WriteLine("Super Lobby Bot: Goodbye!");
        }

        static async Task Shutdown()
        {
            Console.WriteLine("Program.Shutdown()");
            Stats.SaveDataset();
            Steam.Stop();
            await Discord.Stop();
            Web.Stop();
        }
    }
}
