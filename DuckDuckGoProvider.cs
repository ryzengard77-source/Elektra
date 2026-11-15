/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Knowledge.Models;
using System.Net.Http;
using System.Text.Json;

namespace Elektra.Knowledge.Providers;

public sealed class DuckDuckGoProvider(HttpClient http) : IKnowledgeProvider
{
    public string ProviderName => "DuckDuckGo";
    public bool CanHandle(string query) => !string.IsNullOrWhiteSpace(query);

    public async Task<QueryResponse?> TryResolveAsync(string query, CancellationToken ct)
    {
        string url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
        using var stream = await http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        string? answer = doc.RootElement.TryGetProperty("AbstractText", out var abs) &&
                         !string.IsNullOrWhiteSpace(abs.GetString())
            ? abs.GetString()
            : doc.RootElement.TryGetProperty("Answer", out var ans)
                ? ans.GetString()
                : null;

        return string.IsNullOrWhiteSpace(answer)
            ? null
            : new QueryResponse(query, answer.Trim(), ProviderName);
    }
}