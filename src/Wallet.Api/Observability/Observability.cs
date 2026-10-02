namespace Wallet.Api.Observability;

public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";

    private static readonly AsyncLocal<string?> Holder = new();

    public static string? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString("n");

        CorrelationContext.Current = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            await next(context);
    }
}

public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CorrelationContext.Current is { Length: > 0 } correlationId)
            request.Headers.TryAddWithoutValidation(CorrelationContext.HeaderName, correlationId);

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class HttpClientErrorHandler(Clients.IWalletMetrics metrics, string clientName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode >= 500)
                metrics.HttpClientFailed(clientName);

            return response;
        }
        catch (Exception)
        {
            metrics.HttpClientFailed(clientName);
            throw;
        }
    }
}

public sealed class WalletMetrics : Clients.IWalletMetrics
{
    private readonly Prometheus.Counter _amount = Prometheus.Metrics.CreateCounter(
        "wallet_payout_amount_total",
        "Sum of commission amounts booked onto wallets.");

    private readonly Prometheus.Counter _httpErrors = Prometheus.Metrics.CreateCounter(
        "wallet_http_client_errors_total",
        "Failed outbound HTTP calls.",
        new Prometheus.CounterConfiguration { LabelNames = ["client"] });

    public void PayoutBooked(decimal amount) => _amount.Inc((double)amount);

    public void HttpClientFailed(string client) => _httpErrors.WithLabels(client).Inc();
}
