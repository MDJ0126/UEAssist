using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using UEAssist.Core;

namespace UEAssist.Extension
{
    internal sealed class FindReferencesCommand
    {
        public const int CommandId = 0x0102;
        private static readonly Guid OutputPaneGuid = new Guid("5725d377-6839-49a5-b9e2-67694bb4594f");
        private readonly AsyncPackage package;
        private readonly IntelliSenseSquiggleController controller;
        private readonly ProjectIndexService indexService;

        private FindReferencesCommand(AsyncPackage package, IntelliSenseSquiggleController controller, ProjectIndexService indexService, OleMenuCommandService commandService)
        {
            this.package = package;
            this.controller = controller;
            this.indexService = indexService;
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(GoToSymbolCommand.CommandSet, CommandId)));
        }

        public static async Task InitializeAsync(AsyncPackage package, IntelliSenseSquiggleController controller, ProjectIndexService indexService)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null) _ = new FindReferencesCommand(package, controller, indexService, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            package.JoinableTaskFactory.RunAsync(ExecuteAsync).FileAndForget("UEAssist/FindReferences");
        }

        private async Task ExecuteAsync()
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            var selection = dte?.ActiveDocument?.Selection as EnvDTE.TextSelection;
            if (dte == null || selection == null)
            {
                ExecuteFallback(dte);
                return;
            }

            var editPoint = selection.ActivePoint.CreateEditPoint();
            editPoint.StartOfLine();
            var symbol = IdentifierParser.At(editPoint.GetText(editPoint.LineLength), selection.ActivePoint.LineCharOffset - 1);
            if (string.IsNullOrWhiteSpace(symbol))
            {
                ExecuteFallback(dte);
                return;
            }

            controller?.Refresh();
            indexService?.Initialize(controller?.UnrealProjectPath);
            var results = await Task.Run(() => indexService?.Index.FindReferences(symbol));
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (results == null || results.Count == 0)
            {
                ExecuteFallback(dte);
                return;
            }

            var output = await package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (output == null) return;
            var paneGuid = OutputPaneGuid;
            output.CreatePane(ref paneGuid, "UEAssist References", 1, 1);
            output.GetPane(ref paneGuid, out var pane);
            pane?.Clear();
            pane?.OutputStringThreadSafe($"UEAssist: '{symbol}' 참조 {results.Count}개\r\n");
            foreach (var result in results)
            {
                pane?.OutputStringThreadSafe($"{result.FilePath}({result.Line},{result.Column}): {symbol}\r\n");
            }
            pane?.Activate();
        }

        private static void ExecuteFallback(EnvDTE80.DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { dte?.ExecuteCommand("Edit.FindAllReferences"); }
            catch (Exception) { }
        }
    }
}
