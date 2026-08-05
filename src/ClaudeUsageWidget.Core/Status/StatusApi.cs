using System.Net.Http.Headers;

namespace ClaudeUsageWidget.Core;

/// Reads Claude's public status page. No credentials: the endpoint is open.
public sealed class StatusApi
{
    public static readonly Uri Endpoint = new("https://status.claude.com/api/v2/summary.json");

    /// Shared instance so callers that don't inject their own HttpClient
    /// still reuse one connection pool instead of opening a fresh socket per
    /// refresh tick.
    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _client;

    public StatusApi(HttpClient? client = null)
    {
        _client = client ?? SharedClient;
    }

    public async Task<ServiceStatus> FetchAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UsageException(UsageError.Network(ex.Message));
        }

        using (response)
        {
            HttpValidation.Validate((int)response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(ct);
            return StatusDecoder.Status(body);
        }
    }
}
