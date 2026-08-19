using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UEAssist.Core
{
    public enum SemanticTokenKind
    {
        Type,
        Variable
    }

    public sealed class SemanticToken
    {
        public SemanticToken(string name, int start, int length, SemanticTokenKind kind)
        {
            Name = name;
            Start = start;
            Length = length;
            Kind = kind;
        }

        public string Name { get; }
        public int Start { get; }
        public int Length { get; }
        public SemanticTokenKind Kind { get; }
    }

    public static class CppSemanticParser
    {
        private static readonly Regex DeclaredTypePattern = new Regex(
            @"\b(?:class|struct|enum(?:\s+class)?)\s+(?:\w+_API\s+)?(?<name>[A-Za-z_]\w*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex UnrealTypePattern = new Regex(
            @"\b(?<name>(?:[AUFTEI][A-Z][A-Za-z0-9_]*))\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex UnrealClassInheritancePattern = new Regex(
            @"\bclass\s+(?:\w+_API\s+)?[A-Za-z_]\w*\s*:\s*(?:public|protected|private)\s+[A-Za-z_]\w*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex GeneratedSuperUsagePattern = new Regex(
            @"\bSuper\s*::",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex VariableDeclarationPattern = new Regex(
            @"\b(?:const\s+)?(?:auto|bool|char|short|int|long|float|double|int\d+|uint\d+|[AUFTEI][A-Z]\w*|[A-Za-z_]\w*\s*<[^;{}()]+>)\s*[*&]?\s*(?<name>[a-zA-Z_]\w*)\s*(?=[=;,\)\[])" ,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<SemanticToken> Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<SemanticToken>();
            }

            var typeNames = CollectNames(text, DeclaredTypePattern);
            foreach (Match match in UnrealTypePattern.Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (!IsUnrealMacro(name))
                {
                    typeNames.Add(name);
                }
            }

            if (GeneratedSuperUsagePattern.IsMatch(text)
                || (UnrealClassInheritancePattern.IsMatch(text)
                    && (text.IndexOf("GENERATED_BODY", StringComparison.Ordinal) >= 0
                        || text.IndexOf("UCLASS", StringComparison.Ordinal) >= 0)))
            {
                typeNames.Add("Super");
            }

            var variableNames = CollectNames(text, VariableDeclarationPattern);
            variableNames.ExceptWith(typeNames);

            var tokens = new List<SemanticToken>();
            AddOccurrences(text, typeNames, SemanticTokenKind.Type, tokens);
            AddOccurrences(text, variableNames, SemanticTokenKind.Variable, tokens);
            return tokens.OrderBy(token => token.Start).ToArray();
        }

        private static HashSet<string> CollectNames(string text, Regex pattern)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in pattern.Matches(text))
            {
                names.Add(match.Groups["name"].Value);
            }

            return names;
        }

        private static void AddOccurrences(string text, IEnumerable<string> names, SemanticTokenKind kind, ICollection<SemanticToken> tokens)
        {
            foreach (var name in names)
            {
                foreach (Match match in Regex.Matches(text, @"\b" + Regex.Escape(name) + @"\b"))
                {
                    tokens.Add(new SemanticToken(name, match.Index, match.Length, kind));
                }
            }
        }

        private static bool IsUnrealMacro(string name)
        {
            return UnrealMacroParser.Find(name).Count > 0;
        }
    }
}
