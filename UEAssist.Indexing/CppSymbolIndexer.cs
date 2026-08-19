using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UEAssist.Core;

namespace UEAssist.Indexing
{
    public sealed class CppSymbolIndexer
    {
        private static readonly HashSet<string> SourceExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".h", ".hpp", ".hh", ".cpp", ".cc", ".cxx" };

        private static readonly HashSet<string> IgnoredDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "Binaries", "DerivedDataCache", "Intermediate", "Saved" };

        public IReadOnlyList<SourceSymbol> Find(string rootDirectory, string symbolName)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(symbolName) || !Directory.Exists(rootDirectory))
            {
                return Array.Empty<SourceSymbol>();
            }

            var results = new List<SourceSymbol>();
            foreach (var filePath in EnumerateSourceFiles(rootDirectory))
            {
                FindInFile(filePath, symbolName, results);
            }

            return results
                .OrderBy(result => result.Kind)
                .ThenBy(result => IsHeader(result.FilePath) ? 0 : 1)
                .ThenBy(result => result.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.Line)
                .ToArray();
        }

        private static IEnumerable<string> EnumerateSourceFiles(string rootDirectory)
        {
            var pending = new Stack<string>();
            pending.Push(rootDirectory);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                IEnumerable<string> files;
                IEnumerable<string> directories;

                try
                {
                    files = Directory.EnumerateFiles(directory).ToArray();
                    directories = Directory.EnumerateDirectories(directory).ToArray();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    if (SourceExtensions.Contains(Path.GetExtension(file)))
                    {
                        yield return file;
                    }
                }

                foreach (var child in directories)
                {
                    if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        private static void FindInFile(string filePath, string symbolName, ICollection<SourceSymbol> results)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var escapedName = Regex.Escape(symbolName);
            var typePattern = new Regex(@"\b(?:class|struct|enum(?:\s+class)?)\s+(?:\w+_API\s+)?" + escapedName + @"\b");
            var functionPattern = new Regex(@"\b" + escapedName + @"\s*\(");
            var variablePattern = new Regex(@"\b" + escapedName + @"\b\s*(?:[;=,{]|\[)");

            for (var index = 0; index < lines.Length; index++)
            {
                var line = StripLineComment(lines[index]);
                var match = typePattern.Match(line);
                var kind = SymbolKind.Type;

                if (!match.Success)
                {
                    match = functionPattern.Match(line);
                    kind = SymbolKind.Function;
                }

                if (!match.Success)
                {
                    match = variablePattern.Match(line);
                    kind = SymbolKind.Variable;
                }

                if (match.Success)
                {
                    var column = line.IndexOf(symbolName, match.Index, StringComparison.Ordinal);
                    results.Add(new SourceSymbol(symbolName, filePath, index + 1, Math.Max(0, column) + 1, kind));
                }
            }
        }

        private static string StripLineComment(string line)
        {
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            return commentIndex >= 0 ? line.Substring(0, commentIndex) : line;
        }

        private static bool IsHeader(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return extension.Equals(".h", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".hh", StringComparison.OrdinalIgnoreCase);
        }
    }
}
