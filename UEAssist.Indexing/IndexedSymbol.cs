using UEAssist.Core;

namespace UEAssist.Indexing
{
    public sealed class IndexedSymbol
    {
        public IndexedSymbol(string name, SymbolKind kind, string filePath, int line, int column, string ownerType = null, string valueType = null, string baseType = null)
        {
            Name = name;
            Kind = kind;
            FilePath = filePath;
            Line = line;
            Column = column;
            OwnerType = ownerType ?? string.Empty;
            ValueType = valueType ?? string.Empty;
            BaseType = baseType ?? string.Empty;
        }

        public string Name { get; }
        public SymbolKind Kind { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public string OwnerType { get; }
        public string ValueType { get; }
        public string BaseType { get; }
    }
}
