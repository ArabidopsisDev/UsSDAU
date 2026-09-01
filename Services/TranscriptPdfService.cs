using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed record TranscriptExportResult(string Path, int PageCount, string Fingerprint, string VerificationText);

public sealed class TranscriptPdfService(TranscriptVerificationService verification)
{
    private const int RowsPerPage = 17;

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
        var available = results.Where(x => x.Records.Count > 0).OrderBy(x => x.Term).ToList();
        if (available.Count == 0) throw new InvalidOperationException("没有可导出的成绩数据。");

        var totalTranscript = allTerms || available.Count > 1;
        var signed = verification.CreateSignedQr(settings, profile, available, totalTranscript);
        var allRecords = available.SelectMany(x => x.Records).ToList();
        var aggregate = ScorePolicy.Recalculate("all", allRecords, allRecords);
        var termPages = BuildTermPages(available);

        using var logo = ResourceImage("Assets/UsSDAU-logo-square-v2.png");
        using var watermark = ResourceImage("Assets/UsSDAU-logo-watermark-v2.png");
        using var qrStream = new MemoryStream(signed.QrPng, writable: false);
        using var qr = XImage.FromStream(qrStream);
        using var document = new PdfDocument();
        document.Info.Title = totalTranscript ? "UsSDAU 学生成绩册" : $"UsSDAU {available[0].Term} 成绩册";
        document.Info.Author = "UsSDAU";
        document.Info.Subject = "本机生成的个人学业记录";

