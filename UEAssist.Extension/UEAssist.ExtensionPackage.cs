using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace UEAssist.Extension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(ExtensionPackage.PackageGuidString)]
    public sealed class ExtensionPackage : AsyncPackage
    {
        private IntelliSenseSquiggleController squiggleController;
        private ProjectIndexService indexService;
        private IndexingStatusReporter indexingStatusReporter;

         /// <summary>
        /// UEAssist ExtensionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "28053c53-4f17-4fe3-9f58-b4f3cf573bad";

    #region Package Members

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
    /// <param name="progress">A provider for progress updates.</param>
    /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        // When initialized asynchronously, the current thread may be a background thread at this point.
        // Do any initialization that requires the UI thread after switching to the UI thread.
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        indexService = componentModel?.GetService<ProjectIndexService>();
        try
        {
            var statusBar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            if (indexService != null && statusBar != null)
            {
                indexingStatusReporter = new IndexingStatusReporter(this, indexService, statusBar);
            }
        }
        catch (Exception)
        {
            indexingStatusReporter = null;
        }
        squiggleController = await IntelliSenseSquiggleController.CreateAsync(this, indexService);
        indexService?.Initialize(squiggleController?.UnrealProjectPath);
        await GoToSymbolCommand.InitializeAsync(this, squiggleController, indexService);
        await FindReferencesCommand.InitializeAsync(this, squiggleController, indexService);
        await StatusCommand.InitializeAsync(this, squiggleController, indexService);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && squiggleController != null)
        {
            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                squiggleController.Dispose();
            });
        }

        if (disposing) indexingStatusReporter?.Dispose();

        base.Dispose(disposing);
    }

    #endregion
}
}
