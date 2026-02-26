namespace Company.Logging.Abstractions;

/// <summary>
/// Represents a safe, structured log event context for passing into log calls.
/// Use this instead of dumping arbitrary objects to prevent Elasticsearch mapping explosions.
/// </summary>
public sealed record class LogData
{
    /// <summary>
    /// String-to-string tags. Safe for ES keyword fields. E.g. { "order.status", "pending" }.
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>
    /// String-to-object metadata. Use sparingly. Keys must be whitelisted via config.
    /// </summary>
    public Dictionary<string, object?> Meta { get; init; } = new();

    /// <summary>
    /// Optional domain event name. E.g. "order.created".
    /// </summary>
    public string? EventName { get; init; }

    /// <summary>
    /// Optional entity type being acted upon. E.g. "Order".
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// Optional entity id. E.g. "order-123".
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Fluent factory.
    /// </summary>
    public static LogData Create() => new();

    /// <summary>
    /// Returns a new <see cref="LogData"/> with the given tag added.
    /// Never mutates the original — safe to use with <c>record</c> <c>with</c> expressions.
    /// </summary>
    public LogData WithTag(string key, string value)
    {
        var newTags = new Dictionary<string, string>(Tags) { [key] = value };
        return this with { Tags = newTags };
    }

    /// <summary>
    /// Returns a new <see cref="LogData"/> with the given metadata entry added.
    /// Never mutates the original — safe to use with <c>record</c> <c>with</c> expressions.
    /// </summary>
    public LogData WithMeta(string key, object? value)
    {
        var newMeta = new Dictionary<string, object?>(Meta) { [key] = value };
        return this with { Meta = newMeta };
    }

    /// <summary>Sets the domain event name.</summary>
    public LogData WithEvent(string eventName)
        => this with { EventName = eventName };

    /// <summary>Sets the entity context.</summary>
    public LogData WithEntity(string type, string id)
        => this with { EntityType = type, EntityId = id };
}
