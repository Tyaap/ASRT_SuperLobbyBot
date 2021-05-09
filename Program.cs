namespace SLB
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Login details given as arguments
            if (args.Length == 5)
            {
                Discord.token = args[0];
                Steam.user = args[1];
                Steam.pass = args[2];
                Steam.message_wait = int.Parse(args[3]) * 1000;
                Discord.allocatedMessageCount = int.Parse(args[4]);
            }

            Web.Run();
            Discord.Run();
            Steam.Run(); // Callback loop blocks the thread
        }
    }
}
