/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Knowledge.Models;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elektra.Knowledge.Providers;

public sealed partial class WikipediaProvider(HttpClient http) : IKnowledgeProvider
{
    public string ProviderName => "Wikipedia";

    private static readonly (string Pattern, string[] InfoboxKeys, string SpokenLabel)[] AttributeMappings =
        CreateAttributeMappings();

    private static (string Pattern, string[] InfoboxKeys, string SpokenLabel)[] CreateAttributeMappings()
    {
        var mappings = new List<(string Pattern, string[] InfoboxKeys, string SpokenLabel)>
        {
            ("how tall", ["height"], "height"),
            ("height", ["height"], "height"),
            ("how old", ["born"], "birth date"),
            ("born", ["born"], "birth date"),
            ("birthday", ["born"], "birth date"),
            ("birthplace", ["born"], "birthplace"),
            ("died", ["died"], "death date"),
            ("death", ["died"], "death date"),
            ("net worth", ["net worth"], "net worth"),
            ("spouse", ["spouse", "spouses"], "spouse"),
            ("wife", ["spouse", "spouses"], "spouse"),
            ("husband", ["spouse", "spouses"], "spouse"),
            ("children", ["children"], "children"),
            ("alma mater", ["alma mater", "education"], "education"),
            ("education", ["education", "alma mater"], "education"),
            ("occupation", ["occupation", "known for", "profession"], "occupation"),
            ("known for", ["known for", "notable work"], "notable work"),
            ("population", ["population", "total", "estimate"], "population"),
            ("capital", ["capital"], "capital"),
            ("area", ["area", "total"], "area"),
            ("founder", ["founder", "founders"], "founder"),
            ("founded", ["founded"], "founding date"),
            ("headquarters", ["headquarters"], "headquarters"),
            ("ceo", ["key people", "ceo"], "leadership")
        };

        (string Phrases, string[] Keys, string Label)[] groups =
        [
            ("date of birth|birth date|birthdate|when born|born on|born when|birth year|birth month|date born|birth day|when was born|birthday date", ["born"], "birth date"),
            ("place of birth|birth place|birth location|born in|where born|birth town|birth city|birth country|birth region|native place|birthplace of|location born", ["born"], "birthplace"),
            ("date of death|death date|deathdate|when died|died on|death year|death month|death day|date deceased|when passed away|date of passing|death location", ["died"], "death date"),
            ("nationality|citizenship|citizen of|national origin|country of origin|what nationality|nationality of|country citizen|passport country|national identity|origin country|which country from", ["nationality", "citizenship"], "nationality"),
            ("profession|job|line of work|career field|type of work|working as|works as|what does for work|professional role|career|trade|occupation type", ["occupation", "profession", "known for"], "occupation"),
            ("schooling|academic background|college|university attended|study|studied at|degree|degrees|educational background|education history|where studied|training", ["education", "alma mater"], "education"),
            ("partner|life partner|married to|marriage partner|who married|relationship status|significant other|husband or wife|wife or husband|marital partner|consort|marriage", ["spouse", "spouses", "partner"], "spouse"),
            ("son or daughter|sons and daughters|number of children|child|offspring|kids|how many kids|has children|children count|descendants|issue|number of offspring", ["children", "issue"], "children"),
            ("mother|father|mom|dad|parents|parentage|who are the parents|family parents|mother and father|father and mother|parent names|parent", ["parents", "mother", "father"], "parents"),
            ("brother|sister|brothers and sisters|siblings|number of siblings|family siblings|who are siblings|sibling count|brother or sister|sisters and brothers|has siblings|sibling", ["siblings"], "siblings"),
            ("award|prize|honor|honour|awards won|prizes won|recognition|distinctions|what awards|award received|honors received|major awards", ["awards", "honours", "honors"], "awards"),
            ("notable achievement|major accomplishment|best known work|famous work|important work|notable works|main contribution|claim to fame|greatest achievement|major work|signature work|accomplishments", ["known for", "notable work", "notable works"], "notable work"),
            ("number of residents|people living|inhabitants|resident count|population size|how many people|population count|total residents|number of inhabitants|population estimate|resident population|people count", ["population", "total", "estimate"], "population"),
            ("seat of government|administrative center|administrative centre|capital city|capital town|national capital|provincial capital|state capital|government seat|capital of|what is capital|capital location", ["capital", "seat of government"], "capital"),
            ("land area|total area|surface area|size in area|area size|area covered|area measurement|square kilometers|square kilometres|square miles|total land|geographic area", ["area", "total"], "area"),
            ("money used|currency used|national currency|official currency|what currency|currency name|monetary unit|money system|currency code|local currency|unit of money|legal tender", ["currency", "currency code"], "currency"),
            ("official language|spoken language|languages spoken|national language|primary language|main language|what language|language used|language spoken|languages used|native language|common language", ["official language", "languages", "language"], "official language"),
            ("faith|belief system|religious belief|religious affiliation|what religion|religion followed|religious group|religious tradition|religion of|denomination|religious background|spiritual tradition", ["religion", "religious affiliation"], "religion"),
            ("animal species|scientific species|species name|what species|species classification|organism species|biological species|species of|species type|taxonomic species|species designation|species identified", ["species", "binomial name"], "species"),
            ("natural habitat|where lives|living environment|native habitat|habitat type|habitat range|where found|natural range|environment lives|ecological habitat|habitat of|home environment", ["habitat", "range"], "habitat"),
            ("body mass|physical mass|weight in kilograms|weight in pounds|how much weigh|weighs how much|mass in kilograms|mass in pounds|average weight|total weight|weight measurement|current weight", ["weight", "mass"], "weight"),
            ("orbital period|orbit time|time to orbit|how long orbit|year length|orbital duration|period of orbit|revolution period|time for orbit|orbit period|orbital year|revolution time", ["orbital period", "period"], "orbital period"),
            ("date discovered|discovery date|when discovered|discovered on|discovery year|year discovered|discovered when|first observed|date first observed|discovery recorded|date found|when found", ["discovery", "discovered"], "discovery date"),
            ("parent company|owned by|ownership|owner|company owner|corporate owner|who owns|company parent|controlling company|ownership group|parent organization|parent organisation", ["owner", "parent company", "ownership"], "ownership"),
            ("head coach|manager|team manager|coach name|current coach|who coaches|team coach|club manager|manager name|coaching staff|coach of|manager of", ["manager", "head coach", "coach"], "manager")
        ];

        foreach (var group in groups)
        {
            foreach (string phrase in group.Phrases.Split('|'))
                mappings.Add((phrase, group.Keys, group.Label));
        }

        return mappings.ToArray();
    }

