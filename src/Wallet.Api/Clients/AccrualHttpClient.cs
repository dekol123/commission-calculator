using System.Net.Http.Json;
using Contracts;

namespace Wallet.Api.Clients;

public interface IAccrualClient
{
    Task<ClaimPayoutResponse?> ClaimAsync(ClaimPayoutRequest request, CancellationToken cancellationToken);
}

public interface IWalletMetrics
{
    void PayoutBooked(decimal amount);

    void HttpClientFailed(string client);
}

public sealed class AccrualHttpClient(HttpClient http, ILogger<AccrualHttpClient> logger) : IAccrualClient
{
    public async Task<ClaimPayoutResponse?> ClaimAsync(ClaimPayoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("internal/payouts/claim", request, ApiJson.Options, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Accrual claim for payout {PayoutId} returned {StatusCode}", request.PayoutId, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ClaimPayoutResponse>(ApiJson.Options, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Accrual claim for payout {PayoutId} failed", request.PayoutId);
            return null;
        }
    }
}
