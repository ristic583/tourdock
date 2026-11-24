namespace TourDock.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    TooManyRequests,

    /// <summary>A dependency the feature needs is down or switched off; the rest of the app works.</summary>
    Unavailable,
}

/// <summary>An expected business failure. <see cref="Code"/> is stable and machine-readable (e.g. "Slot.Full").</summary>
public record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorType.TooManyRequests);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

/// <summary>Input validation failure with per-field messages.</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("Validation.Failed", "One or more validation errors occurred.", ErrorType.Validation);
