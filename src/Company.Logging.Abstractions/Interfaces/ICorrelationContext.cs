namespace Company.Logging.Abstractions;

/// <summary>
/// Provides access to the current request's correlation and trace identifiers.
/// Inject this into services that need to propagate correlation context.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// The correlation ID for the current request.
    /// Sourced from the incoming <c>X-Correlation-Id</c> header, or auto-generated.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// The ASP.NET Core request ID (<c>HttpContext.TraceIdentifier</c>).
    /// </summary>
    string? RequestId { get; }
}
