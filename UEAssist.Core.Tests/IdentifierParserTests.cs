using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class IdentifierParserTests
    {
        [Theory]
        [InlineData("MyActor->RootComponent", 3, "MyActor")]
        [InlineData("MyActor->RootComponent", 12, "RootComponent")]
        [InlineData("UCLASS()", 1, "UCLASS")]
        [InlineData("    Value", 0, "")]
        public void At_ReturnsIdentifierUnderCursor(string line, int column, string expected)
        {
            Assert.Equal(expected, IdentifierParser.At(line, column));
        }
    }
}
