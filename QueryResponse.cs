/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

namespace Elektra.Knowledge.Models;

public sealed record QueryResponse(
    string Query,
    string Answer,
    string Source,
    bool IsFromCache = false,
    string? SourceUrl = null);