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
        private readonly SolutionEvents solutionEvents;
        private readonly DocumentEvents documentEvents;
        private bool? originalDisableSquiggles;
        private bool applied;

        private IntelliSenseSquiggleController(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.dte = dte;
            solutionEvents = dte.Events.SolutionEvents;
            documentEvents = dte.Events.DocumentEvents;
            solutionEvents.Opened += OnContextChanged;
            solutionEvents.AfterClosing += OnSolutionAfterClosing;
            documentEvents.DocumentOpened += OnDocumentOpened;
        }

        public bool UnrealProjectDetected { get; private set; }
        public string UnrealProjectPath { get; private set; }
        public bool SquigglesSuppressed => applied;
        public string LastError { get; private set; }

        public static async Task<IntelliSenseSquiggleController> CreateAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null)
            {
                return null;
            }

            var controller = new IntelliSenseSquiggleController(dte);
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

            ApplySquiggleSuppression();
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

            var status = SquigglesSuppressed ? "적용됨" : "적용 실패";
            var message = "Unreal 프로젝트: " + UnrealProjectPath + "\n"
                + "IntelliSense 빨간 밑줄 숨김: " + status;

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

        private void ApplySquiggleSuppression()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var property = GetDisableSquigglesProperty();
                if (!originalDisableSquiggles.HasValue)
                {
                    originalDisableSquiggles = Convert.ToBoolean(property.Value);
                }

                property.Value = true;
                applied = Convert.ToBoolean(property.Value);
                if (!applied)
                {
                    LastError = "Visual Studio가 DisableSquiggles 설정 변경을 수락하지 않았습니다.";
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is COMException)
            {
                applied = false;
                LastError = exception.Message;
            }
        }

        private void RestoreOriginalSetting()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!applied || !originalDisableSquiggles.HasValue)
            {
                return;
            }

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
                applied = false;
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
