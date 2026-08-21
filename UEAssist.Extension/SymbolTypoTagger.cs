using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using UEAssist.Core;

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
        private static readonly Regex IdentifierPattern = new Regex(@"\b[AUFTEI][A-Z][A-Za-z0-9_]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex StandaloneIdentifierPattern = new Regex(@"^\s*(?<name>[A-Za-z_]\w*)\s*;?\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ThisClassMemberPointerPattern = new Regex(
            @"&\s*ThisClass\s*::\s*(?<name>[A-Za-z_]\w*)\s*(?=[,);])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> CppKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alignas", "alignof", "asm", "auto", "bool", "break", "case", "catch", "char", "class",
            "const", "continue", "default", "delete", "do", "double", "else", "enum", "explicit", "export",
            "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable",
            "namespace", "new", "noexcept", "nullptr", "operator", "private", "protected", "public", "register",
            "return", "short", "signed", "sizeof", "static", "struct", "switch", "template", "this", "throw",
            "true", "try", "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while"
        };
        private readonly ITextBuffer buffer;
        private readonly ProjectIndexService indexService;
        private ITextSnapshot cachedSnapshot;
        private IReadOnlyList<ITagSpan<IErrorTag>> cachedTags = Array.Empty<ITagSpan<IErrorTag>>();

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
            EnsureParsed(snapshot);
            foreach (var requestedSpan in spans)
            {
                foreach (var tag in cachedTags)
                {
                    if (tag.Span.IntersectsWith(requestedSpan)) yield return tag;
                }
            }
        }

        private void EnsureParsed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(snapshot, cachedSnapshot)) return;
            var tags = new List<ITagSpan<IErrorTag>>();
            foreach (var line in snapshot.Lines)
            {
                var text = line.GetText();
                var comment = text.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) text = text.Substring(0, comment);
                var macros = UnrealMacroParser.Find(text);
                foreach (Match match in IdentifierPattern.Matches(text))
                {
                    if (IsInsideString(text, match.Index)) continue;
                    if (macros.Any(macro => macro.Start == match.Index && macro.Length == match.Length)) continue;
                    var correct = indexService.Index.FindCorrectTypeCasing(match.Value);
                    if (correct == null) continue;
                    var span = new SnapshotSpan(snapshot, line.Start.Position + match.Index, match.Length);
                    tags.Add(new TagSpan<IErrorTag>(span,
                        new ErrorTag(PredefinedErrorTypeNames.SyntaxError, $"'{match.Value}'을(를) 찾을 수 없습니다. '{correct}'을(를) 사용하시겠습니까? (UEAssist)")));
                }

                var standalone = StandaloneIdentifierPattern.Match(text);
                if (standalone.Success)
                {
                    var name = standalone.Groups["name"].Value;
                    var isMacro = macros.Any(macro => macro.Start == standalone.Groups["name"].Index && macro.Length == standalone.Groups["name"].Length);
                    if (!isMacro && !CppKeywords.Contains(name) && !indexService.Index.ContainsSymbol(name))
                    {
                        var group = standalone.Groups["name"];
                        var span = new SnapshotSpan(snapshot, line.Start.Position + group.Index, group.Length);
                        tags.Add(new TagSpan<IErrorTag>(span,
                            new ErrorTag(PredefinedErrorTypeNames.SyntaxError, $"정의되지 않은 식별자 '{name}'입니다. (UEAssist)")));
                    }
                }

                foreach (Match memberPointer in ThisClassMemberPointerPattern.Matches(text))
                {
                    var group = memberPointer.Groups["name"];
                    var name = group.Value;
                    if (indexService.Index.ContainsSymbol(name) || indexService.ContainsLiveSymbol(name)) continue;
                    var span = new SnapshotSpan(snapshot, line.Start.Position + group.Index, group.Length);
                    tags.Add(new TagSpan<IErrorTag>(span,
                        new ErrorTag(PredefinedErrorTypeNames.SyntaxError,
                            $"ThisClass에 '{name}' 멤버가 정의되어 있지 않습니다. (UEAssist)")));
                }
            }
            foreach (var issue in CppDelimiterParser.FindDefiniteIssues(snapshot.GetText()))
            {
                var span = new SnapshotSpan(snapshot, issue.Start, issue.Length);
                tags.Add(new TagSpan<IErrorTag>(span,
                    new ErrorTag(PredefinedErrorTypeNames.SyntaxError, issue.Message + " (UEAssist)")));
            }
            cachedSnapshot = snapshot;
            cachedTags = tags;
        }

        private static bool IsInsideString(string text, int position)
        {
            var quoteCount = 0;
            for (var index = 0; index < position; index++)
            {
                if (text[index] == '"' && (index == 0 || text[index - 1] != '\\')) quoteCount++;
            }
            return quoteCount % 2 != 0;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            cachedSnapshot = null;
            cachedTags = Array.Empty<ITagSpan<IErrorTag>>();
            RaiseTagsChanged(e.After);
        }

        private void OnIndexUpdated(object sender, EventArgs e)
        {
            cachedSnapshot = null;
            cachedTags = Array.Empty<ITagSpan<IErrorTag>>();
            RaiseTagsChanged(buffer.CurrentSnapshot);
        }

        private void RaiseTagsChanged(ITextSnapshot snapshot)
        {
            if (snapshot.Length == 0) return;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }
    }
}
