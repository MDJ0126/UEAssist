using UEAssist.Core;

namespace UEAssist.Core.Tests
{
    public class CppExpressionContextTests
    {
        [Fact]
        public void FindEnclosingFunctionOwner_ReturnsCurrentQualifiedCppClass()
        {
            const string code =
                "void AOther::First() { }\n" +
                "void AR1PlayerController::SetupInputComponent()\n{\n" +
                "    GetPawn()->AddMovementInput(FVector::ForwardVector);\n";

            Assert.Equal("AR1PlayerController",
                CppExpressionContext.FindEnclosingFunctionOwner(code, code.Length));
        }
    }
}
