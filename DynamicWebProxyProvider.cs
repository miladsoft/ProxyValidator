using System.Net;

namespace ProxyValidator;

public class DynamicWebProxyProvider : IWebProxy
{
    private IWebProxy? _proxy;

    public DynamicWebProxyProvider()
    {
    }

    public DynamicWebProxyProvider(IWebProxy dynamicProxy) => DynamicProxy = dynamicProxy;

    public IWebProxy? DynamicProxy
    {
        get => _proxy ??= WebRequest.DefaultWebProxy;
        set => _proxy = value;
    }

    public ICredentials? Credentials
    {
        get => DynamicProxy?.Credentials;
        set
        {
            if (DynamicProxy != null)
            {
                DynamicProxy.Credentials = value;
            }
        }
    }

    public Uri? GetProxy(Uri destination) => DynamicProxy?.GetProxy(destination);

    public bool IsBypassed(Uri host) => DynamicProxy?.IsBypassed(host) == true;
}