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

        public CompletionCommandFilter(IWpfTextView view, ICompletionBroker broker)
        {
            this.view = view;
            this.broker = broker;
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
            var hasTypedCharacter = TryGetTypedCharacter(commandGroup, commandId, input, out var typedCharacter);
            var shouldTrigger = hasTypedCharacter && ShouldTrigger(typedCharacter);
            var result = Next.Exec(ref commandGroup, commandId, commandOptions, input, output);
            if (ErrorHandler.Succeeded(result) && hasTypedCharacter && ShouldDismissPreview(typedCharacter))
            {
                DismissPreviewSessions();
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
            return result;
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
            return char.IsLetter(character) || character == '_' || character == '.' || character == '>';
        }

        private static bool ShouldDismissPreview(char character)
        {
            return char.IsWhiteSpace(character) || "(),;{}[]=\"'".IndexOf(character) >= 0;
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

        private void PreferIntelliSenseWhenReady()
        {
            foreach (var session in broker.GetSessions(view).ToArray())
            {
                var hasPreview = session.CompletionSets.Any(set => string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal));
                var hasIntelliSense = session.CompletionSets.Any(set => !string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal) && set.Completions.Count > 0);
                if (!hasPreview || !hasIntelliSense) continue;
                session.Dismiss();
                broker.TriggerCompletion(view);
                break;
            }
        }
    }
}
