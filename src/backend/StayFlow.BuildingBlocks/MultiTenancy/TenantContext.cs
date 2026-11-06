using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StayFlow.BuildingBlocks.MultiTenancy;

namespace StayFlow.BuildingBlocks.MultiTenancy;

public class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlyCollection<string> Permissions { get; private set; } = [];

    public void SetContext(
        Guid? organizationId,
        Guid? propertyId,
        Guid? userId,
        string? userEmail,
        IEnumerable<string>? permissions = null)
    {
        OrganizationId = organizationId;
        PropertyId = propertyId;
        UserId = userId;
        UserEmail = userEmail;
        Permissions = permissions?.Distinct().ToList().AsReadOnly() ?? (IReadOnlyCollection<string>)[];
    }

    public bool HasPermission(string permission)
    {
        return Permissions.Contains(permission);
    }
}
