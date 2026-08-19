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
    }
}
