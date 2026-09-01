using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed record TranscriptExportResult(string Path, int PageCount, string Fingerprint, string VerificationText);

public sealed class TranscriptPdfService(TranscriptVerificationService verification)
{
    public Task<TranscriptExportResult> ExportAsync(
        string path,
        AcademicSettings settings,
        StudentProfile? profile,
        IReadOnlyList<ScoreResult> results,
        bool allTerms = false)
    {
        return Task.Run(() => Export(path, settings, profile, results, allTerms));
    }

    private TranscriptExportResult Export(
        string path,
        AcademicSettings settings,
        StudentProfile? profile,
        IReadOnlyList<ScoreResult> results,
        bool allTerms)
    {
        if (results.Count == 0 || results.All(x => x.Records.Count == 0))
            throw new InvalidOperationException("没有可导出的成绩数据。");

        var signed = verification.CreateSignedQr(settings, profile, results, allTerms);
        var rows = results.OrderBy(x => x.Term)
            .SelectMany(result => result.Records.Select(score => new PdfScoreRow(result.Term, score)))
            .ToList();
        var pages = rows.Chunk(9).Select(x => x.ToList()).ToList();
        if (pages.Count == 0) pages.Add([]);
        var aggregate = ScorePolicy.Recalculate("all", rows.Select(x => x.Score), rows.Select(x => x.Score));

        using var logo = ResourceImage("Assets/UsSDAU-logo-square-v2.png");
        using var watermark = ResourceImage("Assets/UsSDAU-logo-watermark-v2.png");
        using var qrStream = new MemoryStream(signed.QrPng, writable: false);
        using var qr = XImage.FromStream(qrStream);
        using var document = new PdfDocument();
        document.Info.Title = allTerms || results.Count > 1 ? "UsSDAU 学生总成绩单" : $"UsSDAU {results[0].Term} 成绩单";
        document.Info.Author = "UsSDAU";
        document.Info.Subject = "本机生成的个人成绩记录";

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            DrawPage(
                gfx, page.Width.Point, page.Height.Point, logo, watermark, qr,
                settings, profile, results, pages[pageIndex], aggregate, allTerms,
                pageIndex + 1, pages.Count, pageIndex == pages.Count - 1, signed.Fingerprint);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        document.Save(path);
        return new TranscriptExportResult(path, pages.Count, signed.Fingerprint, signed.Text);
    }

