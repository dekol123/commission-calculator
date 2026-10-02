using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed class GetSchemaHandler(ISchemaSettings schemas) : IRequestHandler<GetSchemaQuery, SchemaType>
{
    public Task<SchemaType> Handle(GetSchemaQuery query, CancellationToken cancellationToken) =>
        schemas.GetAsync(cancellationToken);
}
