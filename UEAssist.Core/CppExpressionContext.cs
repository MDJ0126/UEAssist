using System;
using System.Text.RegularExpressions;

namespace UEAssist.Core
{
    public static class CppExpressionContext
    {
        private static readonly Regex QualifiedFunction = new Regex(
            @"\b(?<owner>[A-Za-z_]\w*)\s*::\s*~?[A-Za-z_]\w*\s*\([^;{}]*\)\s*(?:const\s*)?(?:override\s*)?\{",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string FindEnclosingFunctionOwner(string text, int caretPosition)
        {
            if (string.IsNullOrEmpty(text) || caretPosition <= 0) return null;
            var prefix = text.Substring(0, Math.Min(caretPosition, text.Length));
            var matches = QualifiedFunction.Matches(prefix);
            return matches.Count == 0 ? null : matches[matches.Count - 1].Groups["owner"].Value;
        }
    }
}
