using Accrual.Application;

namespace Accrual.Infrastructure;

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

public sealed class HttpClientErrorHandler(IAccrualMetrics metrics, string clientName) : DelegatingHandler
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

public sealed class AccrualMetrics : IAccrualMetrics
{
    private readonly Prometheus.Counter _events = Prometheus.Metrics.CreateCounter(
        "accrual_events_accepted_total",
        "Profit events accepted into storage.",
        new Prometheus.CounterConfiguration { LabelNames = ["status"] });

    private readonly Prometheus.Counter _commissions = Prometheus.Metrics.CreateCounter(
        "accrual_commissions_calculated_total",
        "Commission rows written.");

    private readonly Prometheus.Counter _httpErrors = Prometheus.Metrics.CreateCounter(
        "accrual_http_client_errors_total",
        "Failed outbound HTTP calls.",
        new Prometheus.CounterConfiguration { LabelNames = ["client"] });

    private readonly Prometheus.Counter _outboxFailed = Prometheus.Metrics.CreateCounter(
        "accrual_outbox_failed_total",
        "Outbox messages that exhausted retries.");

    private readonly Prometheus.Gauge _outboxDepth = Prometheus.Metrics.CreateGauge(
        "accrual_outbox_depth",
        "Outbox messages that are not delivered yet.");

    public void EventAccepted(string status) => _events.WithLabels(status).Inc();

    public void CommissionsCalculated(int count) => _commissions.Inc(count);

    public void HttpClientFailed(string client) => _httpErrors.WithLabels(client).Inc();

    public void OutboxFailed() => _outboxFailed.Inc();

    public void SetOutboxDepth(int depth) => _outboxDepth.Set(depth);
}
