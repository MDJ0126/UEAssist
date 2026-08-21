using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UEAssist.Core;

namespace UEAssist.Indexing
{
    public sealed class PersistentSymbolIndex
    {
        private static readonly HashSet<string> SourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".h", ".hpp", ".hh", ".inl", ".cpp", ".cc", ".cxx" };

        private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".git", ".vs", "Binaries", "DerivedDataCache", "Intermediate", "Saved" };

        private static readonly Dictionary<string, string> BuiltInReturnTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "GetWorld", "UWorld*" }
            };

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

        private static readonly Regex ConstructedVariablePattern = new Regex(
            @"\b(?<type>[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?\s*[*&]?)\s+(?<name>[A-Za-z_]\w*)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ModuleNamePattern = new Regex(
            "\\\"(?<name>[A-Za-z_]\\w*)\\\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly object gate = new object();
        private List<IndexedSymbol> symbols = new List<IndexedSymbol>();
        private Dictionary<string, List<IndexedSymbol>> completionsByPrefix = new Dictionary<string, List<IndexedSymbol>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<IndexedSymbol>> membersByOwner = new Dictionary<string, List<IndexedSymbol>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> typeNamesByInsensitiveName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> variableTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> returnTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> variableTypesByOwnerAndName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> returnTypesByOwnerAndName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> baseTypesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<IndexedSymbol>> definitionsByName = new Dictionary<string, List<IndexedSymbol>>(StringComparer.OrdinalIgnoreCase);
        private List<IndexedSymbol> specifierSymbols = new List<IndexedSymbol>();
        private List<IndexedSymbol> headerSymbols = new List<IndexedSymbol>();

        public string ProjectRoot { get; private set; }
        public string EngineRoot { get; private set; }
        public DateTime LastUpdatedUtc { get; private set; }

        public int Count
        {
            get { lock (gate) return symbols.Count; }
        }

        public void Build(string projectRoot, string engineRoot = null)
        {
            var files = new List<string>(EnumerateProjectFiles(projectRoot));

            if (!string.IsNullOrWhiteSpace(engineRoot) && Directory.Exists(engineRoot))
            {
                files.AddRange(EnumerateEngineApiFiles(engineRoot, CollectEngineModules(projectRoot)));
            }

            BuildFiles(files, projectRoot, engineRoot);
        }

        public void BuildProject(string projectRoot)
        {
            BuildFiles(EnumerateProjectFiles(projectRoot), projectRoot, null);
        }

        public void BuildEngine(string engineRoot, string projectRoot)
        {
            var modules = CollectEngineModules(projectRoot);
            BuildFiles(EnumerateEngineApiFiles(engineRoot, modules), null, engineRoot);
        }

        public void LoadBuiltInApi()
        {
            var builtIns = new[]
            {
                Type("UObject"), Type("AActor", "UObject"), Type("UActorComponent", "UObject"),
                Type("USceneComponent", "UActorComponent"), Type("UWorld", "UObject"),
                Type("UGameplayStatics", "UObject"), Type("UInputMappingContext", "UObject"),
                Type("UInputAction", "UObject"), Type("FVector"), Type("FRotator"),
                Macro("UCLASS"), Macro("USTRUCT"), Macro("UENUM"), Macro("UINTERFACE"),
                Macro("UFUNCTION"), Macro("UPROPERTY"), Macro("UMETA"), Macro("UPARAM"),
                Macro("GENERATED_BODY"), Macro("GENERATED_UCLASS_BODY"), Macro("GENERATED_USTRUCT_BODY"),
                Specifier("VisibleAnywhere"), Specifier("VisibleDefaultsOnly"), Specifier("EditAnywhere"),
                Specifier("EditDefaultsOnly"), Specifier("EditInstanceOnly"), Specifier("BlueprintReadOnly"),
                Specifier("BlueprintReadWrite"), Specifier("BlueprintAssignable"), Specifier("BlueprintCallable"),
                Specifier("BlueprintPure"), Specifier("Category"), Specifier("meta"), Specifier("Transient"),
                Specifier("Replicated"), Specifier("ReplicatedUsing"), Specifier("SaveGame"), Specifier("Config"),
                Specifier("Instanced"), Specifier("AdvancedDisplay"), Specifier("DisplayName"), Specifier("ClampMin"),
                Specifier("ClampMax"), Specifier("AllowPrivateAccess"),
                Header("CoreMinimal.h"), Header("GameFramework/Actor.h"), Header("GameFramework/Pawn.h"),
                Header("Camera/CameraComponent.h"), Header("GameFramework/SpringArmComponent.h"),
                Header("Components/CapsuleComponent.h"), Header("Components/StaticMeshComponent.h"),
                Header("InputMappingContext.h"), Header("InputAction.h"),
                Function("AActor", "Destroy", "bool"), Function("AActor", "SetLifeSpan", "void"),
                Function("AActor", "GetWorld", "UWorld*"), Function("AActor", "GetActorLocation", "FVector"),
                Function("AActor", "SetActorLocation", "bool"), Function("AActor", "GetActorRotation", "FRotator"),
                Function("AActor", "Tick", "void"), Function("AActor", "BeginPlay", "void"),
                Function("UWorld", "SpawnActor", "AActor*"), Function("UWorld", "DestroyActor", "bool"),
                Function("UObject", "GetWorld", "UWorld*"),
                Function("UGameplayStatics", "GetActorOfClass", "AActor*"),
                Function("UGameplayStatics", "GetAllActorsOfClass", "void")
            };
            ApplySymbols(builtIns.ToList(), null, null, DateTime.UtcNow);
        }

        public void ReplaceWith(params PersistentSymbolIndex[] indexes)
        {
            var merged = indexes
                .Where(index => index != null)
                .SelectMany(index => index.Snapshot())
                .GroupBy(item => string.Join("|", item.Name, item.Kind, item.FilePath, item.Line, item.OwnerType), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            ApplySymbols(
                merged,
                indexes.FirstOrDefault(index => !string.IsNullOrWhiteSpace(index?.ProjectRoot))?.ProjectRoot,
                indexes.FirstOrDefault(index => !string.IsNullOrWhiteSpace(index?.EngineRoot))?.EngineRoot,
                indexes.Where(index => index != null).Select(index => index.LastUpdatedUtc).DefaultIfEmpty().Max());
        }

        private void BuildFiles(IEnumerable<string> files, string projectRoot, string engineRoot)
        {
            var discovered = new ConcurrentBag<IndexedSymbol>();
            Parallel.ForEach(
                files.Distinct(StringComparer.OrdinalIgnoreCase),
                CreateParallelOptions(),
                file => ParseFile(file, discovered.Add));

            var built = discovered
                .GroupBy(item => string.Join("|", item.Name, item.Kind, item.FilePath, item.Line, item.OwnerType), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            ApplySymbols(built, projectRoot, engineRoot, DateTime.UtcNow);
        }

        private IndexedSymbol[] Snapshot()
        {
            lock (gate) return symbols.ToArray();
        }

        private static IndexedSymbol Type(string name, string baseType = null)
        {
            return new IndexedSymbol(name, SymbolKind.Type, string.Empty, 0, 0, baseType: baseType);
        }

        private static IndexedSymbol Function(string owner, string name, string returnType)
        {
            return new IndexedSymbol(name, SymbolKind.Function, string.Empty, 0, 0, owner, returnType);
        }

        private static IndexedSymbol Macro(string name)
        {
            return new IndexedSymbol(name, SymbolKind.Macro, string.Empty, 0, 0);
        }

        private static IndexedSymbol Specifier(string name)
        {
            return new IndexedSymbol(name, SymbolKind.Specifier, string.Empty, 0, 0);
        }

        private static IndexedSymbol Header(string includePath)
        {
            return new IndexedSymbol(includePath, SymbolKind.Header, string.Empty, 0, 0);
        }

        public IReadOnlyList<IndexedSymbol> CompleteSpecifiers(string prefix, int limit = 100)
        {
            prefix = prefix ?? string.Empty;
            lock (gate)
            {
                return specifierSymbols
                    .Where(item => item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => CompletionMatchRank(item.Name, prefix))
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray();
            }
        }

        public IReadOnlyList<IndexedSymbol> CompleteHeaders(string prefix, int limit = 100)
        {
            prefix = prefix ?? string.Empty;
            lock (gate)
            {
                return headerSymbols
                    .Select(item => new { Item = item, Rank = HeaderMatchRank(item.Name, prefix) })
                    .Where(value => value.Rank < int.MaxValue)
                    .OrderBy(value => value.Rank)
                    .ThenBy(value => value.Item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(value => value.Item)
                    .Take(limit)
                    .ToArray();
            }
        }

        public IReadOnlyList<IndexedSymbol> Complete(string prefix, int limit = 200)
        {
            prefix = prefix ?? string.Empty;
            lock (gate)
            {
                IEnumerable<IndexedSymbol> pool = symbols;
                if (prefix.Length > 0 && completionsByPrefix.TryGetValue(GetPrefixKey(prefix), out var indexedPool))
                {
                    pool = indexedPool;
                }

                var names = new HashSet<string>(StringComparer.Ordinal);
                var results = new List<IndexedSymbol>();
                foreach (var item in pool)
                {
                    if (!item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !names.Add(item.Name)) continue;
                    results.Add(item);
                }
                return results
                    .OrderBy(item => CompletionMatchRank(item.Name, prefix))
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray();
            }
        }

        public IReadOnlyList<IndexedSymbol> CompleteMembers(string typeName, string prefix, int limit = 200)
        {
            typeName = NormalizeTypeName(typeName);
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
                        if (MemberMatchRank(item.Name, query) == int.MaxValue || !names.Add(item.Name)) continue;
                        results.Add(item);
                    }
                }
                return results
                    .OrderBy(item => MemberMatchRank(item.Name, query))
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
                    .ToArray();
            }
        }

        public static string NormalizeTypeName(string typeName)
        {
            var normalized = Regex.Replace(typeName ?? string.Empty, @"\b(?:const|class|struct)\b|[*&\s]", string.Empty);
            var wrappers = new HashSet<string>(StringComparer.Ordinal)
            {
                "TObjectPtr", "TWeakObjectPtr", "TSoftObjectPtr", "TSharedPtr", "TSharedRef", "TUniquePtr"
            };
            while (true)
            {
                var match = Regex.Match(normalized, @"^(?<wrapper>[A-Za-z_]\w*)<(?<inner>[^,>]+)(?:,[^>]*)?>$");
                if (!match.Success || !wrappers.Contains(match.Groups["wrapper"].Value)) return normalized;
                normalized = Regex.Replace(match.Groups["inner"].Value, @"\b(?:const|class|struct)\b|[*&\s]", string.Empty);
            }
        }

        public string ResolveVariableType(string variableName)
        {
            lock (gate)
            {
                return variableTypesByName.TryGetValue(variableName, out var typeName) ? typeName : null;
            }
        }

        public string ResolveVariableType(string ownerType, string variableName)
        {
            foreach (var owner in GetTypeHierarchy(NormalizeTypeName(ownerType)))
            {
                lock (gate)
                {
                    if (variableTypesByOwnerAndName.TryGetValue(MemberKey(owner, variableName), out var typeName))
                        return typeName;
                }
            }
            return ResolveVariableType(variableName);
        }

        public string ResolveReturnType(string functionName)
        {
            lock (gate)
            {
                // Core Unreal functions must not be displaced by incomplete macro-heavy
                // declarations discovered while scanning engine headers.
                if (BuiltInReturnTypes.TryGetValue(functionName, out var builtInType))
                {
                    return builtInType;
                }

                if (returnTypesByName.TryGetValue(functionName, out var typeName) &&
                    !string.IsNullOrWhiteSpace(typeName))
                {
                    return typeName;
                }
                return null;
            }
        }

        public string ResolveReturnType(string ownerType, string functionName)
        {
            foreach (var owner in GetTypeHierarchy(NormalizeTypeName(ownerType)))
            {
                lock (gate)
                {
                    if (returnTypesByOwnerAndName.TryGetValue(MemberKey(owner, functionName), out var typeName))
                        return typeName;
                }
            }
            return ResolveReturnType(functionName);
        }

        public string FindCorrectTypeCasing(string name)
        {
            lock (gate)
            {
                return typeNamesByInsensitiveName.TryGetValue(name, out var correct) && !correct.Equals(name, StringComparison.Ordinal)
                    ? correct
                    : null;
            }
        }

        public string ResolveBaseType(string typeName)
        {
            lock (gate)
            {
                return baseTypesByName.TryGetValue(NormalizeTypeName(typeName), out var baseType) ? baseType : null;
            }
        }

        public IReadOnlyList<SourceSymbol> FindDefinitions(string name)
        {
            lock (gate)
            {
                return definitionsByName.TryGetValue(name, out var definitions)
                    ? definitions.Where(item => !string.IsNullOrWhiteSpace(item.FilePath)).Select(ToSourceSymbol).ToArray()
                    : Array.Empty<SourceSymbol>();
            }
        }

        public bool ContainsSymbol(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (gate) return definitionsByName.ContainsKey(name);
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

            var results = new ConcurrentBag<SourceSymbol>();
            var pattern = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
            Parallel.ForEach(files, CreateParallelOptions(), file =>
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (IOException) { return; }
                catch (UnauthorizedAccessException) { return; }

                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = StripLineComment(lines[lineIndex]);
                    foreach (Match match in pattern.Matches(line))
                    {
                        results.Add(new SourceSymbol(name, file, lineIndex + 1, match.Index + 1, SymbolKind.Variable));
                    }
                }
            });

            return results.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Line).ThenBy(item => item.Column).ToArray();
        }

        public void Save(string cachePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            List<string> lines;
            lock (gate)
            {
                lines = new List<string> { "UEASSIST4", Encode(ProjectRoot), Encode(EngineRoot), LastUpdatedUtc.Ticks.ToString() };
                lines.AddRange(symbols.Select(item => string.Join("\t", Encode(item.Name), (int)item.Kind, Encode(item.FilePath), item.Line, item.Column, Encode(item.OwnerType), Encode(item.ValueType), Encode(item.BaseType))));
            }
            var temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, lines, Encoding.UTF8);
                if (File.Exists(cachePath)) File.Replace(temporaryPath, cachePath, null);
                else File.Move(temporaryPath, cachePath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public bool Load(string cachePath)
        {
            if (!File.Exists(cachePath)) return false;
            try
            {
                var lines = File.ReadAllLines(cachePath, Encoding.UTF8);
                if (lines.Length < 4 || lines[0] != "UEASSIST4") return false;
                var loaded = new List<IndexedSymbol>();
                foreach (var line in lines.Skip(4))
                {
                    var fields = line.Split('\t');
                    if (fields.Length != 8) continue;
                    loaded.Add(new IndexedSymbol(Decode(fields[0]), (SymbolKind)int.Parse(fields[1]), Decode(fields[2]), int.Parse(fields[3]), int.Parse(fields[4]), Decode(fields[5]), Decode(fields[6]), Decode(fields[7])));
                }
                ApplySymbols(
                    loaded,
                    Decode(lines[1]),
                    Decode(lines[2]),
                    new DateTime(long.Parse(lines[3]), DateTimeKind.Utc));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            {
                return false;
            }
        }

        private HashSet<string> GetTypeHierarchy(string typeName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { typeName };
            lock (gate)
            {
                var current = typeName;
                while (true)
                {
                    var baseType = baseTypesByName.TryGetValue(current, out var indexedBaseType) ? indexedBaseType : null;
                    if (string.IsNullOrWhiteSpace(baseType) || !result.Add(baseType)) break;
                    current = baseType;
                }
            }
            return result;
        }

        private static void ParseFile(string filePath, Action<IndexedSymbol> addSymbol)
        {
            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            var typeScopes = new Stack<TypeScope>();
            string pendingType = null;
            var braceDepth = 0;
            var insideBlockComment = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = StripComments(lines[index], ref insideBlockComment);
                var typeMatch = TypePattern.Match(line);
                if (typeMatch.Success)
                {
                    var typeName = typeMatch.Groups["name"].Value;
                    addSymbol(new IndexedSymbol(typeName, SymbolKind.Type, filePath, index + 1, typeMatch.Groups["name"].Index + 1, baseType: typeMatch.Groups["base"].Value));
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

                var constructedVariableMatch = ConstructedVariablePattern.Match(line);
                var insideExecutableScope = braceDepth > (typeScopes.Count == 0 ? 0 : typeScopes.Peek().Depth);
                if (insideExecutableScope && constructedVariableMatch.Success && line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                {
                    addSymbol(new IndexedSymbol(
                        constructedVariableMatch.Groups["name"].Value,
                        SymbolKind.Variable,
                        filePath,
                        index + 1,
                        constructedVariableMatch.Groups["name"].Index + 1,
                        typeScopes.Count == 0 ? null : typeScopes.Peek().Name,
                        constructedVariableMatch.Groups["type"].Value.Trim()));
                }
                else
                {
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
                            addSymbol(new IndexedSymbol(name, SymbolKind.Function, filePath, index + 1, selectedFunction.Groups["name"].Index + 1, owner, valueType));
                        }
                    }
                    else if (!line.Contains("("))
                    {
                        var variableMatch = VariablePattern.Match(line);
                        if (variableMatch.Success)
                        {
                            addSymbol(new IndexedSymbol(variableMatch.Groups["name"].Value, SymbolKind.Variable, filePath, index + 1, variableMatch.Groups["name"].Index + 1, typeScopes.Count == 0 ? null : typeScopes.Peek().Name, variableMatch.Groups["type"].Value.Trim()));
                        }
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
            foreach (var moduleRoot in EnumerateEngineModuleRoots(engineRoot, modules))
            {
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

        private static IEnumerable<string> EnumerateEngineModuleRoots(string engineRoot, ISet<string> modules)
        {
            var runtimeRoot = Path.Combine(engineRoot, "Engine", "Source", "Runtime");
            foreach (var module in modules)
            {
                var runtimeModule = Path.Combine(runtimeRoot, module);
                if (Directory.Exists(runtimeModule)) yield return runtimeModule;
            }

            // Plugin APIs (for example Enhanced Input) live below
            // Engine/Plugins/.../Source/<Module>, not Engine/Source/Runtime.
            // Walk directories only, stop below a matched module, and parse just
            // its Public/Classes headers so the background scan remains bounded.
            var pluginsRoot = Path.Combine(engineRoot, "Engine", "Plugins");
            if (!Directory.Exists(pluginsRoot)) yield break;
            var pending = new Stack<string>();
            pending.Push(pluginsRoot);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                var parent = Path.GetDirectoryName(directory);
                if (modules.Contains(Path.GetFileName(directory)) &&
                    string.Equals(Path.GetFileName(parent), "Source", StringComparison.OrdinalIgnoreCase))
                {
                    yield return directory;
                    continue;
                }

                string[] children;
                try { children = Directory.GetDirectories(directory); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var child in children) pending.Push(child);
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

        private static string StripComments(string line, ref bool insideBlockComment)
        {
            var result = new StringBuilder(line?.Length ?? 0);
            var index = 0;
            while (index < (line?.Length ?? 0))
            {
                if (insideBlockComment)
                {
                    var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0) return result.ToString();
                    insideBlockComment = false;
                    index = end + 2;
                    continue;
                }
                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/') break;
                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    insideBlockComment = true;
                    index += 2;
                    continue;
                }
                result.Append(line[index]);
                index++;
            }
            return result.ToString();
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

        private void ApplySymbols(List<IndexedSymbol> newSymbols, string projectRoot, string engineRoot, DateTime updatedUtc)
        {
            var lookups = BuildLookups(newSymbols);
            lock (gate)
            {
                symbols = newSymbols;
                completionsByPrefix = lookups.CompletionsByPrefix;
                membersByOwner = lookups.MembersByOwner;
                typeNamesByInsensitiveName = lookups.TypeNamesByInsensitiveName;
                variableTypesByName = lookups.VariableTypesByName;
                returnTypesByName = lookups.ReturnTypesByName;
                variableTypesByOwnerAndName = lookups.VariableTypesByOwnerAndName;
                returnTypesByOwnerAndName = lookups.ReturnTypesByOwnerAndName;
                baseTypesByName = lookups.BaseTypesByName;
                definitionsByName = lookups.DefinitionsByName;
                specifierSymbols = lookups.Specifiers;
                headerSymbols = lookups.Headers;
                ProjectRoot = projectRoot;
                EngineRoot = engineRoot;
                LastUpdatedUtc = updatedUtc;
            }
        }

        private static LookupTables BuildLookups(List<IndexedSymbol> sourceSymbols)
        {
            var distinctCompletions = sourceSymbols
                .Where(item => !string.IsNullOrEmpty(item.Name) && item.Kind != SymbolKind.Specifier && item.Kind != SymbolKind.Header)
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .Select(group => group.OrderBy(item => item.Kind).First())
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var completions = new Dictionary<string, List<IndexedSymbol>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in distinctCompletions)
            {
                for (var length = 1; length <= Math.Min(2, item.Name.Length); length++)
                {
                    var key = item.Name.Substring(0, length);
                    if (!completions.TryGetValue(key, out var values))
                    {
                        values = new List<IndexedSymbol>();
                        completions[key] = values;
                    }
                    values.Add(item);
                }
            }
            var members = sourceSymbols
                .Where(item => !string.IsNullOrEmpty(item.OwnerType))
                .GroupBy(item => item.OwnerType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var typeNames = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Type && !string.IsNullOrEmpty(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
            var variableTypes = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Variable && !string.IsNullOrEmpty(item.ValueType))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().ValueType, StringComparer.OrdinalIgnoreCase);
            var ownerVariableTypes = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Variable && !string.IsNullOrEmpty(item.OwnerType) && !string.IsNullOrEmpty(item.ValueType))
                .GroupBy(item => MemberKey(item.OwnerType, item.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().ValueType, StringComparer.OrdinalIgnoreCase);
            var returnTypes = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Function && !string.IsNullOrEmpty(item.ValueType))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => SelectReturnType(group),
                    StringComparer.OrdinalIgnoreCase);
            var ownerReturnTypes = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Function && !string.IsNullOrEmpty(item.OwnerType) && !string.IsNullOrEmpty(item.ValueType))
                .GroupBy(item => MemberKey(item.OwnerType, item.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => SelectReturnType(group), StringComparer.OrdinalIgnoreCase);
            var baseTypes = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Type && !string.IsNullOrEmpty(item.BaseType))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(item => item.BaseType)
                    .First(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            var definitions = sourceSymbols
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var specifiers = sourceSymbols
                .Where(item => item.Kind == SymbolKind.Specifier)
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToList();
            var headers = sourceSymbols
                .Where(item => IsHeader(item.FilePath))
                .Select(item => GetIncludePath(item.FilePath))
                .Concat(sourceSymbols.Where(item => item.Kind == SymbolKind.Header).Select(item => item.Name))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(Header)
                .ToList();
            return new LookupTables(completions, members, typeNames, variableTypes, returnTypes,
                ownerVariableTypes, ownerReturnTypes, baseTypes, definitions, specifiers, headers);
        }

        private static string SelectReturnType(IEnumerable<IndexedSymbol> candidates)
        {
            return candidates
                .OrderBy(item => IsTemplatePlaceholderType(item.ValueType) ? 1 : 0)
                .ThenBy(item => string.Equals(item.ValueType, item.OwnerType, StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(item => IsHeader(item.FilePath) ? 0 : 1)
                .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.ValueType)
                .First();
        }

        private static bool IsTemplatePlaceholderType(string typeName)
        {
            var normalized = NormalizeTypeName(typeName);
            return normalized == "T" || Regex.IsMatch(normalized, @"^T[A-Z][A-Za-z0-9_]*$");
        }

        private static string MemberKey(string ownerType, string memberName)
        {
            return (ownerType ?? string.Empty) + "\0" + (memberName ?? string.Empty);
        }

        private sealed class LookupTables
        {
            public LookupTables(
                Dictionary<string, List<IndexedSymbol>> completions,
                Dictionary<string, List<IndexedSymbol>> members,
                Dictionary<string, string> typeNames,
                Dictionary<string, string> variableTypes,
                Dictionary<string, string> returnTypes,
                Dictionary<string, string> variableTypesByOwnerAndName,
                Dictionary<string, string> returnTypesByOwnerAndName,
                Dictionary<string, string> baseTypes,
                Dictionary<string, List<IndexedSymbol>> definitions,
                List<IndexedSymbol> specifiers,
                List<IndexedSymbol> headers)
            {
                CompletionsByPrefix = completions;
                MembersByOwner = members;
                TypeNamesByInsensitiveName = typeNames;
                VariableTypesByName = variableTypes;
                ReturnTypesByName = returnTypes;
                VariableTypesByOwnerAndName = variableTypesByOwnerAndName;
                ReturnTypesByOwnerAndName = returnTypesByOwnerAndName;
                BaseTypesByName = baseTypes;
                DefinitionsByName = definitions;
                Specifiers = specifiers;
                Headers = headers;
            }

            public Dictionary<string, List<IndexedSymbol>> CompletionsByPrefix { get; }
            public Dictionary<string, List<IndexedSymbol>> MembersByOwner { get; }
            public Dictionary<string, string> TypeNamesByInsensitiveName { get; }
            public Dictionary<string, string> VariableTypesByName { get; }
            public Dictionary<string, string> ReturnTypesByName { get; }
            public Dictionary<string, string> VariableTypesByOwnerAndName { get; }
            public Dictionary<string, string> ReturnTypesByOwnerAndName { get; }
            public Dictionary<string, string> BaseTypesByName { get; }
            public Dictionary<string, List<IndexedSymbol>> DefinitionsByName { get; }
            public List<IndexedSymbol> Specifiers { get; }
            public List<IndexedSymbol> Headers { get; }
        }

        private static string GetPrefixKey(string prefix)
        {
            return prefix.Substring(0, Math.Min(2, prefix.Length));
        }

        private static bool IsHeader(string filePath)
        {
            var extension = Path.GetExtension(filePath ?? string.Empty);
            return extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".hh", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".inl", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetIncludePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            var normalized = filePath.Replace('\\', '/');
            foreach (var marker in new[] { "/Public/", "/Classes/" })
            {
                var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0) return normalized.Substring(markerIndex + marker.Length);
            }

            var sourceIndex = normalized.IndexOf("/Source/", StringComparison.OrdinalIgnoreCase);
            if (sourceIndex >= 0)
            {
                var moduleStart = sourceIndex + "/Source/".Length;
                var moduleEnd = normalized.IndexOf('/', moduleStart);
                if (moduleEnd >= 0 && moduleEnd + 1 < normalized.Length) return normalized.Substring(moduleEnd + 1);
            }
            return Path.GetFileName(filePath);
        }

        private static int HeaderMatchRank(string path, string query)
        {
            if (string.IsNullOrEmpty(query)) return 3;
            if (path.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (Path.GetFileName(path).StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return int.MaxValue;
        }

        private static ParallelOptions CreateParallelOptions()
        {
            return new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(2, Math.Max(1, Environment.ProcessorCount))
            };
        }

        private static int MatchRank(string name, string query)
        {
            if (string.IsNullOrEmpty(query)) return 2;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 3;
        }

        private static int CompletionMatchRank(string name, string query)
        {
            if (name.Equals(query, StringComparison.Ordinal)) return 0;
            if (name.StartsWith(query, StringComparison.Ordinal)) return 1;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }

        private static int MemberMatchRank(string name, string query)
        {
            if (string.IsNullOrEmpty(query)) return 4;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 2;

            var queryIndex = 0;
            for (var index = 0; index < name.Length && queryIndex < query.Length; index++)
            {
                if (char.ToUpperInvariant(name[index]) == char.ToUpperInvariant(query[queryIndex])) queryIndex++;
            }
            return queryIndex == query.Length ? 3 : int.MaxValue;
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
