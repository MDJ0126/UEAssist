namespace UEAssist.Core
{
    public enum SymbolKind
    {
        Type,
        Function,
        Variable,
        Macro
    }

    public sealed class SourceSymbol
    {
        public SourceSymbol(string name, string filePath, int line, int column, SymbolKind kind)
        {
            Name = name;
            FilePath = filePath;
            Line = line;
            Column = column;
            Kind = kind;
        }

        public string Name { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public SymbolKind Kind { get; }
    }
}
