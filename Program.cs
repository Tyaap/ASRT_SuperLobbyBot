namespace SLB
{
    static class Program
    {
        static void Main(string[] args)
        {
            Web.Run();
            Discord.Run();
            Steam.Run(); // Callback loop blocks the thread
        }
    }
}
