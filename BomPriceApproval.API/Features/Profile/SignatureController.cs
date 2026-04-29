using System.Security.Claims;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Profile;

[ApiController]
[Route("api/profile")]
[Authorize]
public class SignatureController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg"];
    private const long MaxBytes = 500 * 1024;

    [HttpPost("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Empty file" });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = "File too large (max 500KB)" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "Only .png/.jpg/.jpeg allowed" });

        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        var dir = config["Signatures:Directory"] ?? "/data/signatures";
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{CurrentUserId}.png");

        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream);

        user.SignatureImagePath = path;
        await db.SaveChangesAsync();

        return Ok(new SignatureUploadResponse(path, DateTime.UtcNow));
    }

    [HttpGet("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> GetOwn()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user?.SignatureImagePath is null)
            return NotFound(new { error = "No signature uploaded" });
        if (!System.IO.File.Exists(user.SignatureImagePath))
            return NotFound(new { error = "Signature file missing" });
        return PhysicalFile(user.SignatureImagePath, "image/png");
    }
}
