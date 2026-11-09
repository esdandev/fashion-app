using System.Net;
using System.Text;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> that answers every request with a
/// canned JSON body. Used by the offline feed tests; the incoming request is
/// intentionally ignored.
/// </summary>
public sealed class StubHttpHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}
