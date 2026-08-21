using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UEAssist.Core
{
    public sealed class ScopeIssue
    {
        public ScopeIssue(string name, int start, int length, string message)
        {
            Name = name;
            Start = start;
            Length = length;
            Message = message;
        }
        public string Name { get; }
        public int Start { get; }
        public int Length { get; }
        public string Message { get; }
    }

    public static class CppScopeAnalyzer
    {
        private static readonly Regex Declaration = new Regex(
            @"\b(?<type>(?:const\s+)?[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?\s*[*&]?)\s+(?<name>[A-Za-z_]\w*)\s*(?=[=;,\[)])" ,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BareArgument = new Regex(
            @"(?:\(|,)\s*(?<name>[A-Za-z_]\w*)\s*(?=,|\))", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Identifier = new Regex(@"\b[A-Za-z_]\w*\b", RegexOptions.Compiled);
        private static readonly Regex FunctionBrace = new Regex(@"\)\s*(?:const\s*)?(?:override\s*)?$", RegexOptions.Compiled);
        private static readonly Regex ControlBrace = new Regex(@"\b(?:if|for|while|switch|catch)\s*\([^{}]*\)\s*$", RegexOptions.Compiled);
        private static readonly HashSet<string> InvalidTypes = new HashSet<string>(StringComparer.Ordinal)
            { "return", "case", "delete", "new", "throw", "sizeof" };

        public static IReadOnlyList<ScopeIssue> FindOutOfScopeUses(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<ScopeIssue>();
            var clean = MaskCommentsAndLiterals(text);
            var declarations = Declaration.Matches(clean).Cast<Match>()
                .Where(match => !InvalidTypes.Contains(match.Groups["type"].Value.Trim()))
                .ToDictionary(match => match.Groups["name"].Index, match => match.Groups["name"], EqualityComparer<int>.Default);
            var issues = new List<ScopeIssue>();
            var scopes = new Stack<Scope>();
            scopes.Push(new Scope(false));

            for (var index = 0; index < clean.Length;)
            {
                if (clean[index] == '{')
                {
                    var prefixStart = Math.Max(0, index - 240);
                    var prefix = clean.Substring(prefixStart, index - prefixStart).TrimEnd();
                    scopes.Push(new Scope(FunctionBrace.IsMatch(prefix) && !ControlBrace.IsMatch(prefix)));
                    index++;
                    continue;
                }
                if (clean[index] == '}')
                {
                    if (scopes.Count > 1)
                    {
                        var closed = scopes.Pop();
                        if (!closed.IsFunctionBoundary && scopes.Count > 1)
                        {
                            foreach (var name in closed.Declared.Concat(closed.Expired)) scopes.Peek().Expired.Add(name);
                        }
                    }
                    index++;
                    continue;
                }
                if (!char.IsLetter(clean[index]) && clean[index] != '_')
                {
                    index++;
                    continue;
                }

                var identifier = Identifier.Match(clean, index);
                if (!identifier.Success || identifier.Index != index) { index++; continue; }
                if (declarations.TryGetValue(index, out var declarationName))
                {
                    scopes.Peek().Declared.Add(declarationName.Value);
                }
                else if (!scopes.Any(scope => scope.Declared.Contains(identifier.Value)) &&
                         scopes.Any(scope => scope.Expired.Contains(identifier.Value)))
                {
                    issues.Add(new ScopeIssue(identifier.Value, identifier.Index, identifier.Length,
                        $"지역 변수 '{identifier.Value}'은(는) 선언된 범위를 벗어났습니다."));
                }
                index += identifier.Length;
            }
            return issues;
        }

        public static ISet<string> FindDeclaredNames(string text)
        {
            var clean = MaskCommentsAndLiterals(text ?? string.Empty);
            return new HashSet<string>(Declaration.Matches(clean).Cast<Match>()
                .Where(match => !InvalidTypes.Contains(match.Groups["type"].Value.Trim()))
                .Select(match => match.Groups["name"].Value), StringComparer.Ordinal);
        }

        public static IReadOnlyList<ScopeIssue> FindBareArgumentUses(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<ScopeIssue>();
            var clean = MaskCommentsAndLiterals(text);
            return BareArgument.Matches(clean).Cast<Match>()
                .Select(match => match.Groups["name"])
                .Select(group => new ScopeIssue(group.Value, group.Index, group.Length,
                    $"정의되지 않은 식별자 '{group.Value}'입니다."))
                .ToArray();
        }

        private static string MaskCommentsAndLiterals(string text)
        {
            var result = new StringBuilder(text);
            var inLine = false; var inBlock = false; var inString = false; var inCharacter = false;
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (inLine) { if (current == '\n') inLine = false; else result[index] = ' '; continue; }
                if (inBlock) { result[index] = current == '\n' ? '\n' : ' '; if (current == '*' && next == '/') { result[index + 1] = ' '; inBlock = false; index++; } continue; }
                if (!inString && !inCharacter && current == '/' && next == '/') { result[index] = result[index + 1] = ' '; inLine = true; index++; continue; }
                if (!inString && !inCharacter && current == '/' && next == '*') { result[index] = result[index + 1] = ' '; inBlock = true; index++; continue; }
                if (!inCharacter && current == '"') { inString = !inString; result[index] = ' '; continue; }
                if (!inString && current == '\'') { inCharacter = !inCharacter; result[index] = ' '; continue; }
                if (inString || inCharacter) result[index] = current == '\n' ? '\n' : ' ';
            }
            return result.ToString();
        }

        private sealed class Scope
        {
            public Scope(bool isFunctionBoundary) { IsFunctionBoundary = isFunctionBoundary; }
            public bool IsFunctionBoundary { get; }
            public HashSet<string> Declared { get; } = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Expired { get; } = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
