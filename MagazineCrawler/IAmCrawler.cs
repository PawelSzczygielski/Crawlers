internal interface IAmCrawler
{
    string Name { get; }
    Task RunAsync(CancellationToken ct = default);
}
