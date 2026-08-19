using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Composition;
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
            var shouldTrigger = ShouldTrigger(commandGroup, commandId, input);
            var result = Next.Exec(ref commandGroup, commandId, commandOptions, input, output);
            if (ErrorHandler.Succeeded(result) && shouldTrigger && !broker.IsCompletionActive(view))
            {
                broker.TriggerCompletion(view);
            }
            return result;
        }

        private static bool ShouldTrigger(Guid commandGroup, uint commandId, IntPtr input)
        {
            if (commandGroup != VSConstants.VSStd2K || commandId != (uint)VSConstants.VSStd2KCmdID.TYPECHAR || input == IntPtr.Zero)
            {
                return false;
            }

            var value = Marshal.GetObjectForNativeVariant(input);
            if (!(value is ushort characterCode)) return false;
            var character = (char)characterCode;
            return char.IsLetterOrDigit(character) || character == '_' || character == '.' || character == '>';
        }
    }
}
