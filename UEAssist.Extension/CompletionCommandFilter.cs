using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace UEAssist.Extension
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("C/C++")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class CompletionCommandFilterProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService Adapters = null;

        [Import]
        internal ICompletionBroker CompletionBroker = null;

        public void VsTextViewCreated(Microsoft.VisualStudio.TextManager.Interop.IVsTextView textViewAdapter)
        {
            var view = Adapters.GetWpfTextView(textViewAdapter);
            if (view == null) return;
            var filter = new CompletionCommandFilter(view, CompletionBroker);
            textViewAdapter.AddCommandFilter(filter, out var next);
            filter.Next = next;
        }
    }

    internal sealed class CompletionCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView view;
        private readonly ICompletionBroker broker;
        private readonly SynchronizationContext uiContext;
        private bool liveCompletionQueued;

        public CompletionCommandFilter(IWpfTextView view, ICompletionBroker broker)
        {
            this.view = view;
            this.broker = broker;
            uiContext = SynchronizationContext.Current;
        }

        public IOleCommandTarget Next { get; set; }

        public int QueryStatus(ref Guid commandGroup, uint commandCount, OLECMD[] commands, IntPtr commandText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Next.QueryStatus(ref commandGroup, commandCount, commands, commandText);
        }

        public int Exec(ref Guid commandGroup, uint commandId, uint commandOptions, IntPtr input, IntPtr output)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (IsCommitCommand(commandGroup, commandId) && TryCommitUEAssistSelection())
            {
                return VSConstants.S_OK;
            }

            var hasTypedCharacter = TryGetTypedCharacter(commandGroup, commandId, input, out var typedCharacter);
            var isDeletion = IsDeletion(commandGroup, commandId);
            var isExplicitCompletion = IsExplicitCompletion(commandGroup, commandId);
            var shouldTrigger = hasTypedCharacter && ShouldTrigger(typedCharacter);
            var isNumericPunctuation = hasTypedCharacter && IsNumericLiteralPunctuation(typedCharacter);
            if (isNumericPunctuation)
            {
                // A native C++ completion session treats '.' as a commit character.
                // Close every session before "50." can commit an unrelated symbol.
                DismissAllCompletionSessions();
            }
            // A standalone preview must never consume punctuation or whitespace as a
            // completion commit. Dismiss it before Visual Studio processes the key.
            if (hasTypedCharacter && ShouldDismissPreviewBeforeInput(typedCharacter))
            {
                DismissPreviewSessions();
            }
            var result = Next.Exec(ref commandGroup, commandId, commandOptions, input, output);
            if (ErrorHandler.Succeeded(result) && (hasTypedCharacter || isDeletion))
            {
                RefreshUEAssistSessions();
            }
            if (ErrorHandler.Succeeded(result) && hasTypedCharacter && !isNumericPunctuation && ShouldInvokeLiveCompletion(typedCharacter))
            {
                // Refresh on the next UI turn without repeatedly invoking the native
                // SHOWMEMBERLIST command. The latter displays VC++'s loading popup on
                // top of the UEAssist preview while IntelliSense is still indexing.
                QueueCompletionRefresh();
                return result;
            }
            if (ErrorHandler.Succeeded(result) && shouldTrigger && !broker.IsCompletionActive(view))
            {
                broker.TriggerCompletion(view);
                PreferIntelliSenseWhenReady();
            }
            else if (ErrorHandler.Succeeded(result) && hasTypedCharacter)
            {
                PreferIntelliSenseWhenReady();
            }
            if (ErrorHandler.Succeeded(result) && isDeletion && !broker.IsCompletionActive(view) && HasCompletionContextAtCaret())
            {
                broker.TriggerCompletion(view);
                PreferIntelliSenseWhenReady();
            }
            if (isExplicitCompletion && !broker.IsCompletionActive(view))
            {
                broker.TriggerCompletion(view);
                PreferIntelliSenseWhenReady();
                if (ErrorHandler.Failed(result)) result = VSConstants.S_OK;
            }
            return result;
        }

        private void RefreshUEAssistSessions()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                if (!session.Properties.ContainsProperty(UEAssistCompletionSessionState.Marker)) continue;
                // Preserve the 0.0.13 live-refresh behavior without entering the
                // native VCCompletionSet that caused the later devenv crash.
                foreach (var set in session.CompletionSets.Where(item =>
                    string.Equals(item.Moniker, "UEAssistPreview", StringComparison.Ordinal)).ToArray())
                {
                    set.Filter();
                }
            }
        }

        private bool TryCommitUEAssistSelection()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                var set = session.SelectedCompletionSet;
                var selected = set?.SelectionStatus?.Completion;
                var isUEAssist = string.Equals(set?.Moniker, "UEAssistPreview", StringComparison.Ordinal) ||
                                 selected is Completion4 completion && string.Equals(completion.Suffix, "UEAssist", StringComparison.Ordinal);
                if (!isUEAssist || selected == null) continue;
                session.Commit();
                return true;
            }
            return false;
        }

        private static bool IsCommitCommand(Guid commandGroup, uint commandId)
        {
            return commandGroup == VSConstants.VSStd2K &&
                   (commandId == (uint)VSConstants.VSStd2KCmdID.TAB ||
                    commandId == (uint)VSConstants.VSStd2KCmdID.RETURN);
        }

        private static bool IsExplicitCompletion(Guid commandGroup, uint commandId)
        {
            return commandGroup == VSConstants.VSStd2K &&
                   (commandId == (uint)VSConstants.VSStd2KCmdID.COMPLETEWORD ||
                    commandId == (uint)VSConstants.VSStd2KCmdID.SHOWMEMBERLIST ||
                    commandId == (uint)VSConstants.VSStd2KCmdID.AUTOCOMPLETE);
        }

        private static bool IsDeletion(Guid commandGroup, uint commandId)
        {
            return commandGroup == VSConstants.VSStd2K &&
                   (commandId == (uint)VSConstants.VSStd2KCmdID.BACKSPACE ||
                    commandId == (uint)VSConstants.VSStd2KCmdID.DELETE);
        }

        private bool HasCompletionContextAtCaret()
        {
            var point = view.Caret.Position.BufferPosition;
            if (point.Position == 0) return false;
            var previous = point.Snapshot[point.Position - 1];
            return char.IsLetterOrDigit(previous) || previous == '_';
        }

        private static bool TryGetTypedCharacter(Guid commandGroup, uint commandId, IntPtr input, out char character)
        {
            character = default(char);
            if (commandGroup != VSConstants.VSStd2K || commandId != (uint)VSConstants.VSStd2KCmdID.TYPECHAR || input == IntPtr.Zero)
            {
                return false;
            }

            var value = Marshal.GetObjectForNativeVariant(input);
            if (!(value is ushort characterCode)) return false;
            character = (char)characterCode;
            return true;
        }

        private static bool ShouldTrigger(char character)
        {
            return char.IsLetter(character) || character == '_';
        }

        private bool ShouldInvokeLiveCompletion(char character)
        {
            if (char.IsDigit(character))
            {
                return HasIdentifierCompletionContextAtCaret();
            }
            if (char.IsLetter(character) || character == '_' || character == '.' || character == '>')
            {
                return true;
            }

            if (character != ':') return false;
            var caret = view.Caret.Position.BufferPosition;
            return caret.Position >= 2 && caret.Snapshot[caret.Position - 2] == ':';
        }

        private bool HasIdentifierCompletionContextAtCaret()
        {
            var caret = view.Caret.Position.BufferPosition;
            var position = caret.Position;
            while (position > 0)
            {
                var character = caret.Snapshot[position - 1];
                if (!char.IsLetterOrDigit(character) && character != '_') break;
                position--;
            }
            return position < caret.Position &&
                   (char.IsLetter(caret.Snapshot[position]) || caret.Snapshot[position] == '_');
        }

        private bool IsNumericLiteralPunctuation(char character)
        {
            if (character != '.') return false;
            var caret = view.Caret.Position.BufferPosition;
            if (caret.Position == 0) return false;
            return char.IsDigit(caret.Snapshot[caret.Position - 1]);
        }

        private void QueueCompletionRefresh()
        {
            if (liveCompletionQueued) return;
            liveCompletionQueued = true;
#pragma warning disable VSTHRD001 // Captured from IVsTextViewCreated on the VS UI thread.
            uiContext.Post(delegate
            {
                liveCompletionQueued = false;
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    InvokeCompletionRefresh();
                }
                catch (Exception)
                {
                    // Completion must never terminate devenv.exe.
                }
            }, null);
