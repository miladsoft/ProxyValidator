using System.Net;
using System.Reflection;

namespace ProxyValidator;

internal static class Program
{
    private static readonly TimeSpan TimeOut = TimeSpan.FromSeconds(20);
    private static readonly object LockObject = new();
    private static readonly DynamicWebProxyProvider DynamicProxyProvider = new();

    private static readonly HttpClient MyHttpClient =
        new(new HttpClientHandler
            {
                UseProxy = true,
                Proxy = DynamicProxyProvider,
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });

    private static async Task Main(string[] args)
    {
        var rootPath = GetRootPath();
        await ValidateAllProxiesAsync(rootPath);
    }

    private static string GetRootPath()
    {
        var dirName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (dirName is null)
        {
            throw new InvalidOperationException("dirName is null");
        }

        Directory.SetCurrentDirectory(dirName);
        return dirName.Split(new[]
                             {
                                 "/bin/",
                             },
                             StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static async Task ValidateAllProxiesAsync(string rootPath)
    {
        var socks5Proxies = await GetProxyProviderUrlsAsync(rootPath, "socks5-providers.txt", "socks5://");
        await ValidateProxiesAsync(socks5Proxies, rootPath, "socks5-proxies.txt");

        var socks4Proxies = await GetProxyProviderUrlsAsync(rootPath, "socks4-providers.txt", "socks4://");
        await ValidateProxiesAsync(socks4Proxies, rootPath, "socks4-proxies.txt");

        var httpProxies = await GetProxyProviderUrlsAsync(rootPath, "http-proxy-providers.txt", "http://");
        await ValidateProxiesAsync(httpProxies, rootPath, "http-proxies.txt");
    }

    private static async Task ValidateProxiesAsync(IEnumerable<string> proxies, string rootPath, string fileName)
    {
        var outputFilePath = Path.Combine(rootPath, "WorkingProxies", fileName);
        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = 16 };
        await Parallel.ForEachAsync(proxies,
                                    options,
                                    async (proxyUrl, _) => await ValidateProxyAsync(proxyUrl, outputFilePath));
    }

    private static async Task ValidateProxyAsync(string proxyUrl, string outputFilePath)
    {
        try
        {
            Console.WriteLine($"Processing {proxyUrl}");

            DynamicProxyProvider.DynamicProxy =
                new WebProxy { Address = new Uri(proxyUrl) };
            using var cts = new CancellationTokenSource(TimeOut);
            var resultIp = await MyHttpClient.GetStringAsync("https://api.ipify.org/", cts.Token);
            if (proxyUrl.Contains(resultIp))
            {
                lock (LockObject)
                {
                    File.AppendAllText(outputFilePath, $"{proxyUrl}{Environment.NewLine}");
                }

                Console.WriteLine($"Found a working proxy! -> {proxyUrl}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {proxyUrl}, {ex.Message}");
        }
    }

    private static async Task<ISet<string>> GetProxyProviderUrlsAsync(string rootPath, string fileName, string protocol)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DynamicProxyProvider.DynamicProxy = null;

        var proxies = await File.ReadAllLinesAsync(Path.Combine(rootPath, "ProxyProviders", fileName));
        foreach (var proxyProviderUrl in proxies)
        {
            if (string.IsNullOrWhiteSpace(proxyProviderUrl))
            {
                continue;
            }

            Console.WriteLine($"Processing {proxyProviderUrl}");
            var content = await MyHttpClient.GetStringAsync(proxyProviderUrl);
            var proxyItems = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var proxyItem in proxyItems)
            {
                if (string.IsNullOrWhiteSpace(proxyItem))
                {
                    continue;
                }

                var proxy = proxyItem.Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("socks4://", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("socks5://", "", StringComparison.OrdinalIgnoreCase);
                var proxyParts = proxy.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (proxyParts.Length < 2)
                {
                    continue;
                }

                var ip = proxyParts[0];
                var port = proxyParts[1];
                var proxyUrl = $"{protocol}{ip}:{port}";
                results.Add(proxyUrl);
            }
        }

        return results;
    }
}