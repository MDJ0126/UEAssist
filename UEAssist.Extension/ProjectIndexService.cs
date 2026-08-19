using Microsoft.Win32;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        public PersistentSymbolIndex Index { get; } = new PersistentSymbolIndex();
        public bool IsBuilding { get; private set; }
        public string LastError { get; private set; }
        public event EventHandler IndexUpdated;

        public void Initialize(string unrealProjectPath)
        {
            if (string.IsNullOrWhiteSpace(unrealProjectPath)) return;
            var projectRoot = Path.GetDirectoryName(unrealProjectPath);
            var cachePath = GetCachePath(projectRoot);
            var cacheLoaded = Index.Load(cachePath);
            if (cacheLoaded && IsCacheCurrent(projectRoot, cachePath)) return;

            lock (gate)
            {
                if (buildTask != null && !buildTask.IsCompleted) return;
                IsBuilding = true;
                buildTask = Task.Run(() =>
                {
                    try
                    {
                        Index.Build(projectRoot, FindEngineRoot());
                        Index.Save(cachePath);
                        LastError = null;
                        IndexUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        LastError = exception.Message;
                    }
                    finally
                    {
                        IsBuilding = false;
                    }
                });
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

        private static string GetCachePath(string projectRoot)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(projectRoot.ToUpperInvariant()));
                var name = string.Concat(hash.Take(10).Select(value => value.ToString("x2")));
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UEAssist", "Indexes", name + ".index");
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

        private static string FindEngineRoot()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Epic Games\Unreal Engine\Builds"))
                {
                    if (key != null)
                    {
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
                return Directory.GetDirectories(epicRoot, "UE_*")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(IsEngineRoot);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsEngineRoot(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, "Engine", "Source", "Runtime"));
        }
    }
}