#pragma warning restore VSTHRD001
        }

        private void InvokeCompletionRefresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var ueAssistSessions = broker.GetSessions(view)
                .Where(session => session.Properties.ContainsProperty(UEAssistCompletionSessionState.Marker))
                .ToArray();
            if (ueAssistSessions.Length > 0)
            {
                // Filtering can only remove items from the list created for the
                // first character. Recreate our standalone safe session so a more
                // specific prefix can bring GetWorld/GetPawn and similar symbols in.
                foreach (var session in ueAssistSessions) session.Dismiss();
                broker.TriggerCompletion(view);
                return;
            }

            if (!broker.IsCompletionActive(view))
            {
                broker.TriggerCompletion(view);
            }
            else
            {
                RefreshUEAssistSessions();
            }
        }

        private static bool ShouldDismissPreviewBeforeInput(char character)
        {
            return !char.IsLetterOrDigit(character) && character != '_';
        }

        private void DismissPreviewSessions()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                if (session.CompletionSets.Count > 0 && session.CompletionSets.All(set => string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal)))
                {
                    session.Dismiss();
                }
            }
        }

        private void DismissAllCompletionSessions()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                session.Dismiss();
            }
        }

        private void PreferIntelliSenseWhenReady()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                var hasPreview = session.CompletionSets.Any(set => string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal));
                var hasIntelliSense = session.CompletionSets.Any(set => !string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal) && set.Completions.Count > 0);
                if (!hasPreview || !hasIntelliSense) continue;
                session.Dismiss();
                break;
            }
        }
    }
}
