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
        "MRP", "GST", "CGST", "SGST", "IGST", "HSN", "QTY", "PCS", "PACK",
        "TAB", "TABS", "CAP", "CAPS", "STRIP", "BOTTLE", "BOX"
    };

    private static readonly Regex UnitTokenRegex = new(
        @"^\d+(?:\.\d+)?(?:MG|MCG|G|KG|ML|L|TAB|TABS|CAP|CAPS|PCS|PACK|%|IU)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProductNoiseRegex = new(
        @"^(?:\d{4,8}|[A-Z]{1,3}\d{2,8}|\d{1,3}S|\d+(?:\.\d+)?(?:MG|MCG|G|KG|ML|L|TAB|TABS|CAP|CAPS|PCS|PACK|%)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ParsedInvoiceItem> Parse(IReadOnlyList<InvoiceOcrLineInput> inputLines)
    {
        var lines = NormalizeLines(inputLines);
        var candidates = new List<ParsedInvoiceItem>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (IsNoiseLine(lines[index].Text))
                continue;

            var candidate = ParseLine(lines[index]);

            if (candidate.Score < 0.50m &&
                index + 1 < lines.Count &&
                !IsNoiseLine(lines[index + 1].Text))
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

    public static IReadOnlyList<ParsedInvoiceItem> Parse(
        IReadOnlyList<InvoiceOcrLineInput> inputLines,
        IReadOnlyList<InvoiceOcrWordInput> inputWords)
    {
        var coordinateCandidates = ParseUsingCoordinates(inputWords);

        // Coordinate parsing is preferred for pharmacy tables. If OCR did not
        // return enough positional information, retain the original line parser
        // as a controlled fallback.
        if (coordinateCandidates.Count > 0)
            return coordinateCandidates;

        return Parse(inputLines);
    }

    private static IReadOnlyList<ParsedInvoiceItem> ParseUsingCoordinates(
        IReadOnlyList<InvoiceOcrWordInput> inputWords)
    {
        var words = inputWords
            .Where(word =>
                !string.IsNullOrWhiteSpace(word.Text) &&
                word.X1 > word.X0 &&
                word.Y1 > word.Y0 &&
                word.Page > 0)
            .Select(word => new OcrWord(
                CleanLine(word.Text),
                Math.Clamp(word.Confidence, 0m, 100m),
                word.X0,
                word.Y0,
                word.X1,
                word.Y1,
                word.Page))
            .Where(word => word.Text.Length >= 1)
            .ToList();

        var results = new List<ParsedInvoiceItem>();

        foreach (var pageGroup in words.GroupBy(word => word.Page).OrderBy(group => group.Key))
        {
            var rows = GroupWordsIntoRows(pageGroup.OrderBy(word => word.Y0).ToList());
            if (rows.Count == 0)
                continue;

            var header = FindTableHeader(rows);
            if (header is null)
                continue;

            var columns = DetectColumns(header.Words);
            if (!columns.HasDescription)
                continue;

            foreach (var row in rows.Where(row => row.CenterY > header.CenterY + Math.Max(8, header.Height * 0.5)))
            {
                var rowText = CleanLine(string.Join(" ", row.Words.OrderBy(word => word.X0).Select(word => word.Text)));

                if (IsTableStopRow(rowText))
                    break;

                if (row.Words.Count < 2)
                    continue;

                var expiryWords = SelectExpiryWords(row.Words, columns);
                var expiry = ExtractExpiry(
                    CleanLine(string.Join(" ", expiryWords.OrderBy(word => word.X0).Select(word => word.Text))));

                if (!expiry.Found)
                    continue;

                var productWords = SelectProductWords(row.Words, columns);
                var product = CleanProductWords(productWords);

                if (string.IsNullOrWhiteSpace(product))
                    continue;

                var quantity = ExtractColumnQuantity(row.Words, columns);
                var batch = ExtractColumnBatch(row.Words, columns);

                if (!quantity.Found)
                    quantity = ExtractQuantity(rowText, expiry.Position);

                if (!batch.Found)
                    batch = ExtractBatch(rowText, expiry.Position);

                if (!quantity.Found || !batch.Found)
                    continue;

                var rowConfidence = row.Words.Count == 0
                    ? 0m
                    : row.Words.Average(word => word.Confidence);

                var fieldConfidence = new[]
                {
                    Math.Clamp(rowConfidence, 0m, 100m),
                    batch.Explicit ? 96m : 82m,
                    expiry.Explicit ? 96m : 88m,
                    quantity.Explicit ? 95m : 82m
                }.Average();

                results.Add(new ParsedInvoiceItem(
                    row.LineNumber,
                    rowText,
                    product,
                    batch.Value,
                    expiry.Value,
                    quantity.Value,
                    Math.Clamp(fieldConfidence, 0m, 100m),
                    1.00m));
            }
        }

        return MergeDuplicates(results);
    }

    private static List<OcrRow> GroupWordsIntoRows(IReadOnlyList<OcrWord> words)
    {
        var rows = new List<OcrRow>();
        var medianHeight = Median(words.Select(word => word.Height));
        var tolerance = Math.Clamp(medianHeight * 0.75, 8, 22);

        foreach (var word in words.OrderBy(word => word.CenterY).ThenBy(word => word.X0))
        {
            var row = rows
                .Where(candidate => Math.Abs(candidate.CenterY - word.CenterY) <= tolerance)
                .OrderBy(candidate => Math.Abs(candidate.CenterY - word.CenterY))
                .FirstOrDefault();

            if (row is null)
            {
                row = new OcrRow(rows.Count + 1);
                rows.Add(row);
            }

            row.Words.Add(word);
            row.Recalculate();
        }

        return rows
            .Where(row => row.Words.Count > 0)
            .OrderBy(row => row.CenterY)
            .ToList();
    }

    private static OcrRow? FindTableHeader(IReadOnlyList<OcrRow> rows)
    {
        OcrRow? best = null;
        var bestScore = 0;

        foreach (var row in rows)
        {
            var text = NormalizeKey(string.Join(" ", row.Words.Select(word => word.Text)));
            var score = 0;

            if (ContainsAny(text, "DESCRIPTION", "DESCRIPTION/ITEM", "ITEM DESCRIPTION", "PRODUCT"))
                score += 2;

            if (ContainsAny(text, "QTY", "QUANTITY", "QNTY"))
                score += 2;

            if (ContainsAny(text, "BATCH", "BATCH NO", "LOT"))
                score += 2;

            if (ContainsAny(text, "EXP", "EXPIRY", "EXP DATE", "E.D"))
                score += 2;

            if (ContainsAny(text, "MRP", "PACK"))
                score += 1;

            if (score > bestScore)
            {
                best = row;
                bestScore = score;
            }
        }

        return bestScore >= 4 ? best : null;
    }

    private static ColumnMap DetectColumns(IReadOnlyList<OcrWord> words)
    {
        var description = FindHeaderX(words, "DESCRIPTION", "ITEM DESCRIPTION", "PRODUCT", "DESCRIPTION/ITEM");
        var quantity = FindHeaderX(words, "QTY", "QUANTITY", "QNTY");
        var batch = FindHeaderX(words, "BATCH", "BATCH NO", "LOT");
        var expiry = FindHeaderX(words, "EXP", "EXPIRY", "EXP DATE", "E.D");

        return new ColumnMap(description, quantity, batch, expiry);
    }

    private static int? FindHeaderX(IReadOnlyList<OcrWord> words, params string[] labels)
    {
        foreach (var word in words)
        {
            var normalized = NormalizeKey(word.Text);

            if (labels.Any(label =>
                    normalized.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(label, StringComparison.OrdinalIgnoreCase)))
            {
                return word.CenterX;
            }
        }

        return null;
    }

    private static IReadOnlyList<OcrWord> SelectProductWords(
        IReadOnlyList<OcrWord> row,
        ColumnMap columns)
    {
        var start = columns.DescriptionX ?? row.Min(word => word.X0);
        var end = columns.QuantityX ?? columns.BatchX ?? row.Max(word => word.X1);

        return row
            .Where(word => word.CenterX >= start - 12 && word.CenterX < end - 8)
            .Where(word => !IsProductNoiseToken(word.Text))
            .OrderBy(word => word.X0)
            .ToList();
    }

    private static IReadOnlyList<OcrWord> SelectExpiryWords(
        IReadOnlyList<OcrWord> row,
        ColumnMap columns)
    {
        if (columns.ExpiryX is null)
            return row;

        var center = columns.ExpiryX.Value;
        var left = columns.BatchX.HasValue
            ? (columns.BatchX.Value + center) / 2
            : center - 70;

        var right = columns.MrpX ?? center + 80;

        return row
            .Where(word => word.CenterX >= left - 15 && word.CenterX <= right + 15)
            .OrderBy(word => word.X0)
            .ToList();
    }

    private static QuantityResult ExtractColumnQuantity(
        IReadOnlyList<OcrWord> row,
        ColumnMap columns)
    {
        if (columns.QuantityX is null)
            return new QuantityResult(null, false);

        var center = columns.QuantityX.Value;
        var left = columns.DescriptionX.HasValue
            ? (columns.DescriptionX.Value + center) / 2
            : center - 50;

        var right = columns.BatchX.HasValue
            ? (center + columns.BatchX.Value) / 2
            : center + 50;

        var numericWords = row
            .Where(word => word.CenterX >= left && word.CenterX <= right)
            .Select(word => word.Text.Replace(",", string.Empty).Trim())
            .Select(text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : (decimal?)null)
            .Where(value => value is > 0 and <= 1_000_000m)
            .ToList();

        return numericWords.Count > 0
            ? new QuantityResult(numericWords[0], true)
            : new QuantityResult(null, false);
    }

    private static BatchResult ExtractColumnBatch(
        IReadOnlyList<OcrWord> row,
        ColumnMap columns)
    {
        if (columns.BatchX is null)
            return new BatchResult(string.Empty, false);

        var center = columns.BatchX.Value;
        var left = columns.QuantityX.HasValue
            ? (columns.QuantityX.Value + center) / 2
            : center - 70;

        var right = columns.ExpiryX.HasValue
            ? (center + columns.ExpiryX.Value) / 2
            : center + 70;

        var candidates = row
            .Where(word => word.CenterX >= left - 12 && word.CenterX <= right + 12)
            .Select(word => word.Text.Trim())
            .Where(IsLikelyBatch)
            .OrderByDescending(value => value.Any(char.IsLetter) && value.Any(char.IsDigit))
            .ThenByDescending(value => value.Length)
            .ToList();

        return candidates.Count > 0
            ? new BatchResult(candidates[0], true)
            : new BatchResult(string.Empty, false);
    }

    private static bool IsLikelyBatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length is < 3 or > 24 ||
            ExcludedBatchWords.Contains(value) ||
            UnitTokenRegex.IsMatch(value))
            return false;

        var hasLetter = value.Any(char.IsLetter);
        var hasDigit = value.Any(char.IsDigit);

        return hasLetter && hasDigit;
    }

    private static string CleanProductWords(IReadOnlyList<OcrWord> words)
    {
        var product = string.Join(" ", words.Select(word => word.Text));
        product = LeadingRowNumberRegex.Replace(product, string.Empty);
        product = Regex.Replace(product, @"\s{2,}", " ");
        product = Regex.Replace(product, @"^[^A-Za-z0-9]+|[^A-Za-z0-9)%+/-]+$", string.Empty);
        product = product.Trim();

        if (product.Length is < 2 or > 200 || IsNoiseLine(product))
            return string.Empty;

        return product;
    }

    private static bool IsProductNoiseToken(string value)
    {
        var token = value.Trim();

        // Keep dosage/form words such as TAB, CAP, MG and ML because they are
        // part of a pharmacy product name. Only remove obvious numeric codes.
        return ProductNoiseRegex.IsMatch(token);
    }

    private static bool IsTableStopRow(string text)
    {
        var normalized = NormalizeKey(text);

        return ContainsAny(
            normalized,
            "GRAND TOTAL",
            "TOTAL AMOUNT",
            "TOTAL AMT",
            "TOTAL ITEMS",
            "TOTAL QTY",
            "PENDING BILLS",
            "NO RETURNS",
            "NO OF PENDING",
            "BANK DETAILS");
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static double Median(IEnumerable<int> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return 12;

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static List<NormalizedLine> NormalizeLines(IReadOnlyList<InvoiceOcrLineInput> inputLines) =>
        inputLines
            .Select((line, index) => new NormalizedLine(
                Math.Max(0, index + 1),
                CleanLine(line.Text),
                Math.Clamp(line.Confidence, 0m, 100m)))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Where(line => line.Text.Length >= 3)
            .ToList();

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

        var score =
            0.25m +
            (batch.Found ? 0.25m : 0m) +
            (expiry.Found ? 0.25m : 0m) +
            (quantity.Found ? 0.25m : 0m);

        var fieldAverage = new[] { productConfidence, batchConfidence, expiryConfidence, quantityConfidence }
            .Where(value => value > 0m)
            .DefaultIfEmpty(0m)
            .Average();

        var confidence = Math.Clamp(
            ((fieldAverage * 0.80m) + ((line.Confidence / 100m) * 0.20m)) * 100m,
            0m,
            100m);

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

        if (product.Length is < 2 or > 200 || IsNoiseLine(product))
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

            if (!IsLikelyBatch(token))
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

                if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ||
                    value <= 0m ||
                    value > 1_000_000m)
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
            out date);
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

        var month = DateTime.ParseExact(
            monthValue[..3],
            "MMM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces).Month;

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
                Quantity = totalQuantity > 0m ? totalQuantity : existing.Quantity ?? item.Quantity,
                Confidence = Math.Max(existing.Confidence, item.Confidence),
                Score = Math.Max(existing.Score, item.Score)
            };
        }

        return merged;
    }

    private static string NormalizeKey(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", " ");

    private sealed record NormalizedLine(int LineNumber, string Text, decimal Confidence);

    private sealed class OcrRow
    {
        public OcrRow(int lineNumber) => LineNumber = lineNumber;

        public int LineNumber { get; }
        public List<OcrWord> Words { get; } = [];
        public double CenterY { get; private set; }
        public int Height { get; private set; }

        public void Recalculate()
        {
            CenterY = Words.Average(word => word.CenterY);
            Height = Words.Count == 0 ? 0 : Words.Max(word => word.Height);
        }
    }

    private sealed record OcrWord(
        string Text,
        decimal Confidence,
        int X0,
        int Y0,
        int X1,
        int Y1,
        int Page)
    {
        public int CenterX => (X0 + X1) / 2;
        public double CenterY => (Y0 + Y1) / 2d;
        public int Height => Math.Max(1, Y1 - Y0);
    }

    private sealed record ColumnMap(
        int? DescriptionX,
        int? QuantityX,
        int? BatchX,
        int? ExpiryX)
    {
        public int? MrpX => null;
        public bool HasDescription => DescriptionX.HasValue;
    }

    private sealed record BatchResult(string Value, bool Explicit)
    {
        public bool Found => !string.IsNullOrWhiteSpace(Value);
    }

    private sealed record QuantityResult(decimal? Value, bool Explicit)
    {
        public bool Found => Value.HasValue && Value.Value > 0;
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
