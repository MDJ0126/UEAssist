using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace UEAssist.Extension
{
    internal sealed class IndexingStatusReporter : IDisposable
    {
        private readonly AsyncPackage package;
        private readonly ProjectIndexService indexService;
        private readonly IVsStatusbar statusBar;
        private uint progressCookie;

        public IndexingStatusReporter(AsyncPackage package, ProjectIndexService indexService, IVsStatusbar statusBar)
        {
            this.package = package;
            this.indexService = indexService;
            this.statusBar = statusBar;
            indexService.IndexingStatusChanged += OnIndexingStatusChanged;
        }

        public void Dispose()
        {
            indexService.IndexingStatusChanged -= OnIndexingStatusChanged;
        }

        private void OnIndexingStatusChanged(object sender, IndexingStatusEventArgs e)
        {
            package.JoinableTaskFactory.RunAsync(async delegate
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var label = "UEAssist — " + e.Message;
                if (e.Completed)
                {
                    statusBar.Progress(ref progressCookie, 0, label, 100, 100);
                    statusBar.SetText(label);
                }
                else
                {
                    statusBar.Progress(ref progressCookie, 1, label, (uint)e.Percent, 100);
                }
            }).FileAndForget("UEAssist/IndexingStatus");
        }
    }
}
