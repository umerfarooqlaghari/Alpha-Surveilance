using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using violation_management_api.Services.Interfaces;
using System.Security.Claims;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/filemanager")]
[Authorize]
public class FileManagerController(
    AppViolationDbContext db,
    ICloudinaryService cloudinary,
    ILogger<FileManagerController> logger) : ControllerBase
{
    private Guid? TenantId =>
        Guid.TryParse(User.FindFirstValue("tenantId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    // ─── FOLDERS ─────────────────────────────────────────────────────────────

    /// <summary>Get all root folders (no parent) for the calling tenant.</summary>
    [HttpGet("folders")]
    public async Task<IActionResult> GetRootFolders()
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var folders = await db.FileManagerFolders
            .Where(f => f.TenantId == tid && f.ParentFolderId == null)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentFolderId, f.CreatedAt,
                db.FileManagerFolders.Count(c => c.ParentFolderId == f.Id),
                db.FileManagerFiles.Count(fi => fi.FolderId == f.Id)))
            .ToListAsync();

        // Also return root-level files (FolderId = null) so they appear in the root view
        var files = await db.FileManagerFiles
            .Where(f => f.TenantId == tid && f.FolderId == null)
            .OrderBy(f => f.Name)
            .Select(f => new FileDto(f.Id, f.Name, f.OriginalFileName, f.Url, f.ContentType, f.SizeBytes, f.FolderId, f.CreatedAt))
            .ToListAsync();

        return Ok(new { folders, files });
    }

    /// <summary>Get the contents of a specific folder (sub-folders + files).</summary>
    [HttpGet("folders/{id:guid}")]
    public async Task<IActionResult> GetFolderContents(Guid id)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var folder = await db.FileManagerFolders
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tid);
        if (folder == null) return NotFound();

        var subFolders = await db.FileManagerFolders
            .Where(f => f.ParentFolderId == id && f.TenantId == tid)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentFolderId, f.CreatedAt,
                db.FileManagerFolders.Count(c => c.ParentFolderId == f.Id),
                db.FileManagerFiles.Count(fi => fi.FolderId == f.Id)))
            .ToListAsync();

        var files = await db.FileManagerFiles
            .Where(f => f.FolderId == id && f.TenantId == tid)
            .OrderBy(f => f.Name)
            .Select(f => new FileDto(f.Id, f.Name, f.OriginalFileName, f.Url, f.ContentType, f.SizeBytes, f.FolderId, f.CreatedAt))
            .ToListAsync();

        // Build breadcrumb path
        var breadcrumb = await BuildBreadcrumb(id, tid.Value);

        return Ok(new FolderContentsResponse(folder.Id, folder.Name, folder.ParentFolderId, breadcrumb, subFolders, files));
    }

    /// <summary>Create a new folder.</summary>
    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest req)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Folder name is required." });

        // If parent is specified, verify it belongs to this tenant
        if (req.ParentFolderId.HasValue)
        {
            var parent = await db.FileManagerFolders.FirstOrDefaultAsync(
                f => f.Id == req.ParentFolderId && f.TenantId == tid);
            if (parent == null) return NotFound(new { error = "Parent folder not found." });
        }

        var folder = new FileManagerFolder
        {
            TenantId = tid.Value,
            ParentFolderId = req.ParentFolderId,
            Name = req.Name.Trim(),
            CreatedByUserId = UserId
        };

        db.FileManagerFolders.Add(folder);
        await db.SaveChangesAsync();

        logger.LogInformation("Tenant {TenantId} created folder '{Name}'", tid, folder.Name);
        return Ok(new FolderDto(folder.Id, folder.Name, folder.ParentFolderId, folder.CreatedAt, 0, 0));
    }

    /// <summary>Rename a folder.</summary>
    [HttpPatch("folders/{id:guid}")]
    public async Task<IActionResult> RenameFolder(Guid id, [FromBody] RenameRequest req)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var folder = await db.FileManagerFolders.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tid);
        if (folder == null) return NotFound();

        folder.Name = req.Name.Trim();
        await db.SaveChangesAsync();
        return Ok(new { folder.Id, folder.Name });
    }

    /// <summary>Delete a folder and all its contents (files + sub-folders recursive).</summary>
    [HttpDelete("folders/{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var folder = await db.FileManagerFolders.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tid);
        if (folder == null) return NotFound();

        await DeleteFolderRecursive(id, tid.Value);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ─── FILES ───────────────────────────────────────────────────────────────

    /// <summary>Upload one or more files into a folder (or root if folderId is omitted).</summary>
    [HttpPost("files/upload")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> UploadFiles([FromForm] UploadFilesRequest req)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        if (req.ParentFolderId.HasValue)
        {
            var ok = await db.FileManagerFolders.AnyAsync(
                f => f.Id == req.ParentFolderId && f.TenantId == tid);
            if (!ok) return NotFound(new { error = "Target folder not found." });
        }

        var results = new List<FileDto>();

        foreach (var formFile in req.Files)
        {
            try
            {
                var cloudFolder = $"tenants/{tid}/filemanager";
                var (url, publicId, contentType, sizeBytes) = await cloudinary.UploadFileAsync(formFile, cloudFolder);

                var entry = new FileManagerFile
                {
                    TenantId = tid.Value,
                    FolderId = req.ParentFolderId,
                    Name = formFile.FileName,
                    OriginalFileName = formFile.FileName,
                    CloudinaryPublicId = publicId,
                    Url = url,
                    ContentType = contentType,
                    SizeBytes = sizeBytes,
                    CreatedByUserId = UserId
                };

                db.FileManagerFiles.Add(entry);
                results.Add(new FileDto(entry.Id, entry.Name, entry.OriginalFileName, entry.Url, entry.ContentType, entry.SizeBytes, entry.FolderId, entry.CreatedAt));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload file {FileName}", formFile.FileName);
                return BadRequest(new { error = $"Failed to upload '{formFile.FileName}': {ex.Message}" });
            }
        }

        await db.SaveChangesAsync();
        return Ok(results);
    }

    /// <summary>Rename a file.</summary>
    [HttpPatch("files/{id:guid}")]
    public async Task<IActionResult> RenameFile(Guid id, [FromBody] RenameRequest req)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var file = await db.FileManagerFiles.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tid);
        if (file == null) return NotFound();

        file.Name = req.Name.Trim();
        await db.SaveChangesAsync();
        return Ok(new { file.Id, file.Name });
    }

    /// <summary>Delete a file (removes from Cloudinary too).</summary>
    [HttpDelete("files/{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();

        var file = await db.FileManagerFiles.FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tid);
        if (file == null) return NotFound();

        await cloudinary.DeleteFileAsync(file.CloudinaryPublicId);
        db.FileManagerFiles.Remove(file);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Search files and folders by name within the tenant.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        var tid = TenantId;
        if (tid == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q)) return Ok(new { folders = Array.Empty<object>(), files = Array.Empty<object>() });

        var lq = q.ToLower();

        var folders = await db.FileManagerFolders
            .Where(f => f.TenantId == tid && f.Name.ToLower().Contains(lq))
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentFolderId, f.CreatedAt, 0, 0))
            .ToListAsync();

        var files = await db.FileManagerFiles
            .Where(f => f.TenantId == tid && (f.Name.ToLower().Contains(lq) || f.OriginalFileName.ToLower().Contains(lq)))
            .OrderBy(f => f.Name)
            .Select(f => new FileDto(f.Id, f.Name, f.OriginalFileName, f.Url, f.ContentType, f.SizeBytes, f.FolderId, f.CreatedAt))
            .ToListAsync();

        return Ok(new { folders, files });
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private async Task DeleteFolderRecursive(Guid folderId, Guid tenantId)
    {
        var subFolders = await db.FileManagerFolders
            .Where(f => f.ParentFolderId == folderId && f.TenantId == tenantId)
            .ToListAsync();

        foreach (var sub in subFolders)
            await DeleteFolderRecursive(sub.Id, tenantId);

        var files = await db.FileManagerFiles
            .Where(f => f.FolderId == folderId && f.TenantId == tenantId)
            .ToListAsync();

        foreach (var file in files)
        {
            await cloudinary.DeleteFileAsync(file.CloudinaryPublicId);
            db.FileManagerFiles.Remove(file);
        }

        var folder = await db.FileManagerFolders.FindAsync(folderId);
        if (folder != null) db.FileManagerFolders.Remove(folder);
    }

    private async Task<List<BreadcrumbItem>> BuildBreadcrumb(Guid folderId, Guid tenantId)
    {
        var crumbs = new List<BreadcrumbItem>();
        var current = folderId;

        while (true)
        {
            var folder = await db.FileManagerFolders
                .Where(f => f.Id == current && f.TenantId == tenantId)
                .Select(f => new { f.Id, f.Name, f.ParentFolderId })
                .FirstOrDefaultAsync();

            if (folder == null) break;
            crumbs.Insert(0, new BreadcrumbItem(folder.Id, folder.Name));
            if (folder.ParentFolderId == null) break;
            current = folder.ParentFolderId.Value;
        }

        return crumbs;
    }

    // ─── DTOs ─────────────────────────────────────────────────────────────────

    public record FolderDto(Guid Id, string Name, Guid? ParentFolderId, DateTime CreatedAt, int ChildFolderCount, int FileCount);
    public record FileDto(Guid Id, string Name, string OriginalFileName, string Url, string ContentType, long SizeBytes, Guid? FolderId, DateTime CreatedAt);
    public record FolderContentsResponse(Guid Id, string Name, Guid? ParentFolderId, List<BreadcrumbItem> Breadcrumb, List<FolderDto> SubFolders, List<FileDto> Files);
    public record BreadcrumbItem(Guid Id, string Name);
    public record CreateFolderRequest(string Name, Guid? ParentFolderId);
    public record RenameRequest(string Name);

    public class UploadFilesRequest
    {
        public List<IFormFile> Files { get; set; } = new();
        public Guid? ParentFolderId { get; set; }
    }
}
