using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed record SetSchemaCommand(SchemaType SchemaType) : IRequest<SchemaType>;
