using System.Linq;
using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class UnrealMacroParserTests
    {
        [Fact]
        public void Find_RecognizesCommonReflectionMacrosImmediately()
        {
            const string code = "UCLASS() class AMyActor { UPROPERTY(EditAnywhere) int32 Value; GENERATED_BODY() };";

            var names = UnrealMacroParser.Find(code).Select(item => item.Name).ToArray();

            Assert.Equal(new[] { "UCLASS", "UPROPERTY", "GENERATED_BODY" }, names);
        }

        [Theory]
        [InlineData("DECLARE_DYNAMIC_MULTICAST_DELEGATE(FOnChanged)", "DECLARE_DYNAMIC_MULTICAST_DELEGATE")]
        [InlineData("IMPLEMENT_PRIMARY_GAME_MODULE(X, Y, Z)", "IMPLEMENT_PRIMARY_GAME_MODULE")]
        [InlineData("class R1_API AR1Actor", "R1_API")]
        public void Find_RecognizesUnrealMacroFamilies(string code, string expected)
        {
            Assert.Equal(expected, Assert.Single(UnrealMacroParser.Find(code)).Name);
        }
    }
}
