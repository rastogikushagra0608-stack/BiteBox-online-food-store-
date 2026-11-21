using System.Security.Cryptography;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BiteBox.Infrastructure.Services;

/// <summary>
/// Payment gateway simulator. Behaves like a real adapter (async call, transaction reference, failure
/// reason) so the order/payment workflow is exercised end to end, but moves no money and never sees
/// card data. A Razorpay or Stripe test-mode adapter would implement the same <see cref="IPaymentGateway"/>.
/// </summary>
public sealed class SimulatedPaymentGateway(ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    public string Name => "BiteBox Pay (simulated)";

    public async Task<PaymentGatewayResult> ChargeAsync(PaymentGatewayRequest request, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct); // simulate network latency
        if (!request.SimulateSuccess)
        {
            logger.LogInformation("Simulated payment declined for {OrderNumber}", request.OrderNumber);
            return new PaymentGatewayResult(false, null, "Payment declined by bank (simulated)");
        }

        var reference = $"SIM_{DateTime.UtcNow:yyyyMMddHHmmss}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
        return new PaymentGatewayResult(true, reference, null);
    }
}

/// <summary>
/// Stores product images under wwwroot/uploads. Validates the real file signature ("magic bytes"),
/// not just the extension, and generates a random file name so user input never reaches the file system path.
/// </summary>
public sealed class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
{
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]] // "RIFF" (+ "WEBP" at offset 8, checked below)
    };

    public async Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct)
    {
        extension = extension.ToLowerInvariant() == ".jpeg" ? ".jpg" : extension.ToLowerInvariant();
        if (!Signatures.TryGetValue(extension, out var signatures))
            throw new BadRequestException("upload.type", "Only JPG, PNG or WEBP images are allowed.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > MaxBytes)
            throw new BadRequestException("upload.size", "Image must be smaller than 2 MB.");

        var bytes = buffer.GetBuffer();
        var valid = signatures.Any(sig => bytes.Length >= sig.Length && bytes.AsSpan(0, sig.Length).SequenceEqual(sig));
        if (valid && extension == ".webp") valid = bytes.Length > 12 && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (!valid) throw new BadRequestException("upload.signature", "The file content does not match its image type.");

        var root = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(root);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(root, fileName), buffer.ToArray(), ct);
        return $"/uploads/{fileName}";
    }
}
