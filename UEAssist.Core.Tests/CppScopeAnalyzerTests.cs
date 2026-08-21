using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class CppScopeAnalyzerTests
    {
        [Fact]
        public void FindOutOfScopeUses_FindsVariableUsedFromClosedSiblingBlock()
        {
            const string code = "void APlayer::Move() {\n" +
                "if (X != 0) { FVector Direction = GetForward(); Use(Direction); }\n" +
                "if (Y != 0) { Use(Direction); }\n}";
            var issue = Assert.Single(CppScopeAnalyzer.FindOutOfScopeUses(code));
            Assert.Equal("Direction", issue.Name);
            Assert.Equal(code.LastIndexOf("Direction"), issue.Start);
        }

        [Fact]
        public void FindOutOfScopeUses_IgnoresUsesInsideDeclarationScope()
        {
            const string code = "void APlayer::Move() { if (X) { FVector Direction; Use(Direction); } }";
            Assert.Empty(CppScopeAnalyzer.FindOutOfScopeUses(code));
        }

        [Fact]
        public void FindBareArgumentUses_FindsUndeclaredSimpleCallArgument()
        {
            const string code = "void APlayer::Move(FVector Direction) { GetPawn()->AddMovementInput(Dir); }";
            var declared = CppScopeAnalyzer.FindDeclaredNames(code);
            Assert.Contains("Direction", declared);
            Assert.DoesNotContain("Dir", declared);
            var use = Assert.Single(CppScopeAnalyzer.FindBareArgumentUses(code), issue => issue.Name == "Dir");
            Assert.Equal(code.IndexOf("Dir);"), use.Start);
        }
    }
}
