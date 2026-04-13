using HtmlAgilityPack;
using System.Text.RegularExpressions;

internal class SmokopolitanCrawler : IAmCrawler
{
    private const string ArchiveUrl = "https://smokopolitan.pl/archiwum/";
    private const int Parallelism = 3;

    private readonly string _outputDir;

    internal SmokopolitanCrawler(CrawlerSettings settings) => _outputDir = settings.OutputDir;

    public string Name => "Smokopolitan";

    public async Task RunAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MagazineCrawler/1.0)");
        http.Timeout = TimeSpan.FromSeconds(60);

        Console.WriteLine($"Pobieranie archiwum: {ArchiveUrl}");
        var html = await http.GetStringAsync(ArchiveUrl, ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Links like: <a href="/download-attachment/4580">Smokopolitan 11 PDF</a>
        var pdfLinks = doc.DocumentNode
            .SelectNodes("//a[contains(@href, '/download-attachment/') and contains(normalize-space(.), 'PDF')]")
            ?.Select(a => (
                Url: a.GetAttributeValue("href", ""),
                Label: a.InnerText.Trim()))
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .DistinctBy(x => x.Url)
            .ToList() ?? [];

        Console.WriteLine($"Znaleziono {pdfLinks.Count} plików PDF.\n");

        var semaphore = new SemaphoreSlim(Parallelism);
        int downloaded = 0, skipped = 0, errors = 0;

        var tasks = pdfLinks.Select(async link =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var fileName = BuildFileName(link.Label);
                var filePath = Path.Combine(_outputDir, fileName);

                if (File.Exists(filePath))
                {
                    Console.WriteLine($"[ISTNIEJE]  {fileName}");
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Console.WriteLine($"[POBIERANIE] {link.Url}  → {fileName}");
                var bytes = await WithRetry(() => http.GetByteArrayAsync(link.Url, ct), fileName, ct: ct);
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
                Console.WriteLine($"[BŁĄD]      {link.Label} => {ex.Message}");
                Interlocked.Increment(ref errors);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine($"\nGotowe! Pobrano: {downloaded}, Pominięto: {skipped}, Błędy: {errors}");
        Console.WriteLine($"PDFy zapisano w: {Path.GetFullPath(_outputDir)}");
    }

    // "Smokopolitan 11 PDF" → "Smokopolitan_11.pdf"
    private static string BuildFileName(string label)
    {
        var match = Regex.Match(label, @"\d+");
        return match.Success
            ? $"Smokopolitan_{int.Parse(match.Value):D2}.pdf"
            : SanitizeFileName(label) + ".pdf";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c))
                     .Replace(' ', '_')
                     .Trim('_', '.');
    }

    private static async Task<T> WithRetry<T>(Func<Task<T>> action, string label, int maxRetries = 3, CancellationToken ct = default)
    {
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (OperationCanceledException) { throw; }
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
