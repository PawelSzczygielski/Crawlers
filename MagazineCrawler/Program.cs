using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

CrawlerSettings Settings(string section, string defaultOutputDir)
{
    var outputDir = config[$"Crawlers:{section}:OutputDir"];
    return new CrawlerSettings(OutputDir: outputDir ?? defaultOutputDir);
}

IAmCrawler[] crawlers =
[
    new DeltaCrawler(Settings("Delta", "delta_pdfs")),
    new FilozofujCrawler(Settings("Filozofuj", "filozofuj_pdfs")),
    new SmokopolitanCrawler(Settings("Smokopolitan", "smokopolitan_pdfs")),
];

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
