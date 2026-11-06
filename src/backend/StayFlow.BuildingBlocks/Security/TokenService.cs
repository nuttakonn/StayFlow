using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StayFlow.BuildingBlocks.Domain.Identity;
using StayFlow.BuildingBlocks.Domain.Tenancy;

namespace StayFlow.BuildingBlocks.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = "StayFlow_Secret_Key_Minimum_32_Characters_Long_For_HmacSha256!";
    public string Issuer { get; set; } = "StayFlow";
    public string Audience { get; set; } = "StayFlowApp";
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 30;
}

public interface ITokenService
{
    string GenerateAccessToken(User user, Guid? organizationId, OrganizationRole? role, IEnumerable<string> permissions);
    string GenerateRefreshToken();
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateAccessToken(User user, Guid? organizationId, OrganizationRole? role, IEnumerable<string> permissions)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_options.SecretKey);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (organizationId.HasValue)
        {
            claims.Add(new Claim("org_id", organizationId.Value.ToString()));
        }

        if (role.HasValue)
        {
            claims.Add(new Claim("org_role", role.Value.ToString()));
            claims.Add(new Claim(ClaimTypes.Role, role.Value.ToString()));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
