namespace Dispatch.Domain;

/// <summary>
/// An entity with a managed optimistic-concurrency version. The version is incremented
/// by the save-time interceptor, never by hand.
/// </summary>
public interface IVersioned
{
    int Version { get; set; }
}