        var cover = document.AddPage();
        cover.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(cover))
            DrawCover(gfx, cover.Width.Point, cover.Height.Point, logo, watermark, qr, settings, profile, available, aggregate, signed.Fingerprint);

        for (var index = 0; index < termPages.Count; index++)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            DrawTermPage(
                gfx, page.Width.Point, page.Height.Point, logo, watermark, settings, profile,
                termPages[index], index + 1, termPages.Count, index == termPages.Count - 1);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        var exportedPageCount = document.PageCount;
        document.Save(path);
        return new TranscriptExportResult(path, exportedPageCount, signed.Fingerprint, signed.Text);
    }

    private static List<TermPage> BuildTermPages(IReadOnlyList<ScoreResult> results)
    {
        var pages = new List<TermPage>();
        foreach (var result in results.OrderBy(x => x.Term))
        {
            for (var offset = 0; offset < result.Records.Count; offset += RowsPerPage)
            {
                var rows = result.Records.Skip(offset).Take(RowsPerPage).ToList();
                pages.Add(new TermPage(
                    result,
                    rows,
                    Continuation: offset > 0,
                    FinalForTerm: offset + rows.Count >= result.Records.Count));
            }
        }
        return pages;
    }

    private static void DrawCover(
        XGraphics gfx,
        double width,
        double height,
        XImage logo,
        XImage watermark,
        XImage qr,
        AcademicSettings settings,
        StudentProfile? profile,
        IReadOnlyList<ScoreResult> results,
        ScoreResult aggregate,
        string fingerprint)
    {
        const double margin = 48;
        var contentWidth = width - margin * 2;
        var navy = XColor.FromArgb(20, 34, 55);
        var muted = XColor.FromArgb(91, 103, 120);
        var simHei = new XFont("黑体", 10, XFontStyleEx.Bold);
        var simSun = new XFont("STSong", 10, XFontStyleEx.Regular);
        var times = new XFont("Times New Roman", 9.5, XFontStyleEx.Regular);
        var timesBold = new XFont("Times New Roman", 10, XFontStyleEx.Bold);

        gfx.DrawRectangle(new XPen(navy, 1.15), margin - 12, 36, contentWidth + 24, height - 72);
        gfx.DrawRectangle(new XPen(XColor.FromArgb(116, 128, 145), .45), margin - 7, 41, contentWidth + 14, height - 82);
        gfx.DrawImage(watermark, width / 2 - 150, 288, 300, 300);
        gfx.DrawImage(logo, width / 2 - 42, 78, 84, 84);
        gfx.DrawString("山东农业大学学生成绩册", new XFont("黑体", 24, XFontStyleEx.Bold), new XSolidBrush(navy), new XRect(margin, 184, contentWidth, 36), XStringFormats.TopCenter);
        gfx.DrawString("SHANDONG AGRICULTURAL UNIVERSITY", new XFont("Times New Roman", 11, XFontStyleEx.Bold), new XSolidBrush(navy), new XRect(margin, 225, contentWidth, 16), XStringFormats.TopCenter);
        gfx.DrawString("STUDENT ACADEMIC RECORD", new XFont("Times New Roman", 10, XFontStyleEx.Regular), new XSolidBrush(muted), new XRect(margin, 245, contentWidth, 15), XStringFormats.TopCenter);
        gfx.DrawLine(new XPen(navy, 1.8), margin + 90, 278, width - margin - 90, 278);

        var infoY = 315d;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(247, 248, 250)), margin + 34, infoY, contentWidth - 68, 118);
        gfx.DrawRectangle(new XPen(XColor.FromArgb(190, 197, 207), .7), margin + 34, infoY, contentWidth - 68, 118);
        DrawCoverInfo(gfx, "姓名 / NAME", profile?.Name ?? "-", margin + 58, infoY + 22, 190, simHei, simSun);
        DrawCoverInfo(gfx, "学号 / ID", settings.Account, margin + 288, infoY + 22, 170, simHei, times);
        DrawCoverInfo(gfx, "学业范围 / PERIOD", $"{results.First().Term} - {results.Last().Term}", margin + 58, infoY + 69, 260, simHei, times);
        DrawCoverInfo(gfx, "制表日期 / DATE", DateTime.Today.ToString("yyyy-MM-dd"), margin + 338, infoY + 69, 120, simHei, times);

        DrawSummary(gfx, margin + 34, 474, contentWidth - 68, results.Count, aggregate.Records.Count, aggregate, simHei, timesBold);

        gfx.DrawString("离线完整性校验", simHei, new XSolidBrush(navy), new XRect(margin + 58, 600, 165, 16), XStringFormats.TopLeft);
        gfx.DrawString("扫描二维码后可在 UsSDAU 中离线验证本机签名和数据摘要。", simSun, new XSolidBrush(muted), new XRect(margin + 58, 624, 285, 35), XStringFormats.TopLeft);
        gfx.DrawString("本机签名公钥指纹", new XFont("STSong", 8.2), new XSolidBrush(muted), new XRect(margin + 58, 681, 130, 14), XStringFormats.TopLeft);
        gfx.DrawString(FormatFingerprint(fingerprint), new XFont("Consolas", 6.4), new XSolidBrush(muted), new XRect(margin + 58, 699, 285, 32), XStringFormats.TopLeft);
        gfx.DrawImage(qr, width - margin - 126, 594, 112, 112);
        gfx.DrawString("USSD1 / ES256", timesBold, new XSolidBrush(navy), new XRect(width - margin - 136, 713, 132, 15), XStringFormats.TopCenter);
        gfx.DrawString("UsSDAU", new XFont("Times New Roman", 15, XFontStyleEx.Bold), new XSolidBrush(navy), new XRect(margin, 757, contentWidth, 22), XStringFormats.TopCenter);
        gfx.DrawString("ACADEMIC DESKTOP", new XFont("Times New Roman", 8, XFontStyleEx.Regular), new XSolidBrush(muted), new XRect(margin, 781, contentWidth, 12), XStringFormats.TopCenter);
    }

    private static void DrawTermPage(
        XGraphics gfx,
        double width,
        double height,
        XImage logo,
        XImage watermark,
        AcademicSettings settings,
        StudentProfile? profile,
        TermPage content,
        int pageNumber,
        int pageCount,
        bool finalDocumentPage)
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

        gfx.DrawImage(watermark, width / 2 - 112, 315, 224, 224);
        gfx.DrawImage(logo, left, 31, 42, 42);
        gfx.DrawString("山东农业大学学生成绩册", new XFont("黑体", 17, XFontStyleEx.Bold), new XSolidBrush(navy), new XRect(left + 55, 28, contentWidth - 110, 27), XStringFormats.TopCenter);
        gfx.DrawString("STUDENT ACADEMIC RECORD", new XFont("Times New Roman", 8.5), new XSolidBrush(navy), new XRect(left + 55, 55, contentWidth - 110, 14), XStringFormats.TopCenter);
        gfx.DrawLine(new XPen(navy, 2.1), left, 84, width - left, 84);
        gfx.DrawLine(new XPen(navy, .7), left, 88, width - left, 88);

        DrawLabelValue(gfx, "姓名 / NAME", profile?.Name ?? "-", left, 102, 180, simHei, simSun);
        DrawLabelValue(gfx, "学号 / ID", settings.Account, left + 270, 102, 180, simHei, times);
        DrawLabelValue(gfx, "学期 / TERM", content.Result.Term, left, 124, 180, simHei, times);
        DrawLabelValue(gfx, "页内状态 / STATUS", content.Continuation ? "续页" : "本学期首页", left + 270, 124, 180, simHei, simSun);

        var y = 154d;
        var termLabel = content.Continuation ? $"{content.Result.Term}（续）" : content.Result.Term;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(238, 242, 247)), left, y, contentWidth, 24);
        gfx.DrawRectangle(line, left, y, contentWidth, 24);
        gfx.DrawString(termLabel, simHei, new XSolidBrush(navy), new XRect(left + 9, y + 5, contentWidth - 18, 14), XStringFormats.TopLeft);
        y += 24;

        var columnWidths = new[] { 78d, contentWidth - 78 - 55 * 3, 55d, 55d, 55d };
        DrawTableHeader(gfx, line, left, y, 30, columnWidths,
            ["课程代码", "课程名称", "学分", "成绩", "绩点"],
            ["COURSE CODE", "COURSE TITLE", "CREDIT", "GRADE", "GPA"], simHei, timesBold);
        y += 30;
        foreach (var score in content.Rows)
        {
            DrawTableRow(gfx, line, left, y, 27, columnWidths,
                [score.CourseCode, score.CourseName, score.Credit, score.Score, score.Gpa],
                [times, simSun, times, timesBold, times]);
            y += 27;
        }

        if (content.FinalForTerm)
        {
            var summaryY = y + 12;
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(246, 247, 249)), left, summaryY, contentWidth, 42);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(184, 191, 202), .75), left, summaryY, contentWidth, 42);
            gfx.DrawString("学期小计", simHei, new XSolidBrush(navy), new XRect(left + 12, summaryY + 7, 74, 14), XStringFormats.TopLeft);
            var summaryItems = new[]
            {
                ("课程数", content.Result.Records.Count.ToString()),
                ("学分", content.Result.TotalCredits),
                ("平均分", content.Result.AverageScore),
                ("学期绩点", content.Result.AverageCreditGpa)
            };
            var itemWidth = (contentWidth - 92) / summaryItems.Length;
            for (var index = 0; index < summaryItems.Length; index++)
            {
                var itemX = left + 88 + itemWidth * index;
                gfx.DrawString(summaryItems[index].Item1, simHei, new XSolidBrush(navy), new XRect(itemX, summaryY + 5, itemWidth, 13), XStringFormats.TopCenter);
                gfx.DrawString(summaryItems[index].Item2, timesBold, new XSolidBrush(navy), new XRect(itemX, summaryY + 22, itemWidth, 13), XStringFormats.TopCenter);
            }
        }

        if (finalDocumentPage)
        {
            gfx.DrawString("本成绩单由辅助工具生成，不具有山东农业大学官方的效力。",
                simSunSmall, new XSolidBrush(muted), new XRect(left, 785, contentWidth, 14), XStringFormats.TopLeft);
        }
        gfx.DrawString($"第 {pageNumber} 页 / 共 {pageCount} 页", simSunSmall, new XSolidBrush(muted), new XRect(0, height - 26, width, 12), XStringFormats.TopCenter);
    }

    private static void DrawCoverInfo(XGraphics gfx, string label, string value, double x, double y, double width, XFont labelFont, XFont valueFont)
    {
        gfx.DrawString(label, labelFont, XBrushes.Black, new XRect(x, y, 104, 16), XStringFormats.TopLeft);
        gfx.DrawString(value, valueFont, XBrushes.Black, new XRect(x + 108, y, width - 108, 18), XStringFormats.TopLeft);
    }

    private static void DrawSummary(XGraphics gfx, double x, double y, double width, int termCount, int courseCount, ScoreResult result, XFont label, XFont value)
    {
        var values = new[] { ("学期数", termCount.ToString()), ("课程数", courseCount.ToString()), ("总学分", result.TotalCredits), ("累计绩点", result.AverageCreditGpa) };
        var cellWidth = width / values.Length;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 246, 248)), x, y, width, 58);
        gfx.DrawRectangle(new XPen(XColor.FromArgb(184, 191, 202), .8), x, y, width, 58);
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0) gfx.DrawLine(new XPen(XColor.FromArgb(204, 210, 218), .6), x + cellWidth * index, y + 9, x + cellWidth * index, y + 49);
            gfx.DrawString(values[index].Item1, label, XBrushes.Black, new XRect(x + cellWidth * index, y + 9, cellWidth, 13), XStringFormats.TopCenter);
            gfx.DrawString(values[index].Item2, value, XBrushes.Black, new XRect(x + cellWidth * index, y + 31, cellWidth, 16), XStringFormats.TopCenter);
        }
    }

    private static void DrawLabelValue(XGraphics gfx, string label, string value, double x, double y, double width, XFont labelFont, XFont valueFont)
    {
        gfx.DrawString(label, labelFont, XBrushes.Black, new XRect(x, y, 82, 16), XStringFormats.TopLeft);
        gfx.DrawString(value, valueFont, XBrushes.Black, new XRect(x + 86, y, width - 86, 16), XStringFormats.TopLeft);
    }

    private static void DrawTableRow(XGraphics gfx, XPen pen, double x, double y, double height, double[] widths, string[] values, XFont[] fonts)
    {
        var offset = x;
        for (var column = 0; column < widths.Length; column++)
        {
            gfx.DrawRectangle(pen, offset, y, widths[column], height);
            var alignment = column == 1 ? XStringFormats.CenterLeft : XStringFormats.Center;
            gfx.DrawString(FitText(gfx, values[column], fonts[column], widths[column] - 10), fonts[column], XBrushes.Black, new XRect(offset + 5, y, widths[column] - 10, height), alignment);
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
        if (gfx.MeasureString(value, font).Width <= width) return value;
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
    private sealed record TermPage(ScoreResult Result, IReadOnlyList<ScoreRecord> Rows, bool Continuation, bool FinalForTerm);
}
