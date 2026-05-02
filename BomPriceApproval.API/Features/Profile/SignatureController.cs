using System.Security.Claims;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Profile;

[ApiController]
[Route("api/profile")]
[Authorize]
public class SignatureController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
    };
    private const long MaxBytes = 500 * 1024;

    [HttpPost("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Empty file" });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = "File too large (max 500KB)" });

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.TryGetValue(ext, out var mimeType))
            return BadRequest(new { error = "Only .png/.jpg/.jpeg allowed" });

        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        user.SignatureImage = ms.ToArray();
        user.SignatureMimeType = mimeType;
        await db.SaveChangesAsync();

        return Ok(new SignatureUploadResponse(user.SignatureImage.Length, DateTime.UtcNow));
    }

    [HttpGet("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> GetOwn()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user?.SignatureImage is null || user.SignatureImage.Length == 0)
            return NotFound(new { error = "No signature uploaded" });
        return File(user.SignatureImage, user.SignatureMimeType ?? "image/png");
    }
}
