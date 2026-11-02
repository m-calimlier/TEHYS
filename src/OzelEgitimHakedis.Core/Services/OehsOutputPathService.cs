using OzelEgitimHakedis.Core.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OzelEgitimHakedis.Core.Services;

public static class OehsOutputPathService
{
    public static string BuildPaymentDocumentsPdfFilePath(string root, ProgressPaymentHistoryData payment) =>
        BuildOfficialPdfFilePath(root, payment, "Ödemeye Ait Evraklar")
            .Replace("_Ödemeye Ait Evraklar.pdf", "-Ödemeye Ait Evraklar.pdf", StringComparison.Ordinal);

    // Four-document exports must fit Windows paths even for long cooperative and batch names.
    // Identity is kept in a stable folder key instead of repeating names in every directory.
    public static string BuildOfficialPdfDirectory(string root, ProgressPaymentHistoryData payment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(payment);
        var identity = JsonSerializer.Serialize(new[]
        {
            OehsIdentityService.NormalizeIdentity(payment.TenderNumber),
            OehsIdentityService.NormalizeIdentity(payment.ContractorTaxOrIdentityNumber),
            string.IsNullOrWhiteSpace(payment.ContractorTaxOrIdentityNumber) ? payment.ContractorName?.Trim() : "",
            payment.ContractNumber, payment.BatchId.HasValue ? $"Batch:{payment.BatchId}" : payment.RouteNumber, payment.BatchRouteNumbers,
            payment.BatchId.HasValue ? null : payment.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture)
        });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        var program = payment.Input.ProgramType == TransportationProgramType.SpecialEducation ? "OE" : "TE";
        var period = payment.PeriodDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "Donemsiz";
        return Path.Combine(root, "Ciktilar", "Hakedisler", $"{program}_{period}_{key}");
    }

    public static string BuildOfficialPdfFilePath(string root, ProgressPaymentHistoryData payment, string suffix)
    {
        var directory = BuildOfficialPdfDirectory(root, payment);
        var tender = OehsFileNameService.SanitizeFileName(payment.TenderNumber?.Replace('/', '-'), "Ihale");
        var contractor = OehsFileNameService.SanitizeFileName(payment.ContractorName, "Yuklenici", 180);
        var name = tender + "-" + contractor;
        var ending = "_" + OehsFileNameService.SanitizeFileName(suffix, "Belge") + ".pdf";
        var available = 240 - Path.GetFullPath(directory).Length - 1 - ending.Length;
        if (available < 20)
            throw new PathTooLongException("PDF kayıt klasörü çok uzun. Daha kısa bir çıktı kökü seçin.");
        if (name.Length > available)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..10];
            name = name[..(available - 11)].TrimEnd('.', ' ') + "-" + hash;
        }
        return Path.Combine(directory, name + ending);
    }

    public static string DesktopRoot => OehsRuntimePaths.DesktopRoot;

    public static string PdfRoot =>
        Path.Combine(DesktopRoot, "Ciktilar", "Hakedisler");

    public static string BuildPdfDirectory(string tenderNumber, string routeNumber) =>
        BuildPdfDirectory(DesktopRoot, tenderNumber, routeNumber);

    public static string BuildPdfDirectory(string root, string tenderNumber, string routeNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var tender = OehsFileNameService.SanitizeFileName(tenderNumber, "Ihale");
        var route = OehsFileNameService.SanitizeFileName(routeNumber, "Hat");
        return Path.Combine(root, "Ciktilar", "Hakedisler", tender, $"Hat-{route}");
    }

    public static string BuildPdfDirectoryForContract(string tenderNumber, string contractNumber, string routeNumber) =>
        BuildPdfDirectoryForContract(DesktopRoot, tenderNumber, contractNumber, routeNumber);

    public static string BuildPdfDirectoryForContract(string root, string tenderNumber, string contractNumber, string routeNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // Preserve the pre-contract-part directory convention for old/ambiguous records
        // whose contract number could not be resolved safely during migration.
        if (string.IsNullOrWhiteSpace(contractNumber))
            return BuildPdfDirectory(root, tenderNumber, routeNumber);

        var tender = OehsFileNameService.SanitizeFileName(tenderNumber, "Ihale");
        var route = OehsFileNameService.SanitizeFileName(routeNumber, "Hat");
        var contract = OehsFileNameService.SanitizeFileName(contractNumber, "Sozlesme");

        return Path.Combine(root, "Ciktilar", "Hakedisler", tender, $"Sozlesme-{contract}", $"Hat-{route}");
    }

    public static string BuildPdfDirectoryForContractor(
        string tenderNumber,
        string contractorName,
        string contractorTaxOrIdentityNumber,
        string routeNumber,
        TransportationProgramType? programType = null,
        string? batchRouteNumbers = null,
        DateOnly? periodDate = null) =>
        BuildPdfDirectoryForContractor(
            DesktopRoot, tenderNumber, contractorName, contractorTaxOrIdentityNumber, routeNumber,
            programType, batchRouteNumbers, periodDate);

    public static string BuildPdfDirectoryForContractor(
        string root,
        string tenderNumber,
        string contractorName,
        string contractorTaxOrIdentityNumber,
        string routeNumber,
        TransportationProgramType? programType = null,
        string? batchRouteNumbers = null,
        DateOnly? periodDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var tender = OehsFileNameService.SanitizeFileName(tenderNumber, "Ihale");
        var contractor = OehsFileNameService.BuildContractorFilePart(contractorName, contractorTaxOrIdentityNumber);
        var route = OehsFileNameService.SanitizeFileName(routeNumber, "Hat");
        var pathParts = new List<string> { root, "Ciktilar", "Hakedisler" };
        if (programType.HasValue)
            pathParts.Add(OehsFileNameService.BuildProgramFilePart(programType.Value));
        pathParts.Add(tender);
        pathParts.Add($"Yuklenici-{contractor}");
        if (!string.IsNullOrWhiteSpace(batchRouteNumbers) && periodDate.HasValue)
        {
            var batchRoutes = OehsFileNameService.SanitizeFileName(batchRouteNumbers, "Hatlar");
            pathParts.Add($"Aylik-{batchRoutes}_{periodDate.Value:yyyy-MM}");
        }
        pathParts.Add($"Hat-{route}");
        return Path.Combine(pathParts.ToArray());
    }

    public static string BuildContractorPdfFilePath(
        string tenderNumber,
        string contractorName,
        string contractorTaxOrIdentityNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate,
        TransportationProgramType? programType = null,
        string? batchRouteNumbers = null) =>
        BuildContractorPdfFilePath(
            DesktopRoot, tenderNumber, contractorName, contractorTaxOrIdentityNumber, routeNumber,
            progressPaymentNumber, periodDate, programType, batchRouteNumbers);

    public static string BuildContractorPdfFilePath(
        string root,
        string tenderNumber,
        string contractorName,
        string contractorTaxOrIdentityNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate,
        TransportationProgramType? programType = null,
        string? batchRouteNumbers = null)
    {
        var directory = BuildPdfDirectoryForContractor(
            root, tenderNumber, contractorName, contractorTaxOrIdentityNumber, routeNumber,
            programType, batchRouteNumbers, periodDate);
        var fileName = OehsFileNameService.BuildProgressPdfFileNameForContractor(
            tenderNumber, contractorName, contractorTaxOrIdentityNumber, routeNumber,
            progressPaymentNumber, periodDate, programType, batchRouteNumbers);
        return Path.Combine(directory, fileName);
    }

    public static string BuildPdfFilePath(
        string tenderNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate) =>
        BuildPdfFilePath(DesktopRoot, tenderNumber, routeNumber, progressPaymentNumber, periodDate);

    public static string BuildPdfFilePath(
        string root,
        string tenderNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate)
    {
        var directory = BuildPdfDirectory(root, tenderNumber, routeNumber);
        var fileName = OehsFileNameService.BuildProgressPdfFileName(
            tenderNumber, routeNumber, progressPaymentNumber, periodDate);
        return Path.Combine(directory, fileName);
    }

    public static string BuildContractPdfFilePath(
        string tenderNumber,
        string contractNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate) =>
        BuildContractPdfFilePath(DesktopRoot, tenderNumber, contractNumber, routeNumber, progressPaymentNumber, periodDate);

    public static string BuildContractPdfFilePath(
        string root,
        string tenderNumber,
        string contractNumber,
        string routeNumber,
        int progressPaymentNumber,
        DateOnly periodDate)
    {
        var directory = BuildPdfDirectoryForContract(root, tenderNumber, contractNumber, routeNumber);
        var fileName = OehsFileNameService.BuildProgressPdfFileName(
            tenderNumber, contractNumber, routeNumber, progressPaymentNumber, periodDate);
        return Path.Combine(directory, fileName);
    }
}
