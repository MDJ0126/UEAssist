using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;

namespace UEAssist.Extension
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("C/C++")]
    [TagType(typeof(IErrorTag))]
    internal sealed class SymbolTypoTaggerProvider : ITaggerProvider
    {
        [Import]
        internal ProjectIndexService IndexService = null;

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new SymbolTypoTagger(buffer, IndexService)) as ITagger<T>;
        }
    }

    internal sealed class SymbolTypoTagger : ITagger<IErrorTag>
    {
        private static readonly Regex IdentifierPattern = new Regex(@"\b[A-Za-z_]\w*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ITextBuffer buffer;
        private readonly ProjectIndexService indexService;

        public SymbolTypoTagger(ITextBuffer buffer, ProjectIndexService indexService)
        {
            this.buffer = buffer;
            this.indexService = indexService;
            buffer.Changed += OnBufferChanged;
            indexService.IndexUpdated += OnIndexUpdated;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0 || indexService.Index.Count == 0) yield break;
            var snapshot = spans[0].Snapshot;
            foreach (var requestedSpan in spans)
            {
                var text = requestedSpan.GetText();
                foreach (Match match in IdentifierPattern.Matches(text))
                {
                    var name = match.Value;
                    var correct = indexService.Index.Complete(name, 20)
                        .Select(item => item.Name)
                        .FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase) && !candidate.Equals(name, StringComparison.Ordinal));
                    if (correct == null) continue;

                    var span = new SnapshotSpan(snapshot, requestedSpan.Start.Position + match.Index, match.Length);
                    yield return new TagSpan<IErrorTag>(span,
                        new ErrorTag(PredefinedErrorTypeNames.SyntaxError, $"'{name}'을(를) 찾을 수 없습니다. '{correct}'을(를) 사용하시겠습니까? (UEAssist)"));
                }
            }
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            RaiseTagsChanged(e.After);
        }

        private void OnIndexUpdated(object sender, EventArgs e)
        {
            RaiseTagsChanged(buffer.CurrentSnapshot);
        }

        private void RaiseTagsChanged(ITextSnapshot snapshot)
        {
            if (snapshot.Length == 0) return;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }
    }
}
