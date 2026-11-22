using System.Text;

namespace LumaWall.Application.Steam;

// Valve KeyValues, limited to the libraryfolders document. Both old numeric
// path values and current numeric objects containing "path" are accepted.
internal static class SteamLibraryFolders
{
    private sealed record Pair(string Key, string? Value, List<Pair>? Children);
    public static string[] Parse(string text, CancellationToken token)
    {
        var reader = new Reader(text, token);
        var document = reader.Read(false, 0);
        var root = document.FirstOrDefault(p => p.Key.Equals("libraryfolders", StringComparison.OrdinalIgnoreCase))?.Children
            ?? throw new InvalidDataException("Steam 库列表格式无效。");
        return root.Where(p => p.Key.Length > 0 && p.Key.All(char.IsAsciiDigit))
            .Select(p => p.Value ?? p.Children?.FirstOrDefault(v => v.Key.Equals("path", StringComparison.OrdinalIgnoreCase))?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class Reader(string text, CancellationToken token)
    {
        private int offset, tokens;
        public List<Pair> Read(bool nested, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Steam 库列表层级过深。");
            var result = new List<Pair>();
            while (Next() is { } key)
            {
                if (key == "}") { if (nested) return result; throw new InvalidDataException("Steam 库列表括号无效。"); }
                if (key == "{") throw new InvalidDataException("Steam 库列表缺少名称。");
                string value = Next() ?? throw new InvalidDataException("Steam 库列表被截断。");
                if (value == "}") throw new InvalidDataException("Steam 库列表缺少值。");
                result.Add(value == "{" ? new(key, null, Read(true, depth + 1)) : new(key, value, null));
            }
            if (nested) throw new InvalidDataException("Steam 库列表被截断。");
            return result;
        }
        private string? Next()
        {
            token.ThrowIfCancellationRequested();
            if (++tokens > 100_000) throw new InvalidDataException("Steam 库列表条目过多。");
            while (offset < text.Length)
            {
                if (char.IsWhiteSpace(text[offset]) || text[offset] == '\uFEFF') { offset++; continue; }
                if (text[offset] == '/' && offset + 1 < text.Length && text[offset + 1] == '/')
                { while (offset < text.Length && text[offset] != '\n') offset++; continue; }
                break;
            }
            if (offset == text.Length) return null;
            char first = text[offset++];
            if (first is '{' or '}') return first.ToString();
            var value = new StringBuilder();
            if (first == '"')
            {
                while (offset < text.Length)
                {
                    char c = text[offset++];
                    if (c == '"') return value.ToString();
                    if (c == '\\' && offset < text.Length && text[offset] is '\\' or '"') c = text[offset++];
                    value.Append(c);
                    if (value.Length > 32767) throw new InvalidDataException("Steam 库路径过长。");
                }
                throw new InvalidDataException("Steam 库列表字符串被截断。");
            }
            value.Append(first);
            while (offset < text.Length && !char.IsWhiteSpace(text[offset]) && text[offset] is not ('{' or '}'))
                value.Append(text[offset++]);
            return value.ToString();
        }
    }
}
