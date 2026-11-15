/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Knowledge.Models;

namespace Elektra.Knowledge.Providers;

public interface IKnowledgeProvider
{
    string ProviderName { get; }
    bool CanHandle(string query);
    Task<QueryResponse?> TryResolveAsync(string query, CancellationToken ct);
}

public interface IRealTimeKnowledgeProvider : IKnowledgeProvider
{
}