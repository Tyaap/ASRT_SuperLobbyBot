using System;
using System.Threading.Tasks;

namespace SLB
{
    static class Program
    {
        public static readonly TimeZoneInfo TIMEZONE = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        static async Task Main()
        {
            Console.WriteLine("Super Lobby Bot: Hello!");

            try
            {
                await Web.Start();     
                Stats.LoadDatasets();
                await Discord.Start();
                Steam.Start();
                await Web.WaitForShutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Main() Exception! \n" + ex);
            }
            finally
            {   
                Stats.SaveDataset();
                Steam.Stop();
                Discord.Stop();
                Web.Stop();
            }

            Console.WriteLine("Super Lobby Bot: Goodbye!");
        }
    }
}
