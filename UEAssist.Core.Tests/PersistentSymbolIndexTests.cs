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
                Assert.Contains(index.CompleteMembers("AMyActor", "Get"), item => item.Name == "GetHealth");
                Assert.Contains(index.Complete("GetH"), item => item.Name == "GetHealth");
                Assert.Equal("AMyActor*", index.ResolveVariableType("Target"));
                Assert.Equal("float", index.ResolveReturnType("GetHealth"));
                Assert.Contains(index.CompleteMembers("AMyActor", "After"), item => item.Name == "AfterNestedType");
                Assert.Contains(index.CompleteMembers("AMyActor", "Destroy"), item => item.Name == "DestroyActor");
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

        private static string CreateProject()
        {
            var root = Path.Combine(Path.GetTempPath(), "UEAssistTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Source", "Game"));
            File.WriteAllText(Path.Combine(root, "Source", "Game", "MyActor.h"),
                "class ABaseActor\n{\npublic:\n    void GetBase();\n};\n" +
                "class AMyActor : public ABaseActor\n{\npublic:\n    struct FNested { int32 Value; };\n    int32 Health;\n    float GetHealth();\n    void AfterNestedType();\n    UE_API bool DestroyActor(AActor* Actor);\n    AMyActor* Target;\n};\n");
            File.WriteAllText(Path.Combine(root, "Source", "Game", "MyActor.cpp"),
                "float AMyActor::GetHealth() { return Health; }\n");
            return root;
        }
    }
}
