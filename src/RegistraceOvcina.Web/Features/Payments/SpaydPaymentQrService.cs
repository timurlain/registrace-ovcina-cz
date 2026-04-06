using System.Globalization;
using QRCoder;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Payments;

public sealed class SpaydPaymentQrService
{
    public PaymentQrModel? Build(Game game, RegistrationSubmission submission)
    {
        if (submission.ExpectedTotalAmount <= 0)
        {
            return null;
        }

        var normalizedAccount = NormalizeBankAccount(game.BankAccount);
        if (string.IsNullOrWhiteSpace(normalizedAccount))
        {
            return null;
        }

        var variableSymbol = string.IsNullOrWhiteSpace(submission.PaymentVariableSymbol)
            ? submission.Id.ToString("D10", CultureInfo.InvariantCulture)
            : submission.PaymentVariableSymbol!;

        var spaydPayload = string.Join(
            '*',
            [
                "SPD",
                "1.0",
                $"ACC:{normalizedAccount}",
                $"AM:{submission.ExpectedTotalAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
                "CC:CZK",
                $"X-VS:{variableSymbol}",
                $"MSG:{SanitizeMessage($"Ovčina {game.Name}")}"
            ]);

        var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(spaydPayload, QRCodeGenerator.ECCLevel.Q);
        var svgQr = new SvgQRCode(qrData);
        var svgMarkup = svgQr.GetGraphic(8);

        // QRCoder outputs SVG with fixed width/height but no viewBox.
        // Add viewBox so the SVG can scale responsively with CSS.
        var moduleCount = qrData.ModuleMatrix.Count;
        var totalSize = moduleCount * 8;
        svgMarkup = svgMarkup.Replace(
            $"width=\"{totalSize}\" height=\"{totalSize}\"",
            $"width=\"{totalSize}\" height=\"{totalSize}\" viewBox=\"0 0 {totalSize} {totalSize}\"");

        return new PaymentQrModel(
            spaydPayload,
            svgMarkup,
            variableSymbol,
            normalizedAccount,
            submission.ExpectedTotalAmount);
    }

    private static string NormalizeBankAccount(string account) =>
        account.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string SanitizeMessage(string message) =>
        message.Replace("*", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}

public sealed record PaymentQrModel(
    string SpaydPayload,
    string SvgMarkup,
    string VariableSymbol,
    string NormalizedBankAccount,
    decimal Amount);