    private static void DrawPage(
        XGraphics gfx,
        double width,
        double height,
        XImage logo,
        XImage watermark,
        XImage qr,
        AcademicSettings settings,
        StudentProfile? profile,
        IReadOnlyList<ScoreResult> results,
        IReadOnlyList<PdfScoreRow> rows,
        ScoreResult aggregate,
        bool allTerms,
        int pageNumber,
        int pageCount,
        bool finalPage,
        string fingerprint)
    {
        const double left = 44;
        var contentWidth = width - left * 2;
        var navy = XColor.FromArgb(20, 34, 55);
        var muted = XColor.FromArgb(96, 107, 122);
        var line = new XPen(XColor.FromArgb(62, 70, 82), .65);
        var simHei = new XFont("黑体", 9.2, XFontStyleEx.Bold);
        var simSun = new XFont("STSong", 9, XFontStyleEx.Regular);
        var simSunSmall = new XFont("STSong", 7.7, XFontStyleEx.Regular);
        var times = new XFont("Times New Roman", 8.8, XFontStyleEx.Regular);
        var timesBold = new XFont("Times New Roman", 9, XFontStyleEx.Bold);

        gfx.DrawImage(watermark, width / 2 - 112, 290, 224, 224);
        gfx.DrawImage(logo, left, 33, 44, 44);
        gfx.DrawString(allTerms || results.Count > 1 ? "山东农业大学学生总成绩单" : "山东农业大学学生成绩单",
            new XFont("黑体", 17.5, XFontStyleEx.Bold), new XSolidBrush(navy),
            new XRect(left + 55, 29, contentWidth - 110, 27), XStringFormats.TopCenter);
        gfx.DrawString("SHANDONG AGRICULTURAL UNIVERSITY - ACADEMIC TRANSCRIPT",
            new XFont("Times New Roman", 8.4, XFontStyleEx.Regular), new XSolidBrush(navy),
            new XRect(left + 55, 56, contentWidth - 110, 18), XStringFormats.TopCenter);
        gfx.DrawLine(new XPen(navy, 2.2), left, 87, width - left, 87);
        gfx.DrawLine(new XPen(navy, .7), left, 91, width - left, 91);

        var scope = allTerms || results.Count > 1 ? $"全部学期 ({results.Count})" : results[0].Term;
        DrawLabelValue(gfx, "姓名 / NAME", profile?.Name ?? "-", left, 105, 180, simHei, simSun);
        DrawLabelValue(gfx, "学号 / ID", settings.Account, left + 270, 105, 180, simHei, times);
        DrawLabelValue(gfx, "学期 / TERM", scope, left, 127, 180, simHei, simSun);
        DrawLabelValue(gfx, "制表日期 / DATE", DateTime.Today.ToString("yyyy-MM-dd"), left + 270, 127, 180, simHei, times);

        var columnWidths = new[] { 78d, contentWidth - 78 - 55 * 3, 55d, 55d, 55d };
        var y = 157d;
        DrawTableHeader(gfx, line, left, y, 30, columnWidths,
            ["课程代码", "课程名称", "学分", "成绩", "绩点"],
            ["COURSE CODE", "COURSE TITLE", "CREDIT", "GRADE", "GPA"], simHei, timesBold);
        y += 30;
        string? currentTerm = null;
        foreach (var row in rows)
        {
            if (currentTerm != row.Term)
            {
                currentTerm = row.Term;
                var termResult = results.First(x => x.Term == row.Term);
                var termSummary = $"{row.Term}    学期学分 {termResult.TotalCredits}    平均分 {termResult.AverageScore}    学期绩点 {termResult.AverageCreditGpa}";
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 242, 246)), left, y, contentWidth, 20);
                gfx.DrawRectangle(line, left, y, contentWidth, 20);
                gfx.DrawString(termSummary, simHei, new XSolidBrush(navy), new XRect(left + 8, y + 3, contentWidth - 16, 15), XStringFormats.TopLeft);
                y += 20;
            }
            DrawTableRow(gfx, line, left, y, 25, columnWidths,
                [row.Score.CourseCode, row.Score.CourseName, row.Score.Credit, row.Score.Score, row.Score.Gpa],
                [times, simSun, times, timesBold, times], false);
            y += 25;
        }

        if (finalPage)
        {
            var summaryY = Math.Max(y + 18, 520);
            DrawSummary(gfx, left, summaryY, contentWidth, results.Count, rows.Count + (pageCount - 1) * 9, aggregate, simHei, timesBold);
            gfx.DrawImage(qr, width - left - 104, 658, 104, 104);
            gfx.DrawString("离线完整性校验", simHei, new XSolidBrush(navy), new XRect(width - left - 110, 766, 110, 14), XStringFormats.TopCenter);
            gfx.DrawString("本机签名指纹", simSunSmall, new XSolidBrush(muted), new XRect(left, 697, 95, 12), XStringFormats.TopLeft);
            gfx.DrawString(FormatFingerprint(fingerprint), new XFont("Consolas", 6.5), new XSolidBrush(muted), new XRect(left, 711, 365, 32), XStringFormats.TopLeft);
            gfx.DrawString("本成绩单由辅助工具生成，不具有山东农业大学官方的效力。",
                simSunSmall, new XSolidBrush(muted), new XRect(left, 781, contentWidth, 15), XStringFormats.TopLeft);
        }

        gfx.DrawString($"第 {pageNumber} 页 / 共 {pageCount} 页", simSunSmall, new XSolidBrush(muted),
            new XRect(0, height - 26, width, 12), XStringFormats.TopCenter);
    }

    private static void DrawSummary(XGraphics gfx, double x, double y, double width, int termCount, int courseCount, ScoreResult result, XFont label, XFont value)
    {
        var values = new[] { ("学期数", termCount.ToString()), ("课程数", courseCount.ToString()), ("总学分", result.TotalCredits), ("累计绩点", result.AverageCreditGpa) };
        var cellWidth = width / values.Length;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 246, 248)), x, y, width, 54);
        gfx.DrawRectangle(new XPen(XColor.FromArgb(184, 191, 202), .8), x, y, width, 54);
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0) gfx.DrawLine(new XPen(XColor.FromArgb(204, 210, 218), .6), x + cellWidth * index, y + 8, x + cellWidth * index, y + 46);
            gfx.DrawString(values[index].Item1, label, XBrushes.Black, new XRect(x + cellWidth * index, y + 8, cellWidth, 13), XStringFormats.TopCenter);
            gfx.DrawString(values[index].Item2, value, XBrushes.Black, new XRect(x + cellWidth * index, y + 28, cellWidth, 15), XStringFormats.TopCenter);
        }
    }

    private static void DrawLabelValue(XGraphics gfx, string label, string value, double x, double y, double width, XFont labelFont, XFont valueFont)
    {
        gfx.DrawString(label, labelFont, XBrushes.Black, new XRect(x, y, 82, 16), XStringFormats.TopLeft);
        gfx.DrawString(value, valueFont, XBrushes.Black, new XRect(x + 86, y, width - 86, 16), XStringFormats.TopLeft);
    }

    private static void DrawTableRow(XGraphics gfx, XPen pen, double x, double y, double height, double[] widths, string[] values, XFont[] fonts, bool header)
    {
        var offset = x;
        for (var column = 0; column < widths.Length; column++)
        {
            if (header) gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 241, 244)), offset, y, widths[column], height);
            gfx.DrawRectangle(pen, offset, y, widths[column], height);
            var alignment = column == 1 ? XStringFormats.CenterLeft : XStringFormats.Center;
            var text = FitText(gfx, values[column], fonts[column], widths[column] - 10);
            gfx.DrawString(text, fonts[column], XBrushes.Black, new XRect(offset + 5, y, widths[column] - 10, height), alignment);
            offset += widths[column];
        }
    }

    private static void DrawTableHeader(XGraphics gfx, XPen pen, double x, double y, double height, double[] widths, string[] chinese, string[] english, XFont chineseFont, XFont englishFont)
    {
        var offset = x;
        for (var column = 0; column < widths.Length; column++)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 241, 244)), offset, y, widths[column], height);
            gfx.DrawRectangle(pen, offset, y, widths[column], height);
            gfx.DrawString(chinese[column], chineseFont, XBrushes.Black, new XRect(offset, y + 4, widths[column], 12), XStringFormats.TopCenter);
            gfx.DrawString(english[column], englishFont, XBrushes.Black, new XRect(offset, y + 16, widths[column], 10), XStringFormats.TopCenter);
            offset += widths[column];
        }
    }

    private static string FitText(XGraphics gfx, string value, XFont font, double width)
    {
        if (value.Contains('\n') || gfx.MeasureString(value, font).Width <= width) return value;
        var text = value;
        while (text.Length > 1 && gfx.MeasureString(text + "...", font).Width > width) text = text[..^1];
        return text + "...";
    }

    private static XImage ResourceImage(string path)
    {
        var info = System.Windows.Application.GetResourceStream(new Uri($"/UsSDAU;component/{path}", UriKind.Relative))
            ?? throw new InvalidOperationException($"无法读取资源 {path}");
        using var source = info.Stream;
        var bytes = new MemoryStream();
        source.CopyTo(bytes);
        bytes.Position = 0;
        return XImage.FromStream(bytes);
    }

    private static string FormatFingerprint(string value) => string.Join(" ", Enumerable.Range(0, value.Length / 8).Select(i => value.Substring(i * 8, 8)));
    private sealed record PdfScoreRow(string Term, ScoreRecord Score);
}
