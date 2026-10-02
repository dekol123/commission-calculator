using Contracts;
using MediatR;

namespace Wallet.Application;

public sealed record GetPayoutHistoryQuery(string ExternalId) : IRequest<IReadOnlyList<PayoutHistoryItemResponse>>;
