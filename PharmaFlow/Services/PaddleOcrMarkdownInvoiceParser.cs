using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaFlow.Services;

public static class PaddleOcrMarkdownInvoiceParser
{
    private static readonly Regex TableSeparatorRegex = new(
        @"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberRegex = new(
        @"(?<![A-Za-z])\d+(?:\.\d+)?(?![A-Za-z])",
        RegexOptions.Compiled);

    public static IReadOnlyList<InvoiceVisionItem> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n');

        var results = new List<InvoiceVisionItem>();

        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!ContainsTablePipe(lines[i]) || !TableSeparatorRegex.IsMatch(lines[i + 1]))
                continue;

            var headers = SplitCells(lines[i]);
            if (headers.Count < 4)
                continue;

            var productIndex = FindHeaderIndex(
                headers,
                ["item description", "product description", "item name", "product name", "description", "item", "product", "particular"]);

            var batchIndex = FindHeaderIndex(
                headers,
                ["batch no", "batch number", "batch", "lot no", "lot number", "lot"]);

            var expiryIndex = FindHeaderIndex(
                headers,
                ["expiry date", "exp date", "expiry", "exp", "e d"]);

            var quantityIndex = FindQuantityHeaderIndex(headers);

            if (productIndex < 0 || batchIndex < 0 || expiryIndex < 0 || quantityIndex < 0)
                continue;

            var rowNumber = results.Count + 1;

            for (var rowIndex = i + 2; rowIndex < lines.Length; rowIndex++)
            {
                if (!ContainsTablePipe(lines[rowIndex]))
                    break;

                var cells = SplitCells(lines[rowIndex]);
                if (cells.Count == 0 || IsSeparatorOnlyRow(cells))
                    continue;

                var product = Cell(cells, productIndex);
                var batch = Cell(cells, batchIndex);
                var expiry = Cell(cells, expiryIndex);
                var quantityText = Cell(cells, quantityIndex);

                if (IsLikelyFooter(product) || IsLikelyFooter(batch))
                    continue;

                var quantity = ParseQuantity(quantityText);
                var presentFields =
                    (!string.IsNullOrWhiteSpace(product) ? 1 : 0) +
                    (!string.IsNullOrWhiteSpace(batch) ? 1 : 0) +
                    (!string.IsNullOrWhiteSpace(expiry) ? 1 : 0) +
                    (quantity > 0m ? 1 : 0);

                if (presentFields < 2)
                    continue;

                // Heuristic completeness score for the review UI, not a model probability.
                var confidence = Math.Clamp(35m + presentFields * 15m, 0m, 95m);

                results.Add(new InvoiceVisionItem(
                    rowNumber++,
                    CleanCell(product, 200),
                    CleanCell(batch, 100),
                    CleanCell(expiry, 40),
                    quantity,
                    confidence));
            }

            if (results.Count > 0)
                break;
        }

        if (results.Count > 0)
            return results;

        // PaddleOCR normally returns Markdown tables. If a table is emitted in a
        // less-standard form, reuse PharmaFlow's existing HSN-anchored parser
        // as a defensive fallback over the recognized Markdown text.
        var fallbackLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, index) => new PharmaFlow.Models.ViewModels.InvoiceOcrLineInput
            {
                Text = CleanMarkdownTableLine(line),
                Confidence = 85m
            })
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();

        var parsed = InvoiceOcrParser.Parse(fallbackLines);

        return parsed
            .Take(200)
            .Select(item => new InvoiceVisionItem(
                item.LineNumber,
                item.ProductName,
                item.BatchNumber,
                item.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Quantity.GetValueOrDefault(),
                item.Confidence))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductName) &&
                !string.IsNullOrWhiteSpace(item.BatchNumber) &&
                item.Quantity > 0m)
            .ToList();
    }

    private static int FindHeaderIndex(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> preferredNames)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);

            if (preferredNames.Any(name =>
                    normalized.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);

            if (preferredNames.Any(name =>
                    normalized.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindQuantityHeaderIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);

            if (normalized.Contains("billed", StringComparison.OrdinalIgnoreCase) &&
                (normalized.Contains("qty", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("quantity", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Equals("billed", StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);

            if ((normalized.Contains("qty", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("quantity", StringComparison.OrdinalIgnoreCase)) &&
                !normalized.Contains("free", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> SplitCells(string line)
    {
        var value = line.Trim();

        if (value.StartsWith("|", StringComparison.Ordinal))
            value = value[1..];

        if (value.EndsWith("|", StringComparison.Ordinal))
            value = value[..^1];

        return value
            .Split('|')
            .Select(CleanMarkdown)
            .ToList();
    }

    private static string Cell(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index] : string.Empty;

    private static bool ContainsTablePipe(string line) =>
        line.Contains('|', StringComparison.Ordinal);

    private static bool IsSeparatorOnlyRow(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(cell =>
            string.IsNullOrWhiteSpace(cell) || cell.All(ch => ch is '-' or ':' or ' '));

    private static decimal ParseQuantity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        var match = NumberRegex.Match(value);
        if (!match.Success)
            return 0m;

        return decimal.TryParse(
            match.Value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var quantity) && quantity is > 0m and <= 1_000_000m
            ? quantity
            : 0m;
    }

    private static bool IsLikelyFooter(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text.Contains("grand total", StringComparison.Ordinal) ||
               text.Contains("total amount", StringComparison.Ordinal) ||
               text.Contains("amount in words", StringComparison.Ordinal) ||
               text.Contains("taxable value", StringComparison.Ordinal);
    }

    private static string NormalizeHeader(string value)
    {
        var text = CleanMarkdown(value).ToLowerInvariant();

        text = text.Replace("_", " ", StringComparison.Ordinal)
                   .Replace("-", " ", StringComparison.Ordinal)
                   .Replace(".", string.Empty, StringComparison.Ordinal)
                   .Replace("/", " ", StringComparison.Ordinal)
                   .Replace("\\", " ", StringComparison.Ordinal);

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string CleanCell(string value, int maxLength)
    {
        var cleaned = CleanMarkdown(value)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        cleaned = Regex.Replace(cleaned, @"\s+", " ");

        return cleaned.Length <= maxLength
            ? cleaned
            : cleaned[..maxLength];
    }

    private static string CleanMarkdownTableLine(string value)
    {
        var cleaned = value.Trim();

        if (cleaned.StartsWith("|", StringComparison.Ordinal))
            cleaned = cleaned[1..];

        if (cleaned.EndsWith("|", StringComparison.Ordinal))
            cleaned = cleaned[..^1];

        return Regex.Replace(cleaned, @"\s*\|\s*", " ");
    }

    private static string CleanMarkdown(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        cleaned = cleaned.Replace("**", string.Empty, StringComparison.Ordinal)
                         .Replace("__", string.Empty, StringComparison.Ordinal)
                         .Trim();

        return cleaned;
    }
}
