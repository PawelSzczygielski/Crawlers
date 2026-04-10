using HtmlAgilityPack;

const string BaseUrl = "https://deltami.edu.pl";
const string IndexUrl = $"{BaseUrl}/numery/";
const string OutputDir = "pdfs";
const int Parallelism = 4;

Directory.CreateDirectory(OutputDir);

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DeltaCrawler/1.0)");
http.Timeout = TimeSpan.FromSeconds(60);

// 1. Fetch the page with the list of issues
Console.WriteLine($"Pobieranie listy numerów: {IndexUrl}");
var indexHtml = await http.GetStringAsync(IndexUrl);

// 2. Extract links to individual issues (pattern /YYYY/M/)
var indexDoc = new HtmlDocument();
indexDoc.LoadHtml(indexHtml);

var issueUrls = indexDoc.DocumentNode
    .SelectNodes("//a[@href]")
    ?.Select(a => a.GetAttributeValue("href", ""))
    .Where(href => System.Text.RegularExpressions.Regex.IsMatch(href, @"^https?://deltami\.edu\.pl/\d{4}/\d{1,2}/?$")
                || System.Text.RegularExpressions.Regex.IsMatch(href, @"^/\d{4}/\d{1,2}/?$"))
    .Select(href => href.StartsWith("http") ? href : BaseUrl + href)
    .Distinct()
    .Order()
    .ToList() ?? [];

Console.WriteLine($"Znaleziono {issueUrls.Count} numerów.\n");

static async Task<T> WithRetry<T>(Func<Task<T>> action, string label, int maxRetries = 3)
{
    Exception? lastEx = null;
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try { return await action(); }
        catch (Exception ex)
        {
            lastEx = ex;
            if (attempt < maxRetries)
            {
                int delaySec = attempt * 3;
                Console.WriteLine($"[RETRY {attempt}/{maxRetries}] {label} – {ex.Message} (czekam {delaySec}s)");
                await Task.Delay(TimeSpan.FromSeconds(delaySec));
            }
        }
    }
    throw lastEx!;
}

// 3. For each issue, find the PDF and download it in parallel
var semaphore = new SemaphoreSlim(Parallelism);
int downloaded = 0, skipped = 0, errors = 0;

var tasks = issueUrls.Select(async issueUrl =>
{
    await semaphore.WaitAsync();
    try
    {
        var issueHtml = await WithRetry(() => http.GetStringAsync(issueUrl), issueUrl);
        var issueDoc = new HtmlDocument();
        issueDoc.LoadHtml(issueHtml);

        var pdfHref = issueDoc.DocumentNode
            .SelectNodes("//a[@href]")
            ?.Select(a => a.GetAttributeValue("href", ""))
            .FirstOrDefault(href => href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        if (pdfHref is null)
        {
            Console.WriteLine($"[BRAK PDF]  {issueUrl}");
            Interlocked.Increment(ref skipped);
            return;
        }

        var pdfUrl = pdfHref.StartsWith("http") ? pdfHref : BaseUrl + pdfHref;
        var fileName = Path.GetFileName(new Uri(pdfUrl).LocalPath);
        var filePath = Path.Combine(OutputDir, fileName);

        if (File.Exists(filePath))
        {
            Console.WriteLine($"[ISTNIEJE]  {fileName}");
            Interlocked.Increment(ref skipped);
            return;
        }

        Console.WriteLine($"[POBIERANIE] {pdfUrl}");
        var bytes = await WithRetry(() => http.GetByteArrayAsync(pdfUrl), fileName);
        await File.WriteAllBytesAsync(filePath, bytes);
        Console.WriteLine($"[GOTOWE]    {fileName} ({bytes.Length / 1024} KB)");
        Interlocked.Increment(ref downloaded);
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
