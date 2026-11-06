namespace StayFlow.BuildingBlocks.MultiTenancy;

public interface ITenantContext
{
    Guid? OrganizationId { get; }
    Guid? PropertyId { get; }
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? UserEmail { get; }
    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);
}
