using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
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
        private static readonly Regex MemberPattern = new Regex(@"(?<receiver>[A-Za-z_]\w*)(?<call>\s*\(\s*\))?\s*(?:->|\.)\s*(?<prefix>[A-Za-z_]\w*)?$", RegexOptions.Compiled);
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
            if (disposed || indexService.Index.Count == 0 || HasIntelliSenseResults(completionSets)) return;
            var point = session.GetTriggerPoint(buffer.CurrentSnapshot);
            if (!point.HasValue) return;

            var snapshot = point.Value.Snapshot;
            var line = snapshot.GetLineFromPosition(point.Value.Position);
            var beforeCaret = snapshot.GetText(line.Start.Position, point.Value.Position - line.Start.Position);
            var memberMatch = MemberPattern.Match(beforeCaret);
            string prefix;
            IReadOnlyList<IndexedSymbol> candidates;
            if (memberMatch.Success)
            {
                prefix = memberMatch.Groups["prefix"].Value;
                var receiver = memberMatch.Groups["receiver"].Value;
                var typeName = receiver == "Super"
                    ? string.Empty
                    : memberMatch.Groups["call"].Success
                        ? indexService.Index.ResolveReturnType(receiver)
                        : indexService.Index.ResolveVariableType(receiver);
                candidates = string.IsNullOrWhiteSpace(typeName)
                    ? indexService.Index.Complete(prefix)
                    : indexService.Index.CompleteMembers(NormalizeType(typeName), prefix);
            }
            else
            {
                prefix = GetIdentifierPrefix(beforeCaret);
                candidates = indexService.Index.Complete(prefix);
            }

            if (candidates.Count == 0) return;
            var start = point.Value.Position - prefix.Length;
            var applicable = snapshot.CreateTrackingSpan(start, prefix.Length, SpanTrackingMode.EdgeInclusive);
            var completions = candidates.Select(CreateCompletion).ToList();
            completionSets.Add(new CompletionSet("UEAssistPreview", "UEAssist 미리보기", applicable, completions, null));
        }

        public void Dispose()
        {
            disposed = true;
        }

        private static Completion CreateCompletion(IndexedSymbol symbol)
        {
            var owner = string.IsNullOrWhiteSpace(symbol.OwnerType) ? string.Empty : " — " + symbol.OwnerType;
            return new Completion(symbol.Name, symbol.Name, symbol.Kind + owner + " (UEAssist 미리보기)", CompletionIcons.For(symbol.Kind), symbol.Kind.ToString());
        }

        private static bool HasIntelliSenseResults(IEnumerable<CompletionSet> completionSets)
        {
            return completionSets.Any(set => set.Completions != null && set.Completions.Count > 0 && !string.Equals(set.Moniker, "UEAssistPreview", StringComparison.Ordinal));
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
    }


    internal static class CompletionIcons
    {
        private static readonly ImageSource TypeIcon = CreateIcon(SymbolKind.Type);
        private static readonly ImageSource FunctionIcon = CreateIcon(SymbolKind.Function);
        private static readonly ImageSource VariableIcon = CreateIcon(SymbolKind.Variable);

        public static ImageSource For(SymbolKind kind)
        {
            return kind == SymbolKind.Type ? TypeIcon : kind == SymbolKind.Function ? FunctionIcon : VariableIcon;
        }

        private static ImageSource CreateIcon(SymbolKind kind)
        {
            var color = kind == SymbolKind.Type ? Color.FromRgb(78, 201, 176)
                : kind == SymbolKind.Function ? Color.FromRgb(189, 147, 249)
                : Color.FromRgb(86, 156, 214);
            Geometry geometry = kind == SymbolKind.Function
                ? new EllipseGeometry(new Point(8, 8), 5, 5)
                : kind == SymbolKind.Type
                    ? Geometry.Parse("M 8,2 L 14,8 L 8,14 L 2,8 Z")
                    : new RectangleGeometry(new Rect(3, 3, 10, 10), 1, 1);
            var drawing = new GeometryDrawing(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1), geometry);
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
    }
}
