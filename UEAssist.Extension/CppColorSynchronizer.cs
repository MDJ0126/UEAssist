using Microsoft.VisualStudio.Text.Classification;
using System;
using System.ComponentModel.Composition;

namespace UEAssist.Extension
{
    [Export]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class CppColorSynchronizer
    {
        private readonly IClassificationTypeRegistryService registry;
        private readonly IClassificationFormatMap formatMap;
        private bool synchronizing;

        [ImportingConstructor]
        public CppColorSynchronizer(
            IClassificationTypeRegistryService registry,
            IClassificationFormatMapService formatMapService)
        {
            this.registry = registry;
            formatMap = formatMapService.GetClassificationFormatMap("text");
            formatMap.ClassificationFormatMappingChanged += OnFormatMappingChanged;
        }

        public void Synchronize()
        {
            if (synchronizing)
            {
                return;
            }

            synchronizing = true;
            try
            {
                CopyWhenAvailable("C++ Macros", UnrealMacroClassification.Name);
                CopyWhenAvailable("C++ User Types", CppSemanticClassification.TypeName);
                CopyWhenAvailable("C++ Variables", CppSemanticClassification.VariableName);
            }
            finally
            {
                synchronizing = false;
            }
        }

        private void CopyWhenAvailable(string sourceName, string targetName)
        {
            var source = registry.GetClassificationType(sourceName);
            var target = registry.GetClassificationType(targetName);
            if (source == null || target == null)
            {
                return;
            }

            var sourceProperties = formatMap.GetExplicitTextProperties(source);
            if (sourceProperties.ForegroundBrushEmpty && sourceProperties.BackgroundBrushEmpty)
            {
                return;
            }

            var targetProperties = formatMap.GetTextProperties(target);
            if (!Equals(sourceProperties, targetProperties))
            {
                formatMap.SetTextProperties(target, sourceProperties);
            }
        }

        private void OnFormatMappingChanged(object sender, EventArgs e)
        {
            Synchronize();
        }
    }
}
