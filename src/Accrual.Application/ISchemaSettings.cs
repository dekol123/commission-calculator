using Contracts;

namespace Accrual.Application;

public interface ISchemaSettings
{
    Task<SchemaType> GetAsync(CancellationToken cancellationToken);

    Task<SchemaType> SetAsync(SchemaType schema, CancellationToken cancellationToken);
}
