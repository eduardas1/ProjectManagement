namespace Workboard.Application.Common;

public enum OperationStatus { Success, NotFound, Invalid, Conflict, Busy }

public record OperationResult<T>(OperationStatus Status, T? Value, string? Field = null, string? Error = null)
{
    public static OperationResult<T> Success(T value) => new(OperationStatus.Success, value);
    public static OperationResult<T> NotFound() => new(OperationStatus.NotFound, default);
    public static OperationResult<T> Invalid(string field, string error) => new(OperationStatus.Invalid, default, field, error);
    public static OperationResult<T> Conflict() => new(OperationStatus.Conflict, default, "", "This project has changed. Reload it before submitting another change.");
    public static OperationResult<T> Busy() => new(OperationStatus.Busy, default, "", "The project is busy. Please try again.");
}
