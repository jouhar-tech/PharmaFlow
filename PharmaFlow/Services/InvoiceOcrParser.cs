using System.Globalization;
using System.Text.RegularExpressions;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Services;

public static class InvoiceOcrParser
{
    private static readonly Regex NumericDateRegex = new(
        @"\b(?<day>0?[1-9]|[12]\d|3[01])\s*[/.-]\s*(?<month>0?[1-9]|1[0-2])\s*[/.-]\s*(?<year>\d{2}|\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MonthYearRegex = new(
        @"\b(?<month>0?[1-9]|1[0-2])\s*[/.-]\s*(?<year>\d{2}|\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TextDateRegex = new(
        @"\b(?<day>0?[1-9]|[12]\d|3[01])?\s*(?<month>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s*(?<year>\d{2}|\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LabeledBatchRegex = new(
        @"\b(?:batch(?:\s*no)?|b\.?\s*no\.?|lot(?:\s*no)?)\s*[:#.-]?\s*(?<value>[A-Za-z0-9][A-Za-z0-9._/-]{1,24})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LabeledQuantityRegex = new(
        @"\b(?:qty|quantity|qnty|units|unit|no\.?\s*of)\s*[:#.-]?\s*(?<value>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LabelCutRegex = new(
        @"\b(?:batch(?:\s*no)?|b\.?\s*no\.?|lot(?:\s*no)?|exp(?:iry)?(?:\s*date)?|e\.?d\.?|qty|quantity|qnty|units|unit)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TokenRegex = new(
        @"[A-Za-z0-9][A-Za-z0-9._/-]{2,24}",
        RegexOptions.Compiled);

    private static readonly Regex NumberRegex = new(
        @"(?<![A-Za-z])\d+(?:\.\d+)?(?![A-Za-z])",
        RegexOptions.Compiled);

    private static readonly Regex LeadingRowNumberRegex = new(
        @"^\s*\d{1,4}\s*[.)-]?\s*",
        RegexOptions.Compiled);

    private static readonly string[] NoiseTerms =
    [
        "tax invoice", "invoice no", "invoice number", "gstin", "pan no",
        "description", "product description", "item description", "item name",
        "hsn", "hsn code", "gst", "cgst", "sgst", "igst", "mrp", "rate",
        "amount", "discount", "subtotal", "grand total", "total amount",
        "taxable value", "supplier", "customer", "address", "phone", "mobile",
        "email", "doctor", "payment", "balance", "round off", "terms"
    ];

