using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UEAssist.Extension
{
    internal sealed class IntelliSenseSquiggleController : IDisposable
    {
        private const string OptionsCategory = "TextEditor";
        private const string OptionsPage = "C/C++ Specific";
        private const string DisableSquigglesProperty = "DisableSquiggles";
        private const int ParentSearchLimit = 12;

        private readonly DTE2 dte;
        private readonly ProjectIndexService indexService;
        private readonly SolutionEvents solutionEvents;
        private readonly DocumentEvents documentEvents;
        private bool? originalDisableSquiggles;
        private bool settingChanged;

        private IntelliSenseSquiggleController(DTE2 dte, ProjectIndexService indexService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.dte = dte;
            this.indexService = indexService;
            solutionEvents = dte.Events.SolutionEvents;
            documentEvents = dte.Events.DocumentEvents;
            solutionEvents.Opened += OnContextChanged;
            solutionEvents.AfterClosing += OnSolutionAfterClosing;
            documentEvents.DocumentOpened += OnDocumentOpened;
            indexService.IntelliSenseAvailabilityChanged += OnIntelliSenseAvailabilityChanged;
        }

        public bool UnrealProjectDetected { get; private set; }
        public string UnrealProjectPath { get; private set; }
        public bool DiagnosticSquigglesEnabled { get; private set; }
        public string LastError { get; private set; }

        public static async Task<IntelliSenseSquiggleController> CreateAsync(AsyncPackage package, ProjectIndexService indexService)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null)
            {
                return null;
            }

            var controller = new IntelliSenseSquiggleController(dte, indexService);
            controller.Refresh();
            return controller;
        }

        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LastError = null;
            UnrealProjectPath = FindNearestUnrealProject();
            UnrealProjectDetected = !string.IsNullOrWhiteSpace(UnrealProjectPath);

            if (!UnrealProjectDetected)
            {
                RestoreOriginalSetting();
                return;
            }

            ApplyDiagnosticMode();
        }

        public string CreateStatusText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();

            if (!UnrealProjectDetected)
            {
                return "Unreal 프로젝트를 찾지 못했습니다.\n\n"
                    + "Unreal C++ 파일을 연 뒤 다시 확인하세요.\n"
                    + "검색 기준: 현재 문서와 솔루션 폴더의 상위 경로에 있는 .uproject";
            }

            var status = indexService.IntelliSenseReady
                ? "IntelliSense 준비됨 — 기본 진단 사용"
                : "IntelliSense 준비 중 — 초기 오진 숨김, UEAssist 확정 오타만 표시";
            var message = "Unreal 프로젝트: " + UnrealProjectPath + "\n"
                + "진단 상태: " + status;

            if (!string.IsNullOrWhiteSpace(LastError))
            {
                message += "\n\n오류: " + LastError;
            }

            return message;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            solutionEvents.Opened -= OnContextChanged;
            solutionEvents.AfterClosing -= OnSolutionAfterClosing;
            documentEvents.DocumentOpened -= OnDocumentOpened;
            indexService.IntelliSenseAvailabilityChanged -= OnIntelliSenseAvailabilityChanged;
            RestoreOriginalSetting();
        }

        private void OnContextChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        }

        private void OnSolutionAfterClosing()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        }

        private void OnDocumentOpened(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Refresh();
        }

        private void ApplyDiagnosticMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var property = GetDisableSquigglesProperty();
                if (!originalDisableSquiggles.HasValue)
                {
                    originalDisableSquiggles = Convert.ToBoolean(property.Value);
                }
                var disableDuringPreview = !indexService.IntelliSenseReady;
                property.Value = disableDuringPreview;
                settingChanged = true;
                DiagnosticSquigglesEnabled = !Convert.ToBoolean(property.Value);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is COMException)
            {
                DiagnosticSquigglesEnabled = false;
                LastError = exception.Message;
            }
        }

        private void OnIntelliSenseAvailabilityChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (UnrealProjectDetected) ApplyDiagnosticMode();
        }

        private void RestoreOriginalSetting()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!settingChanged || !originalDisableSquiggles.HasValue) return;
            try
            {
                GetDisableSquigglesProperty().Value = originalDisableSquiggles.Value;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is COMException)
            {
                LastError = exception.Message;
            }
            finally
            {
                settingChanged = false;
                originalDisableSquiggles = null;
            }
        }

        private Property GetDisableSquigglesProperty()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.Properties[OptionsCategory, OptionsPage].Item(DisableSquigglesProperty);
        }

        private string FindNearestUnrealProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var startDirectory in GetCandidateDirectories())
            {
                var project = FindInParents(startDirectory);
                if (!string.IsNullOrWhiteSpace(project))
                {
                    return project;
                }
            }

            return null;
        }

        private IEnumerable<string> GetCandidateDirectories()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var activeDocumentPath = dte.ActiveDocument?.FullName;
            if (!string.IsNullOrWhiteSpace(activeDocumentPath))
            {
                yield return Path.GetDirectoryName(activeDocumentPath);
            }

            var solutionPath = dte.Solution?.FullName;
            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                yield return Path.GetDirectoryName(solutionPath);
            }
        }

        private static string FindInParents(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return null;
            }

            var directory = new DirectoryInfo(startDirectory);
            for (var depth = 0; directory != null && depth < ParentSearchLimit; depth++, directory = directory.Parent)
            {
                try
                {
                    var projects = directory.GetFiles("*.uproject", SearchOption.TopDirectoryOnly);
                    if (projects.Length > 0)
                    {
                        return projects[0].FullName;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return null;
        }
    }
}
