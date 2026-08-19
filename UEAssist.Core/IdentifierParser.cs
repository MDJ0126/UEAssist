using System;

namespace UEAssist.Core
{
    public static class IdentifierParser
    {
        public static string At(string line, int zeroBasedColumn)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            var column = Math.Max(0, Math.Min(zeroBasedColumn, line.Length));
            if (column == line.Length || !IsIdentifierCharacter(line[column]))
            {
                column--;
            }

            if (column < 0 || !IsIdentifierCharacter(line[column]))
            {
                return string.Empty;
            }

            var start = column;
            while (start > 0 && IsIdentifierCharacter(line[start - 1]))
            {
                start--;
            }

            var end = column + 1;
            while (end < line.Length && IsIdentifierCharacter(line[end]))
            {
                end++;
            }

            return line.Substring(start, end - start);
        }

        private static bool IsIdentifierCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }
    }
}
