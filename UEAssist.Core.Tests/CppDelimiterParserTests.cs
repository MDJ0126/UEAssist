using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class CppDelimiterParserTests
    {
        [Fact]
        public void FindDefiniteIssues_FindsMissingOuterCallParenthesis()
        {
            const string code = "Mesh->SetRelativeLocationAndRotation(FVector(0, 0, -88), FRotator(0, -90, 0);";
            var issue = Assert.Single(CppDelimiterParser.FindDefiniteIssues(code));
            Assert.Contains("닫는 괄호", issue.Message);
            Assert.Equal(code.IndexOf('('), issue.Start);
        }

        [Fact]
        public void FindDefiniteIssues_IgnoresBalancedCallsAndStringContents()
        {
            const string code = "Log(TEXT(\"missing ( is text\"));\nMesh->SetLocation(FVector(0, 0, 0));";
            Assert.Empty(CppDelimiterParser.FindDefiniteIssues(code));
        }
    }
}
