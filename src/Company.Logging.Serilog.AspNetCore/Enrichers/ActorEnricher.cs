using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;
using System.Security.Claims;

namespace Company.Logging.Serilog.AspNetCore.Enrichers;

/// <summary>
/// Enriches log events with safe actor identity fields from the authenticated principal:
/// <c>user.id</c>, <c>client.id</c>, <c>organization.id</c> (tenant).
/// Only logs opaque identifiers — never names, emails, or other PII.
/// </summary>
public sealed class ActorEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Requires <see cref="IHttpContextAccessor"/> from DI.</summary>
    public ActorEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User?.Identity?.IsAuthenticated != true) return;

        var principal = context.User;

        // user.id: sub | nameidentifier claim — opaque identifier only
        var userId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("user.id", userId));
        }

        // client.id: azp | client_id claim (OAuth2 client)
        var clientId = principal.FindFirstValue("azp")
            ?? principal.FindFirstValue("client_id");
        if (!string.IsNullOrEmpty(clientId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("client.id", clientId));
        }

        // organization.id: tid | tenant_id claim
        var tenantId = principal.FindFirstValue("tid")
            ?? principal.FindFirstValue("tenant_id");
        if (!string.IsNullOrEmpty(tenantId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("organization.id", tenantId));
        }
    }
}
