IAmCrawler[] crawlers = [new DeltaCrawler()];

string[] options = [..crawlers.Select(c => c.Name), "Nic, kończymy"];
var choice = ConsoleUi.ShowMenu("Co chcesz pobrać? (użyj strzałek + entera, ctrl+c przerywa pobieranie)", options);

if (choice < crawlers.Length)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\n[ANULOWANIE] Przerywanie pobierania. To może chwilę potrwać...");
    };

    try
    {
        await crawlers[choice].RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[ANULOWANO] Pobieranie zostało przerwane.");
    }
}
