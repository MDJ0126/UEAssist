using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UEAssist.Indexing;

namespace UEAssist.Extension
{
    [Export]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class ProjectIndexService
    {
        private readonly object gate = new object();
        private Task buildTask;
        private string initializedProjectRoot;
        private int intelliSenseEvidence;
        private readonly Dictionary<string, IReadOnlyList<IndexedSymbol>> liveDocuments =
            new Dictionary<string, IReadOnlyList<IndexedSymbol>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex LiveTypePattern = new Regex(
            @"\b(?:class|struct)\s+(?:\w+_API\s+)?(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);
        private static readonly Regex LiveVariablePattern = new Regex(
            @"\b(?<type>(?:const\s+)?[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?\s*[*&]?)\s+(?<name>[A-Za-z_]\w*)\s*(?=[=;,\[])" , RegexOptions.Compiled);
        private static readonly Regex LiveFunctionPattern = new Regex(
            @"\b(?<type>[A-Za-z_]\w*(?:\s*<[^>]+>)?[*&]?)\s+(?<name>[A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);

        public PersistentSymbolIndex Index { get; } = new PersistentSymbolIndex();
        public bool IsBuilding { get; private set; }
        public string LastError { get; private set; }
        public event EventHandler IndexUpdated;
        public event EventHandler IntelliSenseAvailabilityChanged;
        public event EventHandler<IndexingStatusEventArgs> IndexingStatusChanged;
        public bool IntelliSenseReady { get; private set; }

        public void UpdateLiveDocument(string filePath, string text)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var found = new List<IndexedSymbol>();
            foreach (Match match in LiveTypePattern.Matches(text ?? string.Empty))
                found.Add(new IndexedSymbol(match.Groups["name"].Value, UEAssist.Core.SymbolKind.Type, filePath, 0, match.Index));
            foreach (Match match in LiveVariablePattern.Matches(text ?? string.Empty))
                found.Add(new IndexedSymbol(match.Groups["name"].Value, UEAssist.Core.SymbolKind.Variable, filePath, 0, match.Index,
                    valueType: match.Groups["type"].Value));
            foreach (Match match in LiveFunctionPattern.Matches(text ?? string.Empty))
                found.Add(new IndexedSymbol(match.Groups["name"].Value, UEAssist.Core.SymbolKind.Function, filePath, 0, match.Index,
                    valueType: match.Groups["type"].Value));
            lock (gate)
            {
                liveDocuments[filePath] = found
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToArray();
            }
        }

        public IReadOnlyList<IndexedSymbol> CompleteLive(string prefix, int limit = 100)
        {
            lock (gate)
            {
                return liveDocuments.Values.SelectMany(items => items)
                    .Where(item => item.Name.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(limit).ToArray();
            }
        }

        public bool ContainsLiveSymbol(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (gate)
            {
                return liveDocuments.Values.SelectMany(items => items)
                    .Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void Initialize(string unrealProjectPath)
        {
            if (string.IsNullOrWhiteSpace(unrealProjectPath)) return;
            var projectRoot = Path.GetDirectoryName(unrealProjectPath);
            var engineRoot = FindEngineRoot(unrealProjectPath);
            var projectCachePath = GetProjectCachePath(projectRoot);
            var engineCachePath = GetEngineCachePath(engineRoot);

            lock (gate)
            {
                if (string.Equals(initializedProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase)) return;
                if (buildTask != null && !buildTask.IsCompleted) return;
                initializedProjectRoot = projectRoot;
                IntelliSenseReady = false;
                intelliSenseEvidence = 0;
                var projectIndex = new PersistentSymbolIndex();
                var engineIndex = new PersistentSymbolIndex();
                var builtInIndex = new PersistentSymbolIndex();
                builtInIndex.LoadBuiltInApi();
                // Publish the small built-in snapshot immediately. Large project
                // and engine cache files must never be read on the editor UI thread.
                Index.ReplaceWith(projectIndex, engineIndex, builtInIndex);
                IsBuilding = true;
                ReportIndexingStatus("캐시 확인 중...", 5);
                buildTask = Task.Run(() =>
                {
                    try
                    {
                        var projectCacheLoaded = projectIndex.Load(projectCachePath);
                        var engineCacheLoaded = engineIndex.Load(engineCachePath);
                        Index.ReplaceWith(projectIndex, engineIndex, builtInIndex);
                        var changed = false;
                        if (!projectCacheLoaded || !IsCacheCurrent(projectRoot, projectCachePath))
                        {
                            ReportIndexingStatus("프로젝트 심볼 분석 중...", 20);
                            projectIndex.BuildProject(projectRoot);
                            projectIndex.Save(projectCachePath);
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(engineRoot))
                        {
                            // The existing engine cache remains searchable while the modules used by
                            // this project are scanned and merged into the shared engine installation cache.
                            var refreshedEngine = new PersistentSymbolIndex();
                            ReportIndexingStatus("Unreal Engine API 최신화 중...", 55);
                            refreshedEngine.BuildEngine(engineRoot, projectRoot);
                            ReportIndexingStatus("엔진 공용 캐시 저장 중...", 85);
                            engineIndex.ReplaceWith(engineIndex, refreshedEngine);
                            engineIndex.Save(engineCachePath);
                            changed = true;
                        }

                        Index.ReplaceWith(projectIndex, engineIndex, builtInIndex);
                        LastError = null;
                        if (changed) IndexUpdated?.Invoke(this, EventArgs.Empty);
                        ReportIndexingStatus("인덱스 준비 완료", 100, true);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        LastError = exception.Message;
                        ReportIndexingStatus("인덱싱 실패: " + exception.Message, 100, true, true);
                    }
                    finally
                    {
                        IsBuilding = false;
                    }
                });
            }
        }

        private void ReportIndexingStatus(string message, int percent, bool completed = false, bool failed = false)
        {
            IndexingStatusChanged?.Invoke(this, new IndexingStatusEventArgs(message, percent, completed, failed));
        }

        public void ReportIntelliSenseEvidence(int matchingCandidates, int expectedCandidates)
        {
            var wasReady = IntelliSenseReady;
            var requiredMatches = Math.Min(3, expectedCandidates);
            if (requiredMatches == 0 || matchingCandidates < requiredMatches)
            {
                // Readiness is not permanent. A stale or partially loaded native
                // set must immediately hand completion back to UEAssist.
                intelliSenseEvidence = 0;
                IntelliSenseReady = false;
                if (wasReady) IntelliSenseAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            intelliSenseEvidence = Math.Min(3, intelliSenseEvidence + 1);
            IntelliSenseReady = intelliSenseEvidence >= 3;
            if (IntelliSenseReady != wasReady)
            {
                IntelliSenseAvailabilityChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Refresh(string unrealProjectPath)
        {
            Initialize(unrealProjectPath);
        }

        public void InitializeFromDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var directory = Path.GetDirectoryName(filePath);
            for (var depth = 0; depth < 12 && !string.IsNullOrWhiteSpace(directory); depth++)
            {
                try
                {
                    var project = Directory.EnumerateFiles(directory, "*.uproject", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (project != null)
                    {
                        Initialize(project);
                        return;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return;
                }
                directory = Path.GetDirectoryName(directory);
            }
        }

        private static string GetProjectCachePath(string projectRoot)
        {
            return Path.Combine(GetIndexRoot(), "Projects", HashKey(projectRoot) + ".index");
        }

        private static string GetEngineCachePath(string engineRoot)
        {
            if (string.IsNullOrWhiteSpace(engineRoot)) return Path.Combine(GetIndexRoot(), "Engines", "none.index");
            var versionFile = Path.Combine(engineRoot, "Engine", "Build", "Build.version");
            string version;
            try { version = File.Exists(versionFile) ? File.ReadAllText(versionFile) : string.Empty; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { version = string.Empty; }
            return Path.Combine(GetIndexRoot(), "Engines", HashKey(engineRoot + "|" + version) + ".index");
        }

        private static string GetIndexRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UEAssist", "Indexes");
        }

        private static string HashKey(string value)
        {
            using (var sha = SHA256.Create())
            {
                var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                return string.Concat(hash.Take(10).Select(item => item.ToString("x2")));
            }
        }

        private static bool IsCacheCurrent(string projectRoot, string cachePath)
        {
            var cacheTime = File.GetLastWriteTimeUtc(cachePath);
            try
            {
                return !Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".Build.cs", StringComparison.OrdinalIgnoreCase))
                    .Any(path => File.GetLastWriteTimeUtc(path) > cacheTime);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string FindEngineRoot(string unrealProjectPath)
        {
            var association = ReadEngineAssociation(unrealProjectPath);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Epic Games\Unreal Engine\Builds"))
                {
                    if (key != null)
                    {
                        if (!string.IsNullOrWhiteSpace(association))
                        {
                            var associatedPath = key.GetValue(association) as string;
                            if (IsEngineRoot(associatedPath)) return associatedPath;
                        }
                        foreach (var name in key.GetValueNames().Reverse())
                        {
                            var path = key.GetValue(name) as string;
                            if (IsEngineRoot(path)) return path;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
            {
            }

            var epicRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
            if (!Directory.Exists(epicRoot)) return null;
            try
            {
                if (!string.IsNullOrWhiteSpace(association))
                {
                    var launcherPath = Path.Combine(epicRoot, "UE_" + association);
                    if (IsEngineRoot(launcherPath)) return launcherPath;
                }
                return Directory.GetDirectories(epicRoot, "UE_*")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(IsEngineRoot);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string ReadEngineAssociation(string unrealProjectPath)
        {
            try
            {
                var text = File.ReadAllText(unrealProjectPath);
                const string property = "\"EngineAssociation\"";
                var propertyIndex = text.IndexOf(property, StringComparison.OrdinalIgnoreCase);
                if (propertyIndex < 0) return null;
                var colon = text.IndexOf(':', propertyIndex + property.Length);
                var quote = colon < 0 ? -1 : text.IndexOf('"', colon + 1);
                var endQuote = quote < 0 ? -1 : text.IndexOf('"', quote + 1);
                return quote >= 0 && endQuote > quote ? text.Substring(quote + 1, endQuote - quote - 1) : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsEngineRoot(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, "Engine", "Source", "Runtime"));
        }
    }

    internal sealed class IndexingStatusEventArgs : EventArgs
    {
        public IndexingStatusEventArgs(string message, int percent, bool completed, bool failed)
        {
            Message = message;
            Percent = percent;
            Completed = completed;
            Failed = failed;
        }

        public string Message { get; }
        public int Percent { get; }
        public bool Completed { get; }
        public bool Failed { get; }
    }
}
