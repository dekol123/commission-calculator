using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed record ClaimPayoutCommand(ClaimPayoutRequest Request) : IRequest<ClaimPayoutResponse>;
