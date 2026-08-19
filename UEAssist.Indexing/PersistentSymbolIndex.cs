using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UEAssist.Core;

namespace UEAssist.Indexing
{
    public sealed class PersistentSymbolIndex
    {
        private static readonly HashSet<string> SourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".h", ".hpp", ".hh", ".inl", ".cpp", ".cc", ".cxx" };

        private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".git", ".vs", "Binaries", "DerivedDataCache", "Intermediate", "Saved" };

        private static readonly Regex TypePattern = new Regex(
            @"\b(?:class|struct)\s+(?:\w+_API\s+)?(?<name>[A-Za-z_]\w*)(?:\s*:\s*(?:public|protected|private)\s+(?<base>[A-Za-z_]\w*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FunctionPattern = new Regex(
            @"\b(?<type>[A-Za-z_]\w*(?:\s*<[^>]+>)?[*&]?)\s+(?:(?<owner>[A-Za-z_]\w*)::)?(?<name>~?[A-Za-z_]\w*)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ConstructorPattern = new Regex(
            @"\b(?<owner>[A-Za-z_]\w*)::(?<name>~?[A-Za-z_]\w*)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex VariablePattern = new Regex(
            @"\b(?<type>(?:const\s+)?[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?\s*[*&]?)\s+(?<name>[A-Za-z_]\w*)\s*(?=[=;,\[])" ,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ModuleNamePattern = new Regex(
            "\\\"(?<name>[A-Za-z_]\\w*)\\\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly object gate = new object();
        private List<IndexedSymbol> symbols = new List<IndexedSymbol>();
        private Dictionary<char, List<IndexedSymbol>> completionsByFirstCharacter = new Dictionary<char, List<IndexedSymbol>>();
        private Dictionary<string, List<IndexedSymbol>> membersByOwner = new Dictionary<string, List<IndexedSymbol>>(StringComparer.Ordinal);

        public string ProjectRoot { get; private set; }
        public string EngineRoot { get; private set; }
        public DateTime LastUpdatedUtc { get; private set; }

        public int Count
        {
            get { lock (gate) return symbols.Count; }
        }

        public void Build(string projectRoot, string engineRoot = null)
        {
            var discovered = new List<IndexedSymbol>();
            foreach (var file in EnumerateProjectFiles(projectRoot))
            {
                ParseFile(file, discovered);
            }

            if (!string.IsNullOrWhiteSpace(engineRoot) && Directory.Exists(engineRoot))
            {
                foreach (var file in EnumerateEngineApiFiles(engineRoot, CollectEngineModules(projectRoot)))
                {
                    ParseFile(file, discovered);
                }
            }

            lock (gate)
            {
                symbols = discovered
                    .GroupBy(item => string.Join("|", item.Name, item.Kind, item.FilePath, item.Line, item.OwnerType), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                RebuildLookups();
                ProjectRoot = projectRoot;
                EngineRoot = engineRoot;
                LastUpdatedUtc = DateTime.UtcNow;
            }
        }

        public IReadOnlyList<IndexedSymbol> Complete(string prefix, int limit = 200)
        {
            prefix = prefix ?? string.Empty;
            lock (gate)
            {
                IEnumerable<IndexedSymbol> pool = symbols;
                if (prefix.Length > 0 && completionsByFirstCharacter.TryGetValue(char.ToUpperInvariant(prefix[0]), out var indexedPool))
                {
                    pool = indexedPool;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var results = new List<IndexedSymbol>();
                foreach (var item in pool)
                {
                    if (!item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !names.Add(item.Name)) continue;
                    results.Add(item);
                    if (results.Count >= limit) break;
                }
                return results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public IReadOnlyList<IndexedSymbol> CompleteMembers(string typeName, string prefix, int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return Array.Empty<IndexedSymbol>();
            }

            var owners = GetTypeHierarchy(typeName);
            lock (gate)
            {
                var query = prefix ?? string.Empty;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var results = new List<IndexedSymbol>();
                foreach (var owner in owners)
                {
                    if (!membersByOwner.TryGetValue(owner, out var members)) continue;
                    foreach (var item in members)
                    {
                        if (!item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) || !names.Add(item.Name)) continue;
                        results.Add(item);
                        if (results.Count >= limit) return results.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                }
                return results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public string ResolveVariableType(string variableName)
        {
            lock (gate)
            {
                return symbols.LastOrDefault(item => item.Kind == SymbolKind.Variable && item.Name.Equals(variableName, StringComparison.Ordinal))?.ValueType;
            }
        }

        public string ResolveReturnType(string functionName)
        {
            lock (gate)
            {
                return symbols.LastOrDefault(item => item.Kind == SymbolKind.Function && item.Name.Equals(functionName, StringComparison.Ordinal))?.ValueType;
            }
        }

        public IReadOnlyList<SourceSymbol> FindDefinitions(string name)
        {
            lock (gate)
            {
                return symbols.Where(item => item.Name.Equals(name, StringComparison.Ordinal))
                    .Select(ToSourceSymbol).ToArray();
            }
        }

        public IReadOnlyList<SourceSymbol> FindReferences(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Array.Empty<SourceSymbol>();
            }

            string[] files;
            lock (gate)
            {
                files = symbols.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var results = new List<SourceSymbol>();
            var pattern = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
            foreach (var file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = StripLineComment(lines[lineIndex]);
                    foreach (Match match in pattern.Matches(line))
                    {
                        results.Add(new SourceSymbol(name, file, lineIndex + 1, match.Index + 1, SymbolKind.Variable));
                    }
                }
            }

            return results;
        }

        public void Save(string cachePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            List<string> lines;
            lock (gate)
            {
                lines = new List<string> { "UEASSIST2", Encode(ProjectRoot), Encode(EngineRoot), LastUpdatedUtc.Ticks.ToString() };
                lines.AddRange(symbols.Select(item => string.Join("\t", Encode(item.Name), (int)item.Kind, Encode(item.FilePath), item.Line, item.Column, Encode(item.OwnerType), Encode(item.ValueType), Encode(item.BaseType))));
            }
            File.WriteAllLines(cachePath, lines, Encoding.UTF8);
        }

        public bool Load(string cachePath)
        {
            if (!File.Exists(cachePath)) return false;
            try
            {
                var lines = File.ReadAllLines(cachePath, Encoding.UTF8);
                if (lines.Length < 4 || lines[0] != "UEASSIST2") return false;
                var loaded = new List<IndexedSymbol>();
                foreach (var line in lines.Skip(4))
                {
                    var fields = line.Split('\t');
                    if (fields.Length != 8) continue;
                    loaded.Add(new IndexedSymbol(Decode(fields[0]), (SymbolKind)int.Parse(fields[1]), Decode(fields[2]), int.Parse(fields[3]), int.Parse(fields[4]), Decode(fields[5]), Decode(fields[6]), Decode(fields[7])));
                }
                lock (gate)
                {
                    ProjectRoot = Decode(lines[1]);
                    EngineRoot = Decode(lines[2]);
                    LastUpdatedUtc = new DateTime(long.Parse(lines[3]), DateTimeKind.Utc);
                    symbols = loaded;
                    RebuildLookups();
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            {
                return false;
            }
        }

        private HashSet<string> GetTypeHierarchy(string typeName)
        {
            var result = new HashSet<string>(StringComparer.Ordinal) { typeName };
            lock (gate)
            {
                var current = typeName;
                while (true)
                {
                    var baseType = symbols.FirstOrDefault(item => item.Kind == SymbolKind.Type && item.Name == current)?.BaseType;
                    if (string.IsNullOrWhiteSpace(baseType) || !result.Add(baseType)) break;
                    current = baseType;
                }
            }
            return result;
        }

        private static void ParseFile(string filePath, ICollection<IndexedSymbol> output)
        {
            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            var typeScopes = new Stack<TypeScope>();
            string pendingType = null;
            var braceDepth = 0;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = StripLineComment(lines[index]);
                var typeMatch = TypePattern.Match(line);
                if (typeMatch.Success)
                {
                    var typeName = typeMatch.Groups["name"].Value;
                    output.Add(new IndexedSymbol(typeName, SymbolKind.Type, filePath, index + 1, typeMatch.Groups["name"].Index + 1, baseType: typeMatch.Groups["base"].Value));
                    var declarationTail = line.Substring(typeMatch.Index + typeMatch.Length);
                    if (declarationTail.Contains("{"))
                    {
                        typeScopes.Push(new TypeScope(typeName, braceDepth + 1));
                        pendingType = null;
                    }
                    else if (!declarationTail.Contains(";"))
                    {
                        pendingType = typeName;
                    }
                }
                else if (pendingType != null && line.Contains("{"))
                {
                    typeScopes.Push(new TypeScope(pendingType, braceDepth + 1));
                    pendingType = null;
                }

                var constructorMatch = ConstructorPattern.Match(line);
                var functionMatch = FunctionPattern.Match(line);
                var selectedFunction = constructorMatch.Success ? constructorMatch : functionMatch;
                if (selectedFunction.Success)
                {
                    var owner = selectedFunction.Groups["owner"].Success ? selectedFunction.Groups["owner"].Value : typeScopes.Count == 0 ? null : typeScopes.Peek().Name;
                    var name = selectedFunction.Groups["name"].Value;
                    if (!IsControlKeyword(name) && UnrealMacroParser.Find(name).Count == 0)
                    {
                        var valueType = selectedFunction.Groups["type"].Success ? selectedFunction.Groups["type"].Value : owner;
                        output.Add(new IndexedSymbol(name, SymbolKind.Function, filePath, index + 1, selectedFunction.Groups["name"].Index + 1, owner, valueType));
                    }
                }
                else if (!line.Contains("(") && typeScopes.Count > 0)
                {
                    var variableMatch = VariablePattern.Match(line);
                    if (variableMatch.Success)
                    {
                        output.Add(new IndexedSymbol(variableMatch.Groups["name"].Value, SymbolKind.Variable, filePath, index + 1, variableMatch.Groups["name"].Index + 1, typeScopes.Peek().Name, variableMatch.Groups["type"].Value.Trim()));
                    }
                }

                braceDepth += line.Count(character => character == '{');
                braceDepth -= line.Count(character => character == '}');
                while (typeScopes.Count > 0 && braceDepth < typeScopes.Peek().Depth)
                {
                    typeScopes.Pop();
                }
            }
        }

        private sealed class TypeScope
        {
            public TypeScope(string name, int depth)
            {
                Name = name;
                Depth = depth;
            }

            public string Name { get; }
            public int Depth { get; }
        }

        private static IEnumerable<string> EnumerateProjectFiles(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
            foreach (var file in EnumerateFiles(root, directory => !IgnoredDirectories.Contains(Path.GetFileName(directory)))) yield return file;
        }

        private static IEnumerable<string> EnumerateEngineApiFiles(string engineRoot, ISet<string> modules)
        {
            var source = Path.Combine(engineRoot, "Engine", "Source", "Runtime");
            if (!Directory.Exists(source)) yield break;
            foreach (var module in modules)
            {
                var moduleRoot = Path.Combine(source, module);
                if (!Directory.Exists(moduleRoot)) continue;
                foreach (var apiDirectory in new[] { Path.Combine(moduleRoot, "Public"), Path.Combine(moduleRoot, "Classes") })
                {
                    if (!Directory.Exists(apiDirectory)) continue;
                    foreach (var file in EnumerateFiles(apiDirectory, directory => !IgnoredDirectories.Contains(Path.GetFileName(directory))))
                    {
                        if (!file.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)) yield return file;
                    }
                }
            }
        }

        private static ISet<string> CollectEngineModules(string projectRoot)
        {
            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Core", "CoreUObject", "Engine" };
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) return modules;
            foreach (var buildFile in Directory.EnumerateFiles(projectRoot, "*.Build.cs", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(buildFile); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (Match match in ModuleNamePattern.Matches(text)) modules.Add(match.Groups["name"].Value);
            }
            return modules;
        }

        private static IEnumerable<string> EnumerateFiles(string root, Func<string, bool> includeDirectory)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                string[] files;
                string[] directories;
                try { files = Directory.GetFiles(directory); directories = Directory.GetDirectories(directory); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var file in files) if (SourceExtensions.Contains(Path.GetExtension(file))) yield return file;
                foreach (var child in directories) if (includeDirectory(child)) pending.Push(child);
            }
        }

        private static string StripLineComment(string line)
        {
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment < 0 ? line : line.Substring(0, comment);
        }

        private static bool IsControlKeyword(string name)
        {
            return name == "if" || name == "for" || name == "while" || name == "switch" || name == "catch";
        }

        private static SourceSymbol ToSourceSymbol(IndexedSymbol item)
        {
            return new SourceSymbol(item.Name, item.FilePath, item.Line, item.Column, item.Kind);
        }

        private static bool Matches(string name, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var queryIndex = 0;
            for (var index = 0; index < name.Length && queryIndex < query.Length; index++)
            {
                if (char.ToUpperInvariant(name[index]) == char.ToUpperInvariant(query[queryIndex])) queryIndex++;
            }
            return queryIndex == query.Length;
        }

        private void RebuildLookups()
        {
            completionsByFirstCharacter = symbols
                .Where(item => !string.IsNullOrEmpty(item.Name))
                .GroupBy(item => char.ToUpperInvariant(item.Name[0]))
                .ToDictionary(group => group.Key, group => group.ToList());
            membersByOwner = symbols
                .Where(item => !string.IsNullOrEmpty(item.OwnerType))
                .GroupBy(item => item.OwnerType, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        }

        private static int MatchRank(string name, string query)
        {
            if (string.IsNullOrEmpty(query)) return 2;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 3;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}
