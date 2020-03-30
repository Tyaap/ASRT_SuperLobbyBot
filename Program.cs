namespace SLB
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Login details given as arguments
            if (args.Length == 3)
            {
                Discord.token = args[0];
                Steam.user = args[1];
                Steam.pass = args[2];
            }

            Web.Run();
            Discord.Run();
            Steam.Run(); // Callback loop blocks the thread
        }
    }
}
