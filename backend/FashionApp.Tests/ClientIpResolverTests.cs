using System.Net;
using FashionApp.Api.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;

public sealed class ClientIpResolverTests
{
    private static DefaultHttpContext CreateContext(string? remoteIp = "10.0.0.5")
    {
        var context = new DefaultHttpContext();
        if (remoteIp is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        return context;
    }

    [Fact]
    public void Leftmost_forwarded_for_wins_over_the_socket_address()
    {
        var context = CreateContext();
        context.Request.Headers[ClientIpResolver.ForwardedForHeader] =
            " 203.0.113.9 , 10.0.0.1, 10.0.0.2";

        Assert.Equal("203.0.113.9", ClientIpResolver.Resolve(context));
    }

    [Fact]
    public void Invalid_leftmost_forwarded_for_falls_back_to_the_socket_address()
    {
        var context = CreateContext("10.0.0.5");
        context.Request.Headers[ClientIpResolver.ForwardedForHeader] =
            "unknown, 203.0.113.11";

        // The rightmost entry is a proxy hop, never a trusted client identity.
        Assert.Equal("10.0.0.5", ClientIpResolver.Resolve(context));
    }

    [Fact]
    public void No_headers_use_the_socket_address()
    {
        var context = CreateContext("192.0.2.20");

        Assert.Equal("192.0.2.20", ClientIpResolver.Resolve(context));
    }

    [Fact]
    public void Missing_everything_uses_the_unknown_partition()
    {
        var context = CreateContext(remoteIp: null);

        Assert.Equal(ClientIpResolver.UnknownKey, ClientIpResolver.Resolve(context));
    }
}
