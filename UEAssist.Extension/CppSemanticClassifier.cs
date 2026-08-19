using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using UEAssist.Core;

namespace UEAssist.Extension
{
    internal static class CppSemanticClassification
    {
        public const string TypeName = "UEAssist C++ Type";
        public const string VariableName = "UEAssist C++ Variable";

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(TypeName)]
        [BaseDefinition("C++ User Types")]
        internal static ClassificationTypeDefinition TypeDefinition = null;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(VariableName)]
        [BaseDefinition("C++ Variables")]
        internal static ClassificationTypeDefinition VariableDefinition = null;
    }

    [Export(typeof(IClassifierProvider))]
    [ContentType("C/C++")]
    internal sealed class CppSemanticClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService Registry = null;

        public IClassifier GetClassifier(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new CppSemanticClassifier(
                    textBuffer,
                    Registry.GetClassificationType(CppSemanticClassification.TypeName),
                    Registry.GetClassificationType(CppSemanticClassification.VariableName)));
        }
    }

    internal sealed class CppSemanticClassifier : IClassifier
    {
        private readonly ITextBuffer textBuffer;
        private readonly IClassificationType typeClassification;
        private readonly IClassificationType variableClassification;
        private ITextSnapshot cachedSnapshot;
        private IReadOnlyList<SemanticToken> cachedTokens = Array.Empty<SemanticToken>();

        public CppSemanticClassifier(ITextBuffer textBuffer, IClassificationType typeClassification, IClassificationType variableClassification)
        {
            this.textBuffer = textBuffer;
            this.typeClassification = typeClassification;
            this.variableClassification = variableClassification;
            textBuffer.Changed += OnBufferChanged;
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            EnsureParsed(span.Snapshot);
            var results = new List<ClassificationSpan>();
            foreach (var token in cachedTokens)
            {
                if (token.Start >= span.End.Position || token.Start + token.Length <= span.Start.Position)
                {
                    continue;
                }

                var classification = token.Kind == SemanticTokenKind.Type ? typeClassification : variableClassification;
                results.Add(new ClassificationSpan(new SnapshotSpan(span.Snapshot, token.Start, token.Length), classification));
            }

            return results;
        }

        private void EnsureParsed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(snapshot, cachedSnapshot))
            {
                return;
            }

            cachedTokens = CppSemanticParser.Parse(snapshot.GetText());
            cachedSnapshot = snapshot;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            cachedSnapshot = null;
            cachedTokens = Array.Empty<SemanticToken>();
            if (e.After.Length > 0)
            {
                ClassificationChanged?.Invoke(this, new ClassificationChangedEventArgs(new SnapshotSpan(e.After, 0, e.After.Length)));
            }
        }
    }
}
