using System;
using System.IO;
using System.Linq;
using UEAssist.Core;
using UEAssist.Indexing;

namespace UEAssist.Core.Tests
{
    public class PersistentSymbolIndexTests
    {
        [Fact]
        public void Build_IndexesTypesMembersInheritanceAndReferences()
        {
            var root = CreateProject();
            try
            {
                var index = new PersistentSymbolIndex();
                index.Build(root);

                Assert.Contains(index.Complete("AMy"), item => item.Name == "AMyActor" && item.Kind == SymbolKind.Type);
                Assert.Contains(index.Complete("amy"), item => item.Name == "AMyActor" && item.Kind == SymbolKind.Type);
                Assert.Contains(index.CompleteMembers("AMyActor", "Get"), item => item.Name == "GetHealth");
                Assert.Contains(index.CompleteMembers("amyactor", "get"), item => item.Name == "GetHealth");
                Assert.Contains(index.Complete("GetH"), item => item.Name == "GetHealth");
                Assert.Contains(index.Complete("geth"), item => item.Name == "GetHealth");
                Assert.Equal("AMyActor*", index.ResolveVariableType("Target"));
                Assert.Equal("AMyActor*", index.ResolveVariableType("target"));
                Assert.Equal("float", index.ResolveReturnType("GetHealth"));
                Assert.Equal("float", index.ResolveReturnType("gethealth"));
                Assert.Contains(index.Complete("loc"), item => item.Name == "Location" && item.Kind == SymbolKind.Variable);
                Assert.Contains(index.CompleteMembers("AMyActor", "After"), item => item.Name == "AfterNestedType");
                Assert.Contains(index.CompleteMembers("AMyActor", "Destroy"), item => item.Name == "DestroyActor");
                Assert.Contains(index.CompleteMembers("UGameplayStatics", "GetActor"), item => item.Name == "GetActorOfClass");
                Assert.Contains(index.Complete("UGameplayStat", 100), item => item.Name == "UGameplayStatics");
                Assert.Equal("TSubclassOf", index.FindCorrectTypeCasing("TSubclassof"));
                Assert.True(index.FindReferences("GetHealth").Count >= 2);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SaveAndLoad_PreservesCompletionData()
        {
            var root = CreateProject();
            var cache = Path.Combine(root, "cache", "symbols.index");
            try
            {
                var source = new PersistentSymbolIndex();
                source.Build(root);
                source.Save(cache);

                var loaded = new PersistentSymbolIndex();
                Assert.True(loaded.Load(cache));
                Assert.Contains(loaded.Complete("AMy"), item => item.Name == "AMyActor");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ReplaceWith_MergesEngineAndProjectIndexes()
        {
            var engineRoot = CreateProject();
            var projectRoot = CreateProject();
            try
            {
                File.AppendAllText(Path.Combine(engineRoot, "Source", "Game", "MyActor.h"),
                    "class USharedEngineApi { public: void SharedCall(); };\n");
                File.AppendAllText(Path.Combine(projectRoot, "Source", "Game", "MyActor.h"),
                    "class UProjectOnlyType {};\n");

                var engine = new PersistentSymbolIndex();
                engine.BuildProject(engineRoot);
                var project = new PersistentSymbolIndex();
                project.BuildProject(projectRoot);
                var combined = new PersistentSymbolIndex();
                combined.ReplaceWith(engine, project);

                Assert.Contains(combined.Complete("UShared"), item => item.Name == "USharedEngineApi");
                Assert.Contains(combined.Complete("UProject"), item => item.Name == "UProjectOnlyType");
            }
            finally
            {
                Directory.Delete(engineRoot, true);
                Directory.Delete(projectRoot, true);
            }
        }

        [Fact]
        public void BuiltInApi_IsAvailableWithoutAnEngineCache()
        {
            var index = new PersistentSymbolIndex();
            index.LoadBuiltInApi();

            Assert.Contains(index.CompleteMembers("AActor", "GetActor"), item => item.Name == "GetActorLocation");
            Assert.Contains(index.CompleteMembers("UWorld", "Spawn"), item => item.Name == "SpawnActor");
            var gameplayMatches = index.CompleteMembers("UGameplayStatics", "GetActor");
            Assert.Contains(gameplayMatches, item => item.Name == "GetActorOfClass");
            Assert.Contains(gameplayMatches, item => item.Name == "GetAllActorsOfClass");
            Assert.Equal("GetActorOfClass", gameplayMatches[0].Name);
            Assert.Empty(index.FindDefinitions("SpawnActor"));
            Assert.Contains(index.Complete("U"), item => item.Name == "UGameplayStatics");
            Assert.Contains(index.Complete("UGame"), item => item.Name == "UGameplayStatics");
            Assert.Contains(index.Complete("UGameplaySta"), item => item.Name == "UGameplayStatics");
        }

        private static string CreateProject()
        {
            var root = Path.Combine(Path.GetTempPath(), "UEAssistTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Source", "Game"));
            File.WriteAllText(Path.Combine(root, "Source", "Game", "MyActor.h"),
                "class ABaseActor\n{\npublic:\n    void GetBase();\n};\n" +
                "class AMyActor : public ABaseActor\n{\npublic:\n    struct FNested { int32 Value; };\n    int32 Health;\n    float GetHealth();\n    void AfterNestedType();\n    UE_API bool DestroyActor(AActor* Actor);\n    AMyActor* Target;\n};\n");
            File.AppendAllText(Path.Combine(root, "Source", "Game", "MyActor.h"),
                "class UGameplayStatics\n{\npublic:\n    static AActor* GetActorOfClass(UObject* WorldContext);\n};\n");
            File.AppendAllText(Path.Combine(root, "Source", "Game", "MyActor.h"),
                "class TSubclassOf {};\n");
            File.WriteAllText(Path.Combine(root, "Source", "Game", "MyActor.cpp"),
                "float AMyActor::GetHealth()\n{\n    FVector Location(0, 0, 0);\n    int32 LocalCount = 1;\n    return Health;\n}\n");
            return root;
        }
    }
}