    private static readonly HashSet<string> ExcludedBatchWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "MRP", "GST", "CGST", "SGST", "IGST", "HSN", "QTY", "PCS", "PACK"
    };

    private static readonly Regex UnitTokenRegex = new(
        @"^\d+(?:\.\d+)?(?:MG|MCG|G|KG|ML|L|TAB|TABS|CAP|CAPS|PCS|PACK|%|IU)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ParsedInvoiceItem> Parse(IReadOnlyList<InvoiceOcrLineInput> inputLines)
    {
        var lines = inputLines
            .Select((line, index) => new NormalizedLine(
                Math.Max(0, index + 1),
                CleanLine(line.Text),
                Math.Clamp(line.Confidence, 0m, 100m)))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Where(line => line.Text.Length >= 3)
            .ToList();

        var candidates = new List<ParsedInvoiceItem>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (IsNoiseLine(lines[index].Text))
                continue;

            var candidate = ParseLine(lines[index]);

            if (candidate.Score < 0.50m && index + 1 < lines.Count && !IsNoiseLine(lines[index + 1].Text))
            {
                var combined = new NormalizedLine(
                    lines[index].LineNumber,
                    CleanLine(lines[index].Text + " " + lines[index + 1].Text),
                    (lines[index].Confidence + lines[index + 1].Confidence) / 2m);

                var joinedCandidate = ParseLine(combined);
                if (joinedCandidate.Score > candidate.Score)
                    candidate = joinedCandidate;
            }

            if (candidate.Score >= 0.50m && !string.IsNullOrWhiteSpace(candidate.ProductName))
                candidates.Add(candidate);
        }

        return MergeDuplicates(candidates);
    }

    private static ParsedInvoiceItem ParseLine(NormalizedLine line)
    {
        var text = line.Text;
        var productName = ExtractProductName(text);
        if (string.IsNullOrWhiteSpace(productName))
            return new ParsedInvoiceItem(line.LineNumber, line.Text, string.Empty, string.Empty, null, null, line.Confidence, 0m);

        var expiry = ExtractExpiry(text);
        var batch = ExtractBatch(text, expiry.Position);
        var quantity = ExtractQuantity(text, expiry.Position);

        var productConfidence = 0.82m;
        var batchConfidence = batch.Found ? (batch.Explicit ? 0.96m : 0.72m) : 0m;
        var expiryConfidence = expiry.Found ? (expiry.Explicit ? 0.96m : 0.80m) : 0m;
        var quantityConfidence = quantity.Found ? (quantity.Explicit ? 0.95m : 0.70m) : 0m;

        var score = (
            0.25m +
            (batch.Found ? 0.25m : 0m) +
            (expiry.Found ? 0.25m : 0m) +
            (quantity.Found ? 0.25m : 0m));

        var fieldAverage = new[] { productConfidence, batchConfidence, expiryConfidence, quantityConfidence }
            .Where(value => value > 0m)
            .DefaultIfEmpty(0m)
            .Average();

        var confidence = Math.Clamp(((fieldAverage * 0.80m) + ((line.Confidence / 100m) * 0.20m)) * 100m, 0m, 100m);

        return new ParsedInvoiceItem(
            line.LineNumber,
            line.Text,
            productName,
            batch.Value,
            expiry.Value,
            quantity.Value,
            confidence,
            score);
    }

    private static string ExtractProductName(string text)
    {
        var working = LeadingRowNumberRegex.Replace(text, string.Empty).Trim();

        var cutPositions = new List<int>();

        var labelMatch = LabelCutRegex.Match(working);
        if (labelMatch.Success && labelMatch.Index > 1)
            cutPositions.Add(labelMatch.Index);

        var expiry = ExtractExpiry(working);
        if (expiry.Position > 1)
            cutPositions.Add(expiry.Position);

        var end = cutPositions.Count > 0 ? cutPositions.Min() : working.Length;
        var product = working[..end];

        product = Regex.Replace(product, @"\s{2,}", " ");
        product = Regex.Replace(product, @"^[^A-Za-z0-9]+|[^A-Za-z0-9)%+/-]+$", string.Empty);
        product = product.Trim();

        if (product.Length is < 2 or > 200)
            return string.Empty;

        if (IsNoiseLine(product))
            return string.Empty;

        return product;
    }

    private static BatchResult ExtractBatch(string text, int expiryPosition)
    {
        var labeled = LabeledBatchRegex.Match(text);
        if (labeled.Success)
            return new BatchResult(labeled.Groups["value"].Value.Trim(), true);

        var searchText = expiryPosition > 0 ? text[..expiryPosition] : text;
        var tokens = TokenRegex.Matches(searchText);

        string? best = null;
        foreach (Match tokenMatch in tokens)
        {
            var token = tokenMatch.Value.Trim();
            if (ExcludedBatchWords.Contains(token) || UnitTokenRegex.IsMatch(token))
                continue;

            var hasLetter = token.Any(char.IsLetter);
            var hasDigit = token.Any(char.IsDigit);

            if (!hasLetter || !hasDigit)
                continue;

            if (token.Length is < 3 or > 20)
                continue;

            best = token;
        }

        return new BatchResult(best ?? string.Empty, false);
    }

    private static QuantityResult ExtractQuantity(string text, int expiryPosition)
    {
        var labeled = LabeledQuantityRegex.Match(text);
        if (labeled.Success &&
            decimal.TryParse(labeled.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var labeledValue) &&
            labeledValue > 0m)
        {
            return new QuantityResult(labeledValue, true);
        }

        if (expiryPosition >= 0 && expiryPosition < text.Length)
        {
            var afterExpiry = text[expiryPosition..];
            foreach (Match match in NumberRegex.Matches(afterExpiry))
            {
                var token = match.Value;
                if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    continue;

                if (value <= 0m || value > 1_000_000m)
                    continue;

                if (token.Contains('.') && token.Split('.').Last().Length == 2)
                    continue;

                return new QuantityResult(value, false);
            }
        }

        return new QuantityResult(null, false);
    }

    private static ExpiryResult ExtractExpiry(string text)
    {
        var explicitLabel = Regex.Match(
            text,
            @"\b(?:expiry|exp\.?|e\.?d\.?)\s*(?:date)?\s*[:#.-]?\s*",
            RegexOptions.IgnoreCase);

        var start = explicitLabel.Success ? explicitLabel.Index + explicitLabel.Length : 0;
        var searchText = explicitLabel.Success ? text[start..] : text;

        var textual = TextDateRegex.Match(searchText);
        if (textual.Success && TryBuildTextDate(textual, out var textDate))
            return new ExpiryResult(textDate, explicitLabel.Success, start + textual.Index);

        var numeric = NumericDateRegex.Match(searchText);
        if (numeric.Success && TryBuildNumericDate(numeric, out var numericDate))
            return new ExpiryResult(numericDate, explicitLabel.Success, start + numeric.Index);

        var monthYear = MonthYearRegex.Match(searchText);
        if (monthYear.Success && TryBuildMonthYear(monthYear, out var monthDate))
            return new ExpiryResult(monthDate, explicitLabel.Success, start + monthYear.Index);

        return new ExpiryResult(null, false, -1);
    }

    private static bool TryBuildNumericDate(Match match, out DateOnly date)
    {
        date = default;

        if (!int.TryParse(match.Groups["day"].Value, out var day) ||
            !int.TryParse(match.Groups["month"].Value, out var month) ||
            !int.TryParse(match.Groups["year"].Value, out var year))
            return false;

        year = NormalizeYear(year);
        return DateOnly.TryParse(
            $"{year:0000}-{month:00}-{day:00}",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool TryBuildMonthYear(Match match, out DateOnly date)
    {
        date = default;

        if (!int.TryParse(match.Groups["month"].Value, out var month) ||
            !int.TryParse(match.Groups["year"].Value, out var year))
            return false;

        year = NormalizeYear(year);
        return DateOnly.TryParse(
            $"{year:0000}-{month:00}-01",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date) && date <= date.AddMonths(1).AddDays(-1);
    }

    private static bool TryBuildTextDate(Match match, out DateOnly date)
    {
        date = default;

        var dayValue = match.Groups["day"].Value;
        var monthValue = match.Groups["month"].Value;
        var yearValue = match.Groups["year"].Value;

        if (!int.TryParse(yearValue, out var year))
            return false;

        year = NormalizeYear(year);

        var month = monthValue.Length >= 3
            ? DateTime.ParseExact(
                monthValue[..3],
                "MMM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces).Month
            : 0;

        var day = string.IsNullOrWhiteSpace(dayValue) ? 1 : int.Parse(dayValue);

        return DateOnly.TryParse(
            $"{year:0000}-{month:00}-{day:00}",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    private static bool IsNoiseLine(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();

        if (normalized.Length < 3)
            return true;

        return NoiseTerms.Any(term =>
            normalized == term ||
            normalized.StartsWith(term + " ") ||
            normalized.EndsWith(" " + term));
    }

    private static string CleanLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    private static IReadOnlyList<ParsedInvoiceItem> MergeDuplicates(IReadOnlyList<ParsedInvoiceItem> items)
    {
        var merged = new List<ParsedInvoiceItem>();
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var key = $"{NormalizeKey(item.ProductName)}|{NormalizeKey(item.BatchNumber)}|{item.ExpiryDate:yyyy-MM-dd}";

            if (!lookup.TryGetValue(key, out var existingIndex))
            {
                lookup[key] = merged.Count;
                merged.Add(item);
                continue;
            }

            var existing = merged[existingIndex];
            var totalQuantity = existing.Quantity.GetValueOrDefault() + item.Quantity.GetValueOrDefault();

            merged[existingIndex] = existing with
            {
                Quantity = totalQuantity > 0m
                    ? totalQuantity
                    : existing.Quantity ?? item.Quantity,
                Confidence = Math.Max(existing.Confidence, item.Confidence),
                Score = Math.Max(existing.Score, item.Score)
            };
        }

        return merged;
    }

    private static string NormalizeKey(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", " ");
    
    private sealed record NormalizedLine(int LineNumber, string Text, decimal Confidence);

    private sealed record BatchResult(string Value, bool Explicit)
    {
        public bool Found => !string.IsNullOrWhiteSpace(Value);
    }

    private sealed record QuantityResult(decimal? Value, bool Explicit)
    {
        public bool Found => Value.HasValue && Value.Value > 0m;
    }

    private sealed record ExpiryResult(DateOnly? Value, bool Explicit, int Position)
    {
        public bool Found => Value.HasValue;
    }

    public sealed record ParsedInvoiceItem(
        int LineNumber,
        string RawLine,
        string ProductName,
        string BatchNumber,
        DateOnly? ExpiryDate,
        decimal? Quantity,
        decimal Confidence,
        decimal Score);
}
