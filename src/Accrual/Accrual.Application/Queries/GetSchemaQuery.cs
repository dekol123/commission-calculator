using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed record GetSchemaQuery : IRequest<SchemaType>;