    [GeneratedRegex(@"<tr[^>]*>.*?<th[^>]*class=""[^""]*infobox-label[^""]*""[^>]*>(.*?)</th>.*?<td[^>]*class=""[^""]*infobox-data[^""]*""[^>]*>(.*?)</td>.*?</tr>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InfoboxRowRegex();

    [GeneratedRegex(@"<span[^>]*style=""[^""]*display\s*:\s*none[^""]*""[^>]*>.*?</span>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HiddenSpanRegex();

    [GeneratedRegex(@"<sup[^>]*class=""[^""]*reference[^""]*""[^>]*>.*?</sup>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CitationSupRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public bool CanHandle(string query) => !string.IsNullOrWhiteSpace(query);

    public async Task<QueryResponse?> TryResolveAsync(string query, CancellationToken ct)
    {
        string? nameMeaningEntity = ParseNameMeaningQuery(query);
        var (parsedEntity, parsedAttribute) = ParseEntityAndAttribute(query);
        string entity = nameMeaningEntity ?? parsedEntity;
        var matchedAttr = nameMeaningEntity == null ? parsedAttribute : null;
        if (string.IsNullOrWhiteSpace(entity))
            return null;

        entity = RemoveLeadingArticle(entity);
        if (matchedAttr is { } attr)
        {
            var infoboxAnswer = await TryExtractInfoboxAttributeAsync(query, entity, attr.InfoboxKeys, attr.SpokenLabel, ct);
            if (infoboxAnswer != null)
                return infoboxAnswer;
        }

        (string Title, string Extract)? summary = nameMeaningEntity != null
            ? await TryGetSummaryAsync($"{entity} (given name)", ct) ?? await TryGetSummaryAsync(entity, ct)
            : await TryGetSummaryAsync(entity, ct);
        if (summary == null)
        {
            string searchEntity = nameMeaningEntity != null ? $"{entity} given name" : entity;
            string? matchingTitle = await TrySearchTitleAsync(searchEntity, ct);
            if (matchingTitle == null)
                return null;

            if (matchedAttr is { } targetAttribute)
            {
                var infoboxAnswer = await TryExtractInfoboxAttributeAsync(
                    query, matchingTitle, targetAttribute.InfoboxKeys, targetAttribute.SpokenLabel, ct);
                if (infoboxAnswer != null)
                    return infoboxAnswer;
            }

            summary = await TryGetSummaryAsync(matchingTitle, ct);
            if (summary == null)
                return null;
        }

        string canonicalTitle = summary.Value.Title;
        string extract = summary.Value.Extract;
        string[] sentences = extract.Split(". ", StringSplitOptions.RemoveEmptyEntries);

        if (matchedAttr is { } target)
        {
            string? matchingSentence = sentences.FirstOrDefault(s =>
                target.InfoboxKeys.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(matchingSentence))
            {
                string cleanSentence = matchingSentence.EndsWith('.') ? matchingSentence : $"{matchingSentence}.";
                return new QueryResponse(query, cleanSentence.Trim(), $"{ProviderName} (Summary)", SourceUrl: CreatePageUrl(canonicalTitle));
            }
        }

        string conciseOverview = sentences.Length > 2
            ? $"{sentences[0]}. {sentences[1]}."
            : extract;

        return new QueryResponse(query, conciseOverview.Trim(), $"{ProviderName} ({canonicalTitle})", SourceUrl: CreatePageUrl(canonicalTitle));
    }

    private async Task<(string Title, string Extract)?> TryGetSummaryAsync(string title, CancellationToken ct)
    {
        string summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
        using var response = await http.GetAsync(summaryUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("extract", out var extractProperty))
            return null;

        string? extract = extractProperty.GetString();
        if (string.IsNullOrWhiteSpace(extract))
            return null;

        string canonicalTitle = document.RootElement.TryGetProperty("title", out var titleProperty)
            ? titleProperty.GetString() ?? title
            : title;
        return (canonicalTitle, extract);
    }

    private async Task<string?> TrySearchTitleAsync(string entity, CancellationToken ct)
    {
        string searchUrl = "https://en.wikipedia.org/w/api.php?action=query&list=search" +
            $"&srsearch={Uri.EscapeDataString(entity)}&srlimit=1&format=json&formatversion=2";
        using var response = await http.GetAsync(searchUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("query", out var queryNode) ||
            !queryNode.TryGetProperty("search", out var searchResults) ||
            searchResults.ValueKind != JsonValueKind.Array || searchResults.GetArrayLength() == 0)
            return null;

        JsonElement firstResult = searchResults[0];
        return firstResult.TryGetProperty("title", out var titleProperty)
            ? titleProperty.GetString()
            : null;
    }

    private static string CreatePageUrl(string title) =>
        $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

    private static string RemoveLeadingArticle(string entity)
    {
        foreach (string article in new[] { "a ", "an ", "the " })
        {
            if (entity.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return entity[article.Length..].Trim();
        }

        return entity;
    }

    private async Task<QueryResponse?> TryExtractInfoboxAttributeAsync(
        string originalQuery,
        string entity,
        string[] targetKeys,
        string spokenLabel,
        CancellationToken ct)
    {
        string apiUrl =
            $"https://en.wikipedia.org/w/api.php?action=parse&page={Uri.EscapeDataString(entity)}" +
            "&section=0&prop=text&redirects=1&format=json&formatversion=2";

        using var resp = await http.GetAsync(apiUrl, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("parse", out var parseNode) ||
            !parseNode.TryGetProperty("text", out var htmlProp))
            return null;

        string resolvedTitle = parseNode.TryGetProperty("title", out var titleProp)
            ? titleProp.GetString() ?? entity
            : entity;

        string html = htmlProp.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html))
            return null;

        foreach (Match match in InfoboxRowRegex().Matches(html))
        {
            string label = CleanHtmlFragment(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            bool isKeyMatch = targetKeys.Any(key =>
                label.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                label.Contains(key, StringComparison.OrdinalIgnoreCase));

            if (!isKeyMatch)
                continue;

            string rawValue = CleanHtmlFragment(match.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            return new QueryResponse(originalQuery, $"{resolvedTitle}'s {spokenLabel} is {rawValue}.", $"{ProviderName} Infobox", SourceUrl: CreatePageUrl(resolvedTitle));
        }

        return null;
    }

    private static string CleanHtmlFragment(string htmlFragment)
    {
        string cleaned = HiddenSpanRegex().Replace(htmlFragment, " ");
        cleaned = CitationSupRegex().Replace(cleaned, " ");
        cleaned = Regex.Replace(cleaned, @"<br\s*/?>|</li>", ", ", RegexOptions.IgnoreCase);
        cleaned = HtmlTagRegex().Replace(cleaned, " ");
        cleaned = WebUtility.HtmlDecode(cleaned).Replace('\u00A0', ' ');
        return WhitespaceRegex().Replace(cleaned, " ").Trim().Trim(',', ';');
    }

    private static (string Entity, (string Pattern, string[] InfoboxKeys, string SpokenLabel)? Attribute) ParseEntityAndAttribute(string rawQuery)
    {
        string q = rawQuery.Trim().TrimEnd('?', '.', '!');

        if (q.StartsWith("what is ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = q[8..].Trim();
            int apostrophe = rest.IndexOf("'s ", StringComparison.OrdinalIgnoreCase);
            if (apostrophe > 0)
            {
                string subject = rest[..apostrophe].Trim();
                var attribute = FindAttributeMapping(rest[(apostrophe + 3)..]);
                if (attribute != null)
                    return (subject, attribute);
            }

            string attributeQuery = rest.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
                ? rest[4..].TrimStart()
                : rest;
            foreach (var mapping in AttributeMappings)
            {
                if (!attributeQuery.StartsWith(mapping.Pattern, StringComparison.OrdinalIgnoreCase) ||
                    attributeQuery.Length <= mapping.Pattern.Length ||
                    !char.IsWhiteSpace(attributeQuery[mapping.Pattern.Length]))
                    continue;

                string remainder = attributeQuery[mapping.Pattern.Length..].TrimStart();
                foreach (string connector in new[] { "of ", "for ", "in " })
                {
                    if (remainder.StartsWith(connector, StringComparison.OrdinalIgnoreCase))
                    {
                        string entity = remainder[connector.Length..].Trim();
                        if (entity.Length > 0)
                            return (entity, mapping);
                    }
                }
            }
        }

        foreach (var mapping in AttributeMappings)
        {
            if (StartsWithAttributeCommand(q, mapping.Pattern, " is ", out string entity) ||
                StartsWithAttributeCommand(q, mapping.Pattern, " was ", out entity))
                return (entity, mapping);
        }

        foreach (var mapping in AttributeMappings)
        {
            string suffix = $" {mapping.Pattern}";
            if (q.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && q.Length > suffix.Length + 1)
            {
                string entity = q[..^suffix.Length].Trim();
                if (entity.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
                    entity = entity[..^2].Trim();

                return (entity, mapping);
            }
        }

        string[] overviewPrefixes = ["who is ", "who was ", "what is ", "what was ", "what are ", "tell me about ", "define ", "where is "];
        foreach (string prefix in overviewPrefixes)
        {
            if (q.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (q[prefix.Length..].Trim(), null);
        }

        return (q, null);
    }

    private static string? ParseNameMeaningQuery(string rawQuery)
    {
        string query = rawQuery.Trim().TrimEnd('?', '.', '!');
        string[] prefixes =
        [
            "what does the name ",
            "what does ",
            "what is the meaning of the name ",
            "what is the meaning of "
        ];

        foreach (string prefix in prefixes)
        {
            if (!query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string remainder = query[prefix.Length..].Trim();
            if (remainder.EndsWith(" mean", StringComparison.OrdinalIgnoreCase))
                remainder = remainder[..^5].Trim();
            else if (prefix.StartsWith("what does", StringComparison.OrdinalIgnoreCase))
                continue;

            if (remainder.Length > 0)
                return remainder;
        }

        return null;
    }

    private static (string Pattern, string[] InfoboxKeys, string SpokenLabel)? FindAttributeMapping(string query)
    {
        foreach (var mapping in AttributeMappings)
        {
            if (query.Contains(mapping.Pattern, StringComparison.OrdinalIgnoreCase))
                return mapping;
        }

        return null;
    }

    private static bool StartsWithAttributeCommand(string query, string pattern, string connector, out string entity)
    {
        if (query.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) &&
            query.Length > pattern.Length + connector.Length &&
            query.AsSpan(pattern.Length).StartsWith(connector, StringComparison.OrdinalIgnoreCase))
        {
            entity = query[(pattern.Length + connector.Length)..].Trim();
            return entity.Length > 0;
        }

        entity = string.Empty;
        return false;
    }
}