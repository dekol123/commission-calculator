using Contracts;
using MediatR;

namespace Wallet.Application;

public sealed record AcceptInboxCommand(WalletInboxRequest Request) : IRequest;
