using System;
using System.Threading.Tasks;

var tcs = new TaskCompletionSource();
var sigintReceived = false;

Console.CancelKeyPress += (_, ea) =>
{
    // Tell .NET to not terminate the process
    ea.Cancel = true;
    Console.WriteLine("Received SIGINT (Ctrl+C)");
    tcs.SetResult();
    sigintReceived = true;
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!sigintReceived)
    {
        Console.WriteLine("Received SIGTERM");
        tcs.SetResult();
    }

    SLB.Stats.SaveDataset();
    SLB.Steam.Stop();
    SLB.Discord.Stop();
};

Console.WriteLine("Super Lobby Bot: Hello!");

try
{
    SLB.Stats.LoadDatasets();
    await SLB.Discord.Start();
    SLB.Steam.Start();
}
catch (Exception ex)
{
    Console.WriteLine("Main() Exception! \n" + ex);
}

await tcs.Task;
Console.WriteLine("Super Lobby Bot: Goodbye!");