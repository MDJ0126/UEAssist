using Microsoft.VisualStudio.TaskStatusCenter;
using System;
using System.Threading.Tasks;

namespace UEAssist.Extension
{
    internal sealed class IndexingStatusReporter : IDisposable
    {
        private readonly ProjectIndexService indexService;
        private readonly IVsTaskStatusCenterService statusCenter;
        private ITaskHandler taskHandler;
        private TaskCompletionSource<object> completion;

        public IndexingStatusReporter(ProjectIndexService indexService, IVsTaskStatusCenterService statusCenter)
        {
            this.indexService = indexService;
            this.statusCenter = statusCenter;
            indexService.IndexingStatusChanged += OnIndexingStatusChanged;
        }

        public void Dispose()
        {
            indexService.IndexingStatusChanged -= OnIndexingStatusChanged;
            completion?.TrySetResult(null);
        }

        private void OnIndexingStatusChanged(object sender, IndexingStatusEventArgs e)
        {
            if (taskHandler == null)
            {
                var options = new TaskHandlerOptions
                {
                    Title = "UEAssist — Unreal API 분석",
                    TaskSuccessMessage = "UEAssist 인덱스 준비 완료"
                };
                taskHandler = statusCenter.PreRegister(options, CreateProgress(e));
                completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                taskHandler.RegisterTask(completion.Task);
            }
            else
            {
                taskHandler.Progress.Report(CreateProgress(e));
            }

            if (!e.Completed) return;
            if (e.Failed) completion.TrySetException(new InvalidOperationException(e.Message));
            else completion.TrySetResult(null);
            taskHandler = null;
            completion = null;
        }

        private static TaskProgressData CreateProgress(IndexingStatusEventArgs e)
        {
            return new TaskProgressData
            {
                ProgressText = e.Message,
                PercentComplete = Math.Max(0, Math.Min(100, e.Percent)),
                CanBeCanceled = false
            };
        }
    }
}
