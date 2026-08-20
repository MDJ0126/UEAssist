using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using UEAssist.Core;
using UEAssist.Indexing;

namespace UEAssist.Extension
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("C/C++")]
    [Name("UEAssist Completion")]
    [Order(After = Priority.Default)]
    internal sealed class UEAssistCompletionSourceProvider : ICompletionSourceProvider
    {
        [Import]
        internal ProjectIndexService IndexService = null;

        [Import]
        internal ITextDocumentFactoryService DocumentFactory = null;

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            if (DocumentFactory.TryGetTextDocument(textBuffer, out var document))
            {
                IndexService.InitializeFromDocument(document.FilePath);
            }
            return textBuffer.Properties.GetOrCreateSingletonProperty(() => new UEAssistCompletionSource(textBuffer, IndexService));
        }
    }

    internal sealed class UEAssistCompletionSource : ICompletionSource
    {
        private static readonly Regex MemberPattern = new Regex(@"(?<receiver>[A-Za-z_]\w*)(?<call>\s*\(\s*\))?\s*(?<operator>->|\.|::)\s*(?<prefix>[A-Za-z_]\w*)?$", RegexOptions.Compiled);
        private static readonly Regex UnrealSpecifierContext = new Regex(@"\b(?:UPROPERTY|UFUNCTION|UCLASS|USTRUCT|UENUM|UINTERFACE)\s*\([^)]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IncludeContext = new Regex(@"^\s*#\s*include\s*[<""](?<prefix>[^>""]*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ITextBuffer buffer;
        private readonly ProjectIndexService indexService;
        private bool disposed;

        public UEAssistCompletionSource(ITextBuffer buffer, ProjectIndexService indexService)
        {
            this.buffer = buffer;
            this.indexService = indexService;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (disposed || indexService.Index.Count == 0) return;
            var point = session.GetTriggerPoint(buffer.CurrentSnapshot);
            if (!point.HasValue) return;

            var snapshot = point.Value.Snapshot;
            var line = snapshot.GetLineFromPosition(point.Value.Position);
            var beforeCaret = snapshot.GetText(line.Start.Position, point.Value.Position - line.Start.Position);
            var memberMatch = MemberPattern.Match(beforeCaret);
            var includeMatch = IncludeContext.Match(beforeCaret);
            string prefix;
            IReadOnlyList<IndexedSymbol> candidates;
            var hasResolvedMemberContext = false;
            if (includeMatch.Success)
            {
                prefix = includeMatch.Groups["prefix"].Value;
                candidates = indexService.Index.CompleteHeaders(prefix, 100);
            }
            else if (UnrealSpecifierContext.IsMatch(beforeCaret))
            {
                prefix = GetIdentifierPrefix(beforeCaret);
                candidates = indexService.Index.CompleteSpecifiers(prefix, 100);
            }
            else if (memberMatch.Success)
            {
                prefix = memberMatch.Groups["prefix"].Value;
                var receiver = memberMatch.Groups["receiver"].Value;
                var typeName = memberMatch.Groups["operator"].Value == "::"
                    ? receiver
                    : receiver == "Super"
                    ? string.Empty
                    : memberMatch.Groups["call"].Success
                        ? indexService.Index.ResolveReturnType(receiver)
                        : indexService.Index.ResolveVariableType(receiver);
                hasResolvedMemberContext = !string.IsNullOrWhiteSpace(typeName);
                candidates = !hasResolvedMemberContext
                    ? indexService.Index.Complete(prefix, 100)
                    : indexService.Index.CompleteMembers(NormalizeType(typeName), prefix, 100);
            }
            else
            {
                prefix = GetIdentifierPrefix(beforeCaret);
                candidates = indexService.Index.Complete(prefix, 100);
            }

            if (candidates.Count == 0) return;
            var builtInSet = completionSets.FirstOrDefault(set =>
                !string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal) &&
                set.Completions != null && set.Completions.Count > 0);
            var start = point.Value.Position - prefix.Length;
            var applicable = snapshot.CreateTrackingSpan(start, prefix.Length, SpanTrackingMode.EdgeInclusive);
            if (builtInSet != null)
            {
                var builtInNames = new HashSet<string>(builtInSet.Completions.Select(item => item.InsertionText), StringComparer.OrdinalIgnoreCase);
                var overlap = candidates.Count(candidate => builtInNames.Contains(candidate.Name));
                if (hasResolvedMemberContext) indexService.ReportIntelliSenseEvidence(overlap, candidates.Count);
                if (indexService.IntelliSenseReady) return;

                var previewItems = candidates.Where(candidate => !builtInNames.Contains(candidate.Name)).Select(CreateCompletion).ToList();
                if (previewItems.Count == 0) return;
                completionSets.Insert(0, new PreviewCompletionSet(applicable, previewItems));
                MarkUEAssistSession(session);
                return;
            }

            var completions = candidates.Select(CreateCompletion).ToList();
            completionSets.Add(new PreviewCompletionSet(applicable, completions));
            MarkUEAssistSession(session);
        }

        public void Dispose()
        {
            disposed = true;
        }

        private static Completion CreateCompletion(IndexedSymbol symbol)
        {
            var owner = string.IsNullOrWhiteSpace(symbol.OwnerType) ? string.Empty : " — " + symbol.OwnerType;
            return new Completion4(
                symbol.Name,
                symbol.Name,
                symbol.Kind + owner + " (UEAssist 미리보기)",
                CompletionIcons.For(symbol.Kind),
                symbol.Kind.ToString(),
                null,
                "UEAssist");
        }

        private static string GetIdentifierPrefix(string text)
        {
            var index = text.Length;
            while (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_')) index--;
            return text.Substring(index);
        }

        private static string NormalizeType(string typeName)
        {
            return Regex.Replace(typeName ?? string.Empty, @"\bconst\b|[*&\s]", string.Empty);
        }

        private static void MarkUEAssistSession(ICompletionSession session)
        {
            if (!session.Properties.ContainsProperty(UEAssistCompletionSessionState.Marker))
            {
                session.Properties.AddProperty(UEAssistCompletionSessionState.Marker, true);
            }
        }
    }

    internal static class UEAssistCompletionSessionState
    {
        internal static readonly object Marker = new object();
    }

    internal sealed class PreviewCompletionSet : CompletionSet
    {
        public PreviewCompletionSet(ITrackingSpan applicableTo, IList<Completion> completions)
            : base("UEAssistPreview", "UEAssist 미리보기", applicableTo, completions, null)
        {
        }

        public override IReadOnlyList<Span> GetHighlightedSpansInDisplayText(string displayText)
        {
            var snapshot = ApplicableTo.TextBuffer.CurrentSnapshot;
            var query = ApplicableTo.GetText(snapshot);
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(displayText)) return null;

            var start = displayText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (start >= 0) return new[] { new Span(start, query.Length) };

            var spans = new List<Span>();
            var queryIndex = 0;
            for (var index = 0; index < displayText.Length && queryIndex < query.Length; index++)
            {
                if (char.ToUpperInvariant(displayText[index]) == char.ToUpperInvariant(query[queryIndex]))
                {
                    spans.Add(new Span(index, 1));
                    queryIndex++;
                }
            }
            return queryIndex == query.Length ? spans : null;
        }

    }


    internal static class CompletionIcons
    {
        public static Microsoft.VisualStudio.Imaging.Interop.ImageMoniker For(SymbolKind kind)
        {
            return kind == SymbolKind.Type
                ? KnownMonikers.Class
                : kind == SymbolKind.Function
                    ? KnownMonikers.Method
                    : kind == SymbolKind.Macro
                        ? KnownMonikers.MacroPublic
                    : kind == SymbolKind.Specifier
                            ? KnownMonikers.IntellisenseKeyword
                            : kind == SymbolKind.Header
                                ? KnownMonikers.CPPHeaderFile
                                : KnownMonikers.Field;
        }
    }
}
