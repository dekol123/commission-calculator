using Contracts;
using MediatR;

namespace Wallet.Application;

public sealed record GetBalanceQuery(string ExternalId) : IRequest<WalletBalanceResponse>;
