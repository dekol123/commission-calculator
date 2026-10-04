using Contracts;
using MediatR;

namespace Accrual.Application;

public sealed class SetSchemaHandler(ISchemaSettings schemas) : IRequestHandler<SetSchemaCommand, SchemaType>
{
    public Task<SchemaType> Handle(SetSchemaCommand command, CancellationToken cancellationToken) =>
        schemas.SetAsync(command.SchemaType, cancellationToken);
}
