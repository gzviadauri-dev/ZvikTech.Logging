using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Enrichers;
using Microsoft.AspNetCore.Http;

namespace Company.Logging.Serilog.AspNetCore.Middleware;

/// <summary>
/// Provides <see cref="ICorrelationContext"/> from the current HTTP request's Items collection.
/// Register as Scoped. If no HTTP context is present (background services), falls back to AsyncLocal.
/// </summary>
internal sealed class HttpContextCorrelationContextAccessor : ICorrelationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCorrelationContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string CorrelationId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Items.TryGetValue(typeof(ICorrelationContext), out var obj) == true
                && obj is ICorrelationContext ctx)
            {
                return ctx.CorrelationId;
            }

            // Fallback to AsyncLocal (works in background services / non-HTTP contexts)
            return CorrelationContext.CorrelationId ?? string.Empty;
        }
    }

    /// <inheritdoc />
    public string? RequestId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Items.TryGetValue(typeof(ICorrelationContext), out var obj) == true
                && obj is ICorrelationContext ctx)
            {
                return ctx.RequestId;
            }

            return CorrelationContext.RequestId;
        }
    }
}
