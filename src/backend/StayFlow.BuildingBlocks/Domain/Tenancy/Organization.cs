using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Tenancy;

public enum OrganizationRole
{
    Owner = 1,
    Admin = 2,
    Manager = 3,
    Staff = 4,
    Viewer = 5
}

public class Organization : Entity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private readonly List<OrganizationMembership> _memberships = [];
    public IReadOnlyCollection<OrganizationMembership> Memberships => _memberships.AsReadOnly();

    private Organization() { } // EF Core

    public static Organization Create(string name, string? slug = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cleanName = name.Trim();
        var generatedSlug = string.IsNullOrWhiteSpace(slug)
            ? GenerateSlug(cleanName)
            : slug.Trim().ToLowerInvariant();

        return new Organization
        {
            Id = Guid.NewGuid(),
            Name = cleanName,
            Slug = generatedSlug,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private static string GenerateSlug(string text)
    {
        var slug = text.ToLowerInvariant().Replace(" ", "-");
        // Remove invalid characters
        return new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    }
}

public class OrganizationMembership : Entity<Guid>
{
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; } = OrganizationRole.Staff;

    private OrganizationMembership() { } // EF Core

    public static OrganizationMembership Create(Guid organizationId, Guid userId, OrganizationRole role)
    {
        return new OrganizationMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateRole(OrganizationRole newRole)
    {
        Role = newRole;
        Touch();
    }
}

/// <summary>
/// Grants a member access to a specific property with a property-scoped role.
/// Owner and Admin org-roles bypass property membership checks entirely.
/// </summary>
public class PropertyMembership : Entity<Guid>
{
    public Guid OrganizationMembershipId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public OrganizationRole Role { get; private set; } = OrganizationRole.Staff;

    private PropertyMembership() { } // EF Core

    public static PropertyMembership Create(
        Guid organizationMembershipId,
        Guid organizationId,
        Guid propertyId,
        OrganizationRole role)
    {
        return new PropertyMembership
        {
            Id = Guid.NewGuid(),
            OrganizationMembershipId = organizationMembershipId,
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Role = role,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateRole(OrganizationRole newRole)
    {
        Role = newRole;
        Touch();
    }
}
