using BarBrain.Api.Settings;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BarBrain.Api.Venues;

/// <summary>
/// The QR table-tent kit (Sprint 5 spec): a per-venue QR deep-linking to the
/// venue page, and a printable one-pager PDF the founder hands to owners
/// during onboarding (the Gate D "would you hand this over?" artifact).
///
/// Print collateral, not UI: monochrome ink on white, so the dark-only token
/// system (ADR-021) doesn't apply; copy is BRAND.md-bound (discovery framing,
/// no volume/consumption language). QuestPDF Community license — ADR-029.
/// </summary>
public sealed class VenueKitService(ISettingsService settings)
{
    public const string PublicBaseUrlFlag = "digest.public_base_url";

    static VenueKitService()
        => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<string> VenueUrlAsync(Guid venueId, CancellationToken ct = default)
    {
        var baseUrl = (await settings.GetStringAsync(PublicBaseUrlFlag, "https://dev.barbrain.co", ct))
            .TrimEnd('/');
        return $"{baseUrl}/venues/{venueId}";
    }

    /// <summary>QR PNG for the venue's deep link (pixelsPerModule ≈ print density).</summary>
    public byte[] QrPng(string url, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>The printable one-pager (US Letter, fold-ready table tent copy).</summary>
    public byte[] OnePagerPdf(string venueName, string url)
    {
        var qr = QrPng(url, pixelsPerModule: 20);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(48);
                page.DefaultTextStyle(t => t.FontSize(14).FontColor("#111111"));

                page.Content().Column(column =>
                {
                    column.Spacing(18);

                    column.Item().Text("BarBrain").FontSize(30).Bold();
                    column.Item().Text(venueName).FontSize(22).SemiBold();

                    column.Item().PaddingVertical(6).LineHorizontal(1).LineColor("#999999");

                    column.Item().Text("Your menu, sorted to every guest's taste.")
                        .FontSize(18).SemiBold();
                    column.Item().Text(
                        "Guests scan the code, check in, and see this menu arranged " +
                        "around what they already love — favorites first, familiar " +
                        "ground next, and a shelf of discoveries picked for their palate.");

                    column.Item().AlignCenter().PaddingVertical(10)
                        .Width(220).Image(qr);

                    column.Item().AlignCenter().Text("Scan to open this venue in BarBrain")
                        .FontSize(12).FontColor("#555555");
                    column.Item().AlignCenter().Text(url)
                        .FontSize(10).FontColor("#777777");

                    column.Item().PaddingTop(14).Text(
                        "BarBrain is free for guests. Ratings build each guest's flavor " +
                        "profile; your menu is where it pays off. 21+ only.")
                        .FontSize(11).FontColor("#555555");
                });
            });
        }).GeneratePdf();
    }
}
