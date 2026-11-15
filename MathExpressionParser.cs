/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Globalization;
using System.Text.RegularExpressions;

namespace Elektra.Knowledge.Providers;

internal static partial class MathExpressionParser
{
    private static readonly HashSet<string> NumberWords = new(StringComparer.Ordinal)
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
        "eighteen", "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy",
        "eighty", "ninety", "hundred", "thousand", "million", "billion", "and"
    };

    public static bool TryEvaluate(string query, out decimal result)
    {
        result = 0;
        string normalized = NormalizeQuestion(query);
        var rawTokens = TokenRegex().Matches(normalized).Select(match => match.Value).ToArray();
        if (rawTokens.Length == 0)
            return false;

        var tokens = new List<string>(rawTokens.Length);
        bool hasOperator = false;
        for (int i = 0; i < rawTokens.Length;)
        {
            string token = rawTokens[i];
            if (TryNormalizeOperator(rawTokens, i, out string op, out int consumed))
            {
                tokens.Add(op);
                hasOperator = true;
                i += consumed;
                continue;
            }

            if (decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
            {
                tokens.Add(token);
                i++;
                continue;
            }

            if (NumberWords.Contains(token))
            {
                if (!TryReadSpokenNumber(rawTokens, i, out decimal number, out int next))
                    return false;
                tokens.Add(number.ToString(CultureInfo.InvariantCulture));
                i = next;
                continue;
            }

            if (token is "(" or ")")
            {
                tokens.Add(token);
                i++;
                continue;
            }

            return false;
        }

        if (!hasOperator)
            return false;

        try
        {
            var parser = new ExpressionReader(tokens);
            result = parser.ReadExpression();
            return parser.AtEnd;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or DivideByZeroException)
        {
            result = 0;
            return false;
        }
    }

    private static string NormalizeQuestion(string query)
    {
        string normalized = query.Trim().ToLowerInvariant().Replace('’', '\'');
        normalized = HyphenatedWordRegex().Replace(normalized, " ");
        normalized = normalized.Replace("what's", "what is", StringComparison.Ordinal)
            .Replace("what’s", "what is", StringComparison.Ordinal);

        string[] prefixes =
        [
            "what is the result of ", "what is the answer to ", "how much is ",
            "what is the ", "what is ", "calculate ", "compute ", "solve ", "please "
        ];
        foreach (string prefix in prefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        Match command = ArithmeticCommandRegex().Match(normalized);
        if (command.Success)
        {
            string left = command.Groups["left"].Value;
            string right = command.Groups["right"].Value;
            string connector = command.Groups["connector"].Value;
            normalized = command.Groups["verb"].Value switch
            {
                "add" or "added" => $"{left} plus {right}",
                "subtract" or "subtracted" => connector == "from" ? $"{right} minus {left}" : $"{left} minus {right}",
                "multiply" or "multiplied" => $"{left} times {right}",
                "divide" or "divided" => $"{left} divided by {right}",
                _ => normalized
            };
        }
        else
        {
            Match operationPhrase = ArithmeticPhraseRegex().Match(normalized);
            if (operationPhrase.Success)
            {
                string left = operationPhrase.Groups["left"].Value;
                string right = operationPhrase.Groups["right"].Value;
                string operation = operationPhrase.Groups["verb"].Value;
                string connector = operationPhrase.Groups["connector"].Value;
                normalized = operation switch
                {
                    "addition" => $"{left} plus {right}",
                    "subtraction" => connector == "from" ? $"{right} minus {left}" : $"{left} minus {right}",
                    "multiplication" => $"{left} times {right}",
                    "division" => $"{left} divided by {right}",
                    _ => normalized
                };
            }
        }

        return normalized.Trim().TrimEnd('?', '.', '!');
    }

    private static bool TryNormalizeOperator(string[] tokens, int index, out string op, out int consumed)
    {
        string token = tokens[index];
        consumed = 1;
        op = token switch
        {
            "+" or "plus" or "add" or "added" or "addition" or "and" => "+",
            "-" or "minus" or "subtract" or "subtracted" or "subtraction" => "-",
            "*" or "times" or "multiply" or "multiplied" or "multiplication" or "x" => "*",
            "/" or "divide" or "divided" or "division" or "over" => "/",
            _ => string.Empty
        };

        if (op.Length == 0)
            return false;

        if (token is "multiply" or "multiplied" or "divide" or "divided" &&
            index + 1 < tokens.Length && tokens[index + 1] == "by")
        {
            consumed = 2;
        }
        else if (token == "added" && index + 1 < tokens.Length && tokens[index + 1] == "to")
        {
            consumed = 2;
        }

        return true;
    }

    private static bool TryReadSpokenNumber(string[] tokens, int start, out decimal value, out int next)
    {
        value = 0;
        next = start;
        decimal total = 0;
        decimal current = 0;
        bool sawNumber = false;
        bool allowAnd = false;

        while (next < tokens.Length)
        {
            string word = tokens[next];
            if (word == "and")
            {
                if (!allowAnd || next + 1 >= tokens.Length || !NumberWords.Contains(tokens[next + 1]) || tokens[next + 1] == "and")
                    break;
                allowAnd = false;
                next++;
                continue;
            }

            if (TryGetSmallNumber(word, out int small))
            {
                current += small;
                sawNumber = true;
                allowAnd = false;
                next++;
                continue;
            }

            if (TryGetTens(word, out int tens))
            {
                current += tens;
                sawNumber = true;
                allowAnd = false;
                next++;
                continue;
            }

            if (word == "hundred")
            {
                current = Math.Max(1, current) * 100;
                sawNumber = true;
                allowAnd = true;
                next++;
                continue;
            }

            if (TryGetScale(word, out int scale))
            {
                total += Math.Max(1, current) * scale;
                current = 0;
                sawNumber = true;
                allowAnd = true;
                next++;
                continue;
            }

            break;
        }

        if (!sawNumber)
            return false;

        value = total + current;
        return true;
    }

    private static bool TryGetSmallNumber(string word, out int value)
    {
        value = word switch
        {
            "zero" => 0, "one" => 1, "two" => 2, "three" => 3, "four" => 4,
            "five" => 5, "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9,
            "ten" => 10, "eleven" => 11, "twelve" => 12, "thirteen" => 13,
            "fourteen" => 14, "fifteen" => 15, "sixteen" => 16, "seventeen" => 17,
            "eighteen" => 18, "nineteen" => 19, _ => -1
        };
        return value >= 0;
    }

    private static bool TryGetTens(string word, out int value)
    {
        value = word switch
        {
            "twenty" => 20, "thirty" => 30, "forty" => 40, "fifty" => 50,
            "sixty" => 60, "seventy" => 70, "eighty" => 80, "ninety" => 90,
            _ => 0
        };
        return value != 0;
    }

    private static bool TryGetScale(string word, out int scale)
    {
        scale = word switch
        {
            "thousand" => 1_000, "million" => 1_000_000, "billion" => 1_000_000_000,
            _ => 0
        };
        return scale != 0;
    }

    [GeneratedRegex(@"\d+(?:\.\d+)?|[a-z]+|[()+\-*/]")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"(?<=[a-z])-(?=[a-z])")]
    private static partial Regex HyphenatedWordRegex();

    [GeneratedRegex(@"^(?<verb>add|added|subtract|subtracted|multiply|multiplied|divide|divided)\s+(?<left>.+?)\s+(?<connector>and|from|by|to)\s+(?<right>.+)$")]
    private static partial Regex ArithmeticCommandRegex();

    [GeneratedRegex(@"^(?:the\s+)?(?<verb>addition|subtraction|multiplication|division)\s+of\s+(?<left>.+?)\s+(?<connector>and|from|by)\s+(?<right>.+)$")]
    private static partial Regex ArithmeticPhraseRegex();

    private sealed class ExpressionReader(IReadOnlyList<string> tokens)
    {
        private int _position;
        public bool AtEnd => _position == tokens.Count;

        public decimal ReadExpression()
        {
            decimal value = ReadTerm();
            while (TryReadOperator("+", "-", out string op))
            {
                decimal right = ReadTerm();
                value = op == "+" ? value + right : value - right;
            }
            return value;
        }

        private decimal ReadTerm()
        {
            decimal value = ReadUnary();
            while (TryReadOperator("*", "/", out string op))
            {
                decimal right = ReadUnary();
                value = op == "*" ? value * right : value / right;
            }
            return value;
        }

        private decimal ReadUnary()
        {
            if (TryReadOperator("+", "-", out string op))
            {
                decimal value = ReadUnary();
                return op == "-" ? -value : value;
            }
            return ReadPrimary();
        }

        private decimal ReadPrimary()
        {
            if (_position >= tokens.Count)
                throw new FormatException("Expected a number.");

            string token = tokens[_position++];
            if (token == "(")
            {
                decimal value = ReadExpression();
                if (_position >= tokens.Count || tokens[_position++] != ")")
                    throw new FormatException("Unclosed parenthesis.");
                return value;
            }

            if (!decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
                throw new FormatException("Expected a number.");
            return number;
        }

        private bool TryReadOperator(string first, string second, out string op)
        {
            if (_position < tokens.Count && (tokens[_position] == first || tokens[_position] == second))
            {
                op = tokens[_position++];
                return true;
            }
            op = string.Empty;
            return false;
        }
    }
}
