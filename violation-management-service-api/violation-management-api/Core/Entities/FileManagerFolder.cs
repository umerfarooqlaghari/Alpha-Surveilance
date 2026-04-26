namespace violation_management_api.Core.Entities;

public class FileManagerFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ParentFolderId { get; set; }    // null = root folder
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }

    // Navigation
    public FileManagerFolder? Parent { get; set; }
    public ICollection<FileManagerFolder> Children { get; set; } = new List<FileManagerFolder>();
    public ICollection<FileManagerFile> Files { get; set; } = new List<FileManagerFile>();
}
