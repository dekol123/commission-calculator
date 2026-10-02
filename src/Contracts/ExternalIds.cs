namespace Contracts;

public static class ExternalIds
{
    public const int MaxLength = 128;

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && value.Trim().Length == value.Length;
}
