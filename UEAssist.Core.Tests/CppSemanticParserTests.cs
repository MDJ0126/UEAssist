using System.Linq;
using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class CppSemanticParserTests
    {
        [Fact]
        public void Parse_ClassifiesUnrealTypesAndVariables()
        {
            const string code = "class AR1Actor : public AActor { int32 HP; float Speed; AR1Actor* Target; };";

            var tokens = CppSemanticParser.Parse(code);

            Assert.Contains(tokens, token => token.Name == "AR1Actor" && token.Kind == SemanticTokenKind.Type);
            Assert.Contains(tokens, token => token.Name == "AActor" && token.Kind == SemanticTokenKind.Type);
            Assert.Contains(tokens, token => token.Name == "HP" && token.Kind == SemanticTokenKind.Variable);
            Assert.Contains(tokens, token => token.Name == "Speed" && token.Kind == SemanticTokenKind.Variable);
            Assert.Contains(tokens, token => token.Name == "Target" && token.Kind == SemanticTokenKind.Variable);
        }

        [Fact]
        public void Parse_DoesNotClassifyReflectionMacroAsType()
        {
            var tokens = CppSemanticParser.Parse("UCLASS() class AThing {};");

            Assert.DoesNotContain(tokens, token => token.Name == "UCLASS");
        }

        [Fact]
        public void Parse_ClassifiesGeneratedSuperAliasImmediately()
        {
            const string code = "UCLASS() class R1_API AR1Actor : public AActor { GENERATED_BODY() void BeginPlay() { Super::BeginPlay(); } };";

            var tokens = CppSemanticParser.Parse(code);

            Assert.Contains(tokens, token => token.Name == "Super" && token.Kind == SemanticTokenKind.Type);
        }

        [Fact]
        public void Parse_ClassifiesSuperUsageInCppWithoutHeaderContext()
        {
            const string code = "void AR1Actor::BeginPlay() { Super::BeginPlay(); }";

            var tokens = CppSemanticParser.Parse(code);

            Assert.Contains(tokens, token => token.Name == "Super" && token.Kind == SemanticTokenKind.Type);
        }

        [Fact]
        public void Parse_ClassifiesFunctionDefinitionsAndCallsImmediately()
        {
            const string code = "void AR1Actor::BeginPlay() { Super::BeginPlay(); PrimaryActorTick.bCanEverTick = true; }";

            var tokens = CppSemanticParser.Parse(code);

            Assert.Equal(2, tokens.Count(token => token.Name == "BeginPlay" && token.Kind == SemanticTokenKind.Function));
        }

        [Fact]
        public void Parse_DoesNotClassifyKeywordsOrUnrealMacrosAsFunctions()
        {
            const string code = "if (Ready) { UPROPERTY() Tick(DeltaTime); }";

            var tokens = CppSemanticParser.Parse(code);

            Assert.DoesNotContain(tokens, token => token.Name == "if" && token.Kind == SemanticTokenKind.Function);
            Assert.DoesNotContain(tokens, token => token.Name == "UPROPERTY" && token.Kind == SemanticTokenKind.Function);
            Assert.Contains(tokens, token => token.Name == "Tick" && token.Kind == SemanticTokenKind.Function);
        }
    }
}
