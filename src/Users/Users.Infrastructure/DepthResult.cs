namespace Users.Infrastructure;

public sealed class DepthResult
{
    // EF maps this property to the lowercase "value" returned by Postgres.
    public int value { get; set; }
}
