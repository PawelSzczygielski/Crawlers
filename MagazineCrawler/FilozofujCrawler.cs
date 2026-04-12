using HtmlAgilityPack;

internal class FilozofujCrawler : IAmCrawler
{
    private const string BaseUrl = "https://filozofuj.eu";
    private const string IndexUrl = $"{BaseUrl}/wydania/";
    private const string OutputDir = "filozofuj_pdfs";
    private const int Parallelism = 4;

    public string Name => "Filozofuj!";

    public async Task RunAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(OutputDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MagazineCrawler/1.0)");
        http.Timeout = TimeSpan.FromSeconds(60);

        Console.WriteLine($"Pobieranie listy numerów: {IndexUrl}");
        var indexHtml = await http.GetStringAsync(IndexUrl, ct);

        var indexDoc = new HtmlDocument();
        indexDoc.LoadHtml(indexHtml);

        var issueUrls = indexDoc.DocumentNode
            .SelectNodes("//h2//a[@href]")
            ?.Select(a => a.GetAttributeValue("href", ""))
            .Where(href => href.StartsWith(BaseUrl + "/")
                        && !href.Contains("/category/")
                        && !href.Contains("/tag/")
                        && !href.Contains("/produkt/")
                        && !href.Contains("/kategoria-produktu/"))
            .Distinct()
            .Order()
            .ToList() ?? [];

        Console.WriteLine($"Znaleziono {issueUrls.Count} numerów.\n");

        var semaphore = new SemaphoreSlim(Parallelism);
        int downloaded = 0, skipped = 0, errors = 0;

        var tasks = issueUrls.Select(async issueUrl =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var issueHtml = await WithRetry(() => http.GetStringAsync(issueUrl, ct), issueUrl, ct: ct);
                var issueDoc = new HtmlDocument();
                issueDoc.LoadHtml(issueHtml);

                var pdfHref = issueDoc.DocumentNode
                    .SelectNodes("//a[@href]")
                    ?.Select(a => a.GetAttributeValue("href", ""))
                    .FirstOrDefault(href => href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                                         && href.Contains("filozofuj.eu/wp-content/"));

                if (pdfHref is null)
                {
                    Console.WriteLine($"[BRAK PDF]  {issueUrl}");
                    Interlocked.Increment(ref skipped);
                    return;
                }

                var fileName = Path.GetFileName(new Uri(pdfHref).LocalPath);
                var filePath = Path.Combine(OutputDir, fileName);

                if (File.Exists(filePath))
                {
                    Console.WriteLine($"[ISTNIEJE]  {fileName}");
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Console.WriteLine($"[POBIERANIE] {pdfHref}");
                var bytes = await WithRetry(() => http.GetByteArrayAsync(pdfHref, ct), fileName, ct: ct);
                await File.WriteAllBytesAsync(filePath, bytes, ct);
                Console.WriteLine($"[GOTOWE]    {fileName} ({bytes.Length / 1024} KB)");
                Interlocked.Increment(ref downloaded);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BŁĄD]      {issueUrl} => {ex.Message}");
                Interlocked.Increment(ref errors);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine($"\nGotowe! Pobrano: {downloaded}, Pominięto: {skipped}, Błędy: {errors}");
        Console.WriteLine($"PDFy zapisano w: {Path.GetFullPath(OutputDir)}");
    }

    private static async Task<T> WithRetry<T>(Func<Task<T>> action, string label, int maxRetries = 3, CancellationToken ct = default)
    {
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt < maxRetries)
                {
                    int delaySec = attempt * 3;
                    Console.WriteLine($"[RETRY {attempt}/{maxRetries}] {label} – {ex.Message} (czekam {delaySec}s)");
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
                }
            }
        }
        throw lastEx!;
    }
}
