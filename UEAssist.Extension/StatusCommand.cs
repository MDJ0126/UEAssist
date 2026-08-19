using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace UEAssist.Extension
{
    internal sealed class StatusCommand
    {
        public const int CommandId = 0x0101;
        private readonly AsyncPackage package;
        private readonly IntelliSenseSquiggleController controller;
        private readonly ProjectIndexService indexService;

        private StatusCommand(AsyncPackage package, IntelliSenseSquiggleController controller, ProjectIndexService indexService, OleMenuCommandService commandService)
        {
            this.package = package;
            this.controller = controller;
            this.indexService = indexService;
            var commandId = new CommandID(GoToSymbolCommand.CommandSet, CommandId);
            commandService.AddCommand(new MenuCommand(Execute, commandId));
        }

        public static async Task InitializeAsync(AsyncPackage package, IntelliSenseSquiggleController controller, ProjectIndexService indexService)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null && controller != null)
            {
                _ = new StatusCommand(package, controller, indexService, commandService);
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                package,
                controller.CreateStatusText() + Environment.NewLine + Environment.NewLine +
                $"심볼 인덱스: {indexService?.Index.Count ?? 0:N0}개" + Environment.NewLine +
                $"인덱싱: {(indexService?.IsBuilding == true ? "진행 중" : "완료")}",
                "UEAssist Status",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
