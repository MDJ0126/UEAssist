using Microsoft.VisualStudio.Text;
using System;
using System.Threading;

namespace UEAssist.Extension
{
    internal sealed class LiveDocumentTracker
    {
        private readonly ITextBuffer buffer;
        private readonly string filePath;
        private readonly ProjectIndexService indexService;
        private readonly Timer updateTimer;
        private ITextSnapshot pendingSnapshot;

        public LiveDocumentTracker(ITextBuffer buffer, string filePath, ProjectIndexService indexService)
        {
            this.buffer = buffer;
            this.filePath = filePath;
            this.indexService = indexService;
            Update(buffer.CurrentSnapshot);
            updateTimer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            buffer.Changed += OnChanged;
        }

        private void OnChanged(object sender, TextContentChangedEventArgs args)
        {
            pendingSnapshot = args.After;
            updateTimer.Change(150, Timeout.Infinite);
        }

        private void OnTimer(object state)
        {
            var snapshot = pendingSnapshot;
            if (snapshot != null) Update(snapshot);
        }

        private void Update(ITextSnapshot snapshot)
        {
            indexService.UpdateLiveDocument(filePath, snapshot.GetText());
        }
    }
}
