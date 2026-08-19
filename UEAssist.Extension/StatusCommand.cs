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

        private StatusCommand(AsyncPackage package, IntelliSenseSquiggleController controller, OleMenuCommandService commandService)
        {
            this.package = package;
            this.controller = controller;
            var commandId = new CommandID(GoToSymbolCommand.CommandSet, CommandId);
            commandService.AddCommand(new MenuCommand(Execute, commandId));
        }

        public static async Task InitializeAsync(AsyncPackage package, IntelliSenseSquiggleController controller)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null && controller != null)
            {
                _ = new StatusCommand(package, controller, commandService);
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                package,
                controller.CreateStatusText(),
                "UEAssist Status",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
