using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using UEAssist.Core;
using UEAssist.Indexing;

namespace UEAssist.Extension
{
    internal sealed class GoToSymbolCommand
    {
        public static readonly Guid CommandSet = new Guid("f9d77864-fac8-457e-bd4f-552715151b6c");
        public const int CommandId = 0x0100;

        private readonly AsyncPackage package;
        private readonly IntelliSenseSquiggleController controller;

        private GoToSymbolCommand(AsyncPackage package, IntelliSenseSquiggleController controller, OleMenuCommandService commandService)
        {
            this.package = package;
            this.controller = controller;
            var menuCommandId = new CommandID(CommandSet, CommandId);
            commandService.AddCommand(new MenuCommand(Execute, menuCommandId));
        }

        public static async Task InitializeAsync(AsyncPackage package, IntelliSenseSquiggleController controller)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService))
                as OleMenuCommandService;

            if (commandService != null)
            {
                _ = new GoToSymbolCommand(package, controller, commandService);
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            package.JoinableTaskFactory.RunAsync(ExecuteAsync).FileAndForget("UEAssist/GoToSymbol");
        }

        private async Task ExecuteAsync()
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = await package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            var selection = dte?.ActiveDocument?.Selection as EnvDTE.TextSelection;
            if (dte == null || selection == null)
            {
                ExecuteDefaultGoToDefinition(dte);
                return;
            }

            var extension = Path.GetExtension(dte.ActiveDocument.FullName);
            if (!IsCppFile(extension))
            {
                ExecuteDefaultGoToDefinition(dte);
                return;
            }

            var editPoint = selection.ActivePoint.CreateEditPoint();
            editPoint.StartOfLine();
            var lineText = editPoint.GetText(editPoint.LineLength);
            var symbolName = IdentifierParser.At(lineText, selection.ActivePoint.LineCharOffset - 1);

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                ExecuteDefaultGoToDefinition(dte);
                return;
            }

            controller?.Refresh();
            var solutionPath = dte.Solution?.FullName;
            var solutionDirectory = !string.IsNullOrWhiteSpace(controller?.UnrealProjectPath)
                ? Path.GetDirectoryName(controller.UnrealProjectPath)
                : string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);

            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                ExecuteDefaultGoToDefinition(dte);
                return;
            }

            var results = await Task.Run(() => new CppSymbolIndexer().Find(solutionDirectory, symbolName));
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (results.Count == 0)
            {
                ExecuteDefaultGoToDefinition(dte);
                return;
            }

            var target = results[0];
            dte.ItemOperations.OpenFile(target.FilePath, EnvDTE.Constants.vsViewKindTextView);
            if (dte.ActiveDocument?.Selection is EnvDTE.TextSelection targetSelection)
            {
                targetSelection.MoveToLineAndOffset(target.Line, target.Column, false);
            }
        }

        private static bool IsCppFile(string extension)
        {
            return extension.Equals(".h", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".hh", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".inl", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".c", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cc", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cxx", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExecuteDefaultGoToDefinition(EnvDTE80.DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null)
            {
                return;
            }

            try
            {
                dte.ExecuteCommand("Edit.GoToDefinition");
            }
            catch (Exception)
            {
                // Visual Studio owns the fallback command and may reject it when no editor is active.
            }
        }
    }
}
