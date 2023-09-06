using System.Net;
using System.Reflection;

namespace ProxyValidator;

internal static class Program
{
    private const string Socks5 = "socks5://";
    private const string Socks4 = "socks4://";
    private const string Https = "https://";
    private const string Http = "http://";

    private const int NumbersToProcess = 500;
    private const string Socks5ProxiesOutputFile = "socks5-proxies.txt";
    private const string Socks4ProxiesOutputFile = "socks4-proxies.txt";
    private const string HttpProxiesOutputFile = "http-proxies.txt";
    private const string Socks5ProvidersInputFile = "socks5-providers.txt";
    private const string Socks4ProvidersInputFile = "socks4-providers.txt";
    private const string HttpProxyProvidersInputFile = "http-proxy-providers.txt";
    private const string WorkingProxiesOutputFolderName = "WorkingProxies";
    private const int MaxDegreeOfParallelism = 16;
    private const string ProxyProvidersInputFolderName = "ProxyProviders";
    private static readonly Uri IpifyOrgUrl = new("https://api.ipify.org/");
    private static readonly TimeSpan TimeOut = TimeSpan.FromSeconds(7);

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
                                 $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                             },
                             StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static async Task ValidateAllProxiesAsync(string rootPath)
    {
        var socks5Proxies = await GetProxyProviderUrlsAsync(rootPath, Socks5ProvidersInputFile, Socks5);
        await ValidateProxiesAsync(socks5Proxies, rootPath, Socks5ProxiesOutputFile);

        var socks4Proxies = await GetProxyProviderUrlsAsync(rootPath, Socks4ProvidersInputFile, Socks4);
        await ValidateProxiesAsync(socks4Proxies, rootPath, Socks4ProxiesOutputFile);

        var httpProxies = await GetProxyProviderUrlsAsync(rootPath, HttpProxyProvidersInputFile, Http);
        await ValidateProxiesAsync(httpProxies, rootPath, HttpProxiesOutputFile);
    }

    private static async Task ValidateProxiesAsync(IEnumerable<string> proxies, string rootPath, string fileName)
    {
        var outputFilePath = Path.Combine(rootPath, WorkingProxiesOutputFolderName, fileName);
        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism };
        await Parallel.ForEachAsync(proxies,
                                    options,
                                    async (proxyUrl, _) => await ValidateProxyAsync(proxyUrl, outputFilePath));
    }

    private static async Task ValidateProxyAsync(string proxyUrl, string outputFilePath)
    {
        try
        {
            Console.WriteLine($"Processing {proxyUrl}");

            DynamicProxyProvider.DynamicProxy = new WebProxy { Address = new Uri(proxyUrl) };
            using var cts = new CancellationTokenSource(TimeOut);
            var resultIp = await MyHttpClient.GetStringAsync(IpifyOrgUrl, cts.Token);
            if (proxyUrl.Contains(resultIp, StringComparison.Ordinal))
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

    private static async Task<IEnumerable<string>> GetProxyProviderUrlsAsync(
        string rootPath,
        string fileName,
        string protocol)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DynamicProxyProvider.DynamicProxy = null;

        var proxies = await File.ReadAllLinesAsync(Path.Combine(rootPath, ProxyProvidersInputFolderName, fileName));
        foreach (var proxyProviderUrl in proxies)
        {
            if (string.IsNullOrWhiteSpace(proxyProviderUrl))
            {
                continue;
            }

            Console.WriteLine($"Processing {proxyProviderUrl}");
            var content = await MyHttpClient.GetStringAsync(new Uri(proxyProviderUrl));
            var proxyItems = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var proxyItem in proxyItems)
            {
                if (string.IsNullOrWhiteSpace(proxyItem))
                {
                    continue;
                }

                var proxy = proxyItem.Replace(Http, "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(Https, "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(Socks4, "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(Socks5, "", StringComparison.OrdinalIgnoreCase);
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

        return results.OrderBy(x => Random.Shared.Next()).Take(NumbersToProcess);
    }
}
