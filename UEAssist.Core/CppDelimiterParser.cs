using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UEAssist.Core
{
    public sealed class DelimiterIssue
    {
        public DelimiterIssue(int start, int length, string message)
        {
            Start = start;
            Length = length;
            Message = message;
        }

        public int Start { get; }
        public int Length { get; }
        public string Message { get; }
    }

    public static class CppDelimiterParser
    {
        private static readonly Regex ControlPrefix = new Regex(@"\b(?:for|if|while|switch)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<DelimiterIssue> FindDefiniteIssues(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<DelimiterIssue>();
            var issues = new List<DelimiterIssue>();
            var parentheses = new Stack<int>();
            var inString = false;
            var inCharacter = false;
            var inLineComment = false;
            var inBlockComment = false;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (inLineComment)
                {
                    if (current == '\n') inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (current == '*' && next == '/') { inBlockComment = false; index++; }
                    continue;
                }
                if (!inString && !inCharacter && current == '/' && next == '/') { inLineComment = true; index++; continue; }
                if (!inString && !inCharacter && current == '/' && next == '*') { inBlockComment = true; index++; continue; }
                if (!inCharacter && current == '"' && !IsEscaped(text, index)) { inString = !inString; continue; }
                if (!inString && current == '\'' && !IsEscaped(text, index)) { inCharacter = !inCharacter; continue; }
                if (inString || inCharacter) continue;

                if (current == '(') parentheses.Push(index);
                else if (current == ')')
                {
                    if (parentheses.Count > 0) parentheses.Pop();
                    else issues.Add(new DelimiterIssue(index, 1, "대응하는 여는 괄호 '('가 없습니다."));
                }
                else if (current == ';' && parentheses.Count > 0 && IsEndOfLine(text, index))
                {
                    var opening = parentheses.Last();
                    var prefixStart = Math.Max(0, opening - 24);
                    var prefix = text.Substring(prefixStart, opening - prefixStart);
                    if (ControlPrefix.IsMatch(prefix)) continue;
                    issues.Add(new DelimiterIssue(opening, index - opening + 1, "문장이 끝나기 전에 닫는 괄호 ')'가 필요합니다."));
                    parentheses.Clear();
                }
            }
            return issues;
        }

        private static bool IsEscaped(string text, int position)
        {
            var slashes = 0;
            for (var index = position - 1; index >= 0 && text[index] == '\\'; index--) slashes++;
            return slashes % 2 != 0;
        }

        private static bool IsEndOfLine(string text, int position)
        {
            for (var index = position + 1; index < text.Length && text[index] != '\r' && text[index] != '\n'; index++)
            {
                if (!char.IsWhiteSpace(text[index]) && !(text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')) return false;
                if (text[index] == '/') return true;
            }
            return true;
        }
    }
}
