using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AspApp.Models;

//*********************************** DataProtection_DbContext ************************************
class DataProtection_DbContext : DbContext, IDataProtectionKeyContext
{
    public DataProtection_DbContext(DbContextOptions<DataProtection_DbContext> options)
    : base(options) { }

    // This maps to the table that stores keys
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
}

//*********************************** Identity_DbContext ************************************
public class Identity_DbContext : IdentityDbContext<Identity_UserDbModel, Identity_RoleDbModel, Guid>
{
    public Identity_DbContext(DbContextOptions<Identity_DbContext> options) : base(options) { }
}

public class Identity_UserDbModel : IdentityUser<Guid>
{
    [MaxLength(128)]
    public string? DisplayName { get; set; }
    [MaxLength(1024)]
    public string? Description { get; set; }
    public bool PublicEmail { get; set; } = false;
    public byte _imageVersion { get; set; } = 0;
    [NotMapped]
    public int ImageVersion
    {
        get => _imageVersion;
        set => _imageVersion = value > 255 || value < 0 ? (byte)0 : (byte)value;
    }
    public bool HasImage { get; set; } = false;
    [MaxLength(1024)]
    public string? RemoteImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Identity_RoleDbModel : IdentityRole<Guid>
{
    public Identity_RoleDbModel() : base() { }
    public Identity_RoleDbModel(string roleName) : base(roleName) { }
    [MaxLength(128)]
    public string Description { get; set; } = string.Empty;
}