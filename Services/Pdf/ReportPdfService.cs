using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HardwareManagementSystem.Services.Pdf
{
    public class ReportPdfService
    {
        public byte[] GenerateSimpleReportPdf(
            string reportTitle,
            string dateRange,
            List<string> headers,
            List<List<string>> rows)
        {
            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("HardBuild POS")
                            .FontSize(18)
                            .Bold();

                        col.Item().Text(reportTitle)
                            .FontSize(14)
                            .SemiBold();

                        col.Item().Text(dateRange)
                            .FontSize(9);
                    });

                    page.Content().PaddingVertical(15).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var header in headers)
                            {
                                columns.RelativeColumn();
                            }
                        });

                        table.Header(header =>
                        {
                            foreach (var h in headers)
                            {
                                header.Cell()
                                    .Background(Colors.Grey.Lighten2)
                                    .Padding(5)
                                    .Text(h)
                                    .Bold();
                            }
                        });

                        foreach (var row in rows)
                        {
                            foreach (var cell in row)
                            {
                                table.Cell()
                                    .BorderBottom(1)
                                    .BorderColor(Colors.Grey.Lighten3)
                                    .Padding(5)
                                    .Text(cell);
                            }
                        }
                    });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Generated ");
                            x.Span(DateTime.Now.ToString("MMM dd, yyyy hh:mm tt"));
                        });
                });
            });

            return pdf.GeneratePdf();
        }
    }
}