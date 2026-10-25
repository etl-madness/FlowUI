namespace FlowUI.Models;

/// <summary>
/// Granular CRUD + Execute + Admin permissions for UI access.
/// </summary>
public sealed class CrudPermissions
{
    public bool CanCreate
    {
        get; set;
    }
    public bool CanRead { get; set; } = true;
    public bool CanUpdate
    {
        get; set;
    }
    public bool CanDelete
    {
        get; set;
    }
    public bool CanExecute
    {
        get; set;
    }
    public bool IsAdmin
    {
        get; set;
    }

    public static CrudPermissions FullAdmin() => new()
    {
        CanCreate = true,
        CanRead = true,
        CanUpdate = true,
        CanDelete = true,
        CanExecute = true,
        IsAdmin = true
    };

    public static CrudPermissions ReadOnly() => new()
    {
        CanCreate = false,
        CanRead = true,
        CanUpdate = false,
        CanDelete = false,
        CanExecute = false,
        IsAdmin = false
    };

    public string ToSummaryBadge()
    {
        if (IsAdmin) return "ADMIN (FULL CRUD+EXEC)";
        var parts = new List<string>();
        if (CanCreate) parts.Add("C");
        if (CanRead) parts.Add("R");
        if (CanUpdate) parts.Add("U");
        if (CanDelete) parts.Add("D");
        if (CanExecute) parts.Add("EXEC");
        return parts.Count > 0 ? string.Join("·", parts) : "NONE";
    }
}

/// <summary>
/// Represents an authenticated or configured UI user stored in dbo.flow_ui_users.
/// </summary>
public sealed class AppUser
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool CanCreate
    {
        get; set;
    }
    public bool CanRead { get; set; } = true;
    public bool CanUpdate
    {
        get; set;
    }
    public bool CanDelete
    {
        get; set;
    }
    public bool CanExecute
    {
        get; set;
    }
    public bool IsAdmin
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string AuthProvider { get; set; } = "Database";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CrudPermissions GetPermissions() => new()
    {
        CanCreate = IsAdmin || CanCreate,
        CanRead = IsAdmin || CanRead,
        CanUpdate = IsAdmin || CanUpdate,
        CanDelete = IsAdmin || CanDelete,
        CanExecute = IsAdmin || CanExecute,
        IsAdmin = IsAdmin
    };
}

/// <summary>
/// Authentication request passed to an IUiAuthProvider.
/// </summary>
public sealed class AuthRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DomainOrTenant
    {
        get; set;
    }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Result returned by an IUiAuthProvider.
/// </summary>
public sealed class AuthResult
{
    public bool Succeeded
    {
        get; set;
    }
    public AppUser? User
    {
        get; set;
    }
    public string? ErrorMessage
    {
        get; set;
    }
    public string ProviderName { get; set; } = string.Empty;

    public static AuthResult Success(AppUser user, string providerName) => new()
    {
        Succeeded = true,
        User = user,
        ProviderName = providerName
    };

    public static AuthResult Fail(string errorMessage, string providerName) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage,
        ProviderName = providerName
    };
}
