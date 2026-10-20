using Microsoft.AspNetCore.HttpOverrides;

namespace GymApp.Server;

/// <summary>
/// HTTPS ends at the host's proxy, so the client's IP and scheme arrive in X-Forwarded-For and X-Forwarded-Proto.
/// They're only believed from the proxy itself: loopback, plus the networks listed in
/// <c>ForwardedHeaders:KnownNetworks</c> (e.g. <c>ForwardedHeaders__KnownNetworks__0=10.0.0.0/8</c>). Believing anyone
/// would let a client pick its own IP and step around the per-IP rate limits.
/// </summary>
public static class TrustedProxies
{
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration config)
    {
        var networks = config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        return services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Only the entry the proxy itself added: anything further left was written by the client
            o.ForwardLimit = 1;
            foreach (var network in networks)
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
    }
}
