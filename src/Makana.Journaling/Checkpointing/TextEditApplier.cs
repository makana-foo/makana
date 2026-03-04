using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Makana.Journaling.Checkpointing;

internal static class TextEditApplier
{
    public static bool TryApplyTextEdits(string source, string editsJson, out string result)
    {
        result = source;
        using var doc = JsonDocument.Parse(editsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var edits = new List<(int Start, int End, string NewText)>();
        foreach (var editElement in doc.RootElement.EnumerateArray())
        {
            if (editElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetEditSpan(editElement, source, out var start, out var end))
            {
                return false;
            }

            if (!TryGetEditText(editElement, out var newText))
            {
                return false;
            }

            edits.Add((start, end, newText));
        }

        edits.Sort((left, right) => right.Start.CompareTo(left.Start));

        var current = source;
        foreach (var edit in edits)
        {
            if (edit.Start < 0 || edit.End < edit.Start || edit.End > current.Length)
            {
                return false;
            }

            current = string.Concat(
                current.AsSpan(0, edit.Start),
                edit.NewText,
                current.AsSpan(edit.End));
        }

        result = current;
        return true;
    }

    public static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static bool TryGetEditText(JsonElement editElement, out string newText)
    {
        if (TryGetProperty(editElement, "newText", out var newTextElement) ||
            TryGetProperty(editElement, "NewText", out newTextElement))
        {
            if (newTextElement.ValueKind == JsonValueKind.String)
            {
                newText = newTextElement.GetString() ?? string.Empty;
                return true;
            }
        }

        newText = string.Empty;
        return false;
    }

    private static bool TryGetEditSpan(JsonElement editElement, string source, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (!TryGetProperty(editElement, "span", out var spanElement) &&
            !TryGetProperty(editElement, "Span", out spanElement))
        {
            return false;
        }

        if (!TryGetSpanOffset(spanElement, "start", source, out start) &&
            !TryGetSpanOffset(spanElement, "Start", source, out start))
        {
            return false;
        }

        if (!TryGetSpanOffset(spanElement, "end", source, out end) &&
            !TryGetSpanOffset(spanElement, "End", source, out end))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetSpanOffset(JsonElement spanElement, string name, string source, out int offset)
    {
        offset = 0;
        if (!TryGetProperty(spanElement, name, out var point))
        {
            return false;
        }

        if (point.ValueKind == JsonValueKind.Number)
        {
            offset = point.GetInt32();
            return true;
        }

        if (point.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetProperty(point, "offset", out var offsetElement) ||
            TryGetProperty(point, "Offset", out offsetElement) ||
            TryGetProperty(point, "index", out offsetElement) ||
            TryGetProperty(point, "Index", out offsetElement))
        {
            if (offsetElement.ValueKind == JsonValueKind.Number)
            {
                offset = offsetElement.GetInt32();
                return true;
            }
        }

        if (!TryGetLineColumn(point, out var line, out var column))
        {
            return false;
        }

        offset = LineColumnToOffset(source, line, column);
        return offset >= 0;
    }

    private static bool TryGetLineColumn(JsonElement point, out int line, out int column)
    {
        line = 0;
        column = 0;

        if (!(TryGetProperty(point, "line", out var lineElement) ||
              TryGetProperty(point, "Line", out lineElement)))
        {
            return false;
        }

        if (lineElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        line = lineElement.GetInt32();

        if (TryGetProperty(point, "column", out var columnElement) ||
            TryGetProperty(point, "Column", out columnElement) ||
            TryGetProperty(point, "character", out columnElement) ||
            TryGetProperty(point, "Character", out columnElement))
        {
            if (columnElement.ValueKind == JsonValueKind.Number)
            {
                column = columnElement.GetInt32();
                return true;
            }
        }

        return false;
    }

    private static int LineColumnToOffset(string source, int line, int column)
    {
        if (line < 0 || column < 0)
        {
            return -1;
        }

        var currentLine = 0;
        var index = 0;
        while (index < source.Length && currentLine < line)
        {
            if (source[index] == '\n')
            {
                currentLine++;
            }

            index++;
        }

        if (currentLine != line)
        {
            return -1;
        }

        if (index + column > source.Length)
        {
            return -1;
        }

        return index + column;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

