using System.Net.Http.Json;
using System.Text;
using Accrual.Application;
using Contracts;

namespace Accrual.Infrastructure;

public sealed class UsersHttpGateway(HttpClient http, ILogger<UsersHttpGateway> logger) : IUsersGateway
{
    public async Task<AncestorLookup> GetAncestorsAsync(string userExternalId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetAsync($"users/{Uri.EscapeDataString(userExternalId)}/up", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new AncestorLookup.NotFound();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Users lookup for {UserExternalId} returned {StatusCode}",
                    userExternalId,
                    (int)response.StatusCode);
                return new AncestorLookup.Unavailable();
            }

            var nodes = await response.Content.ReadFromJsonAsync<List<TreeNodeResponse>>(ApiJson.Options, cancellationToken) ?? [];
            return new AncestorLookup.Found(nodes.OrderBy(node => node.Level).Select(node => node.ExternalId).ToList());
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Users lookup failed for {UserExternalId}", userExternalId);
            return ClientFailures.IsCircuitOpen(exception)
                ? new AncestorLookup.CircuitOpen()
                : new AncestorLookup.Unavailable();
        }
    }
}

public sealed class WalletHttpClient(HttpClient http)
{
    public async Task SendAsync(string payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("internal/inbox", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

internal static class ClientFailures
{
    public static bool IsCircuitOpen(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name == "BrokenCircuitException")
                return true;
        }

        return false;
    }
}
