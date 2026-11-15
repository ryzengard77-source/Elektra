/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Knowledge.Models;
using System.Globalization;

namespace Elektra.Knowledge.Providers;

public sealed class MathApiProvider : IKnowledgeProvider
{
    public string ProviderName => "Basic arithmetic";

    public bool CanHandle(string query) => MathExpressionParser.TryEvaluate(query, out _);

    public Task<QueryResponse?> TryResolveAsync(string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!MathExpressionParser.TryEvaluate(query, out decimal result))
            return Task.FromResult<QueryResponse?>(null);

        string answer = result.ToString("G", CultureInfo.InvariantCulture);
        return Task.FromResult<QueryResponse?>(new QueryResponse(query, answer, ProviderName));
    }
}