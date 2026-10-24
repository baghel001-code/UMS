using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using System.Security.Claims;

namespace Infrastructure.Identity
{
    public class IdentityService : ITokenIssuer
    {
        /// <summary>Claim with the sign-in session id; every API checks that the session is still active.</summary>
        public const string SessionClaim = "sid";
        /// <summary>Claim with the application the session belongs to (an AMS token is not accepted by another app's API).</summary>
        public const string AppClaim = "app";

        private readonly JwtTokenService _jwt;
        public IdentityService(JwtTokenService jwt) => _jwt = jwt;

        public IssuedToken IssueAccessToken(User user, UserSession session, TimeSpan lifetime)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Email, user.Email.Value),
                new(SessionClaim, session.Id.ToString()),
                new(AppClaim, session.AppCode),
                new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            };
            // UMS accounts also carry their tenant and employee code
            if (user.TenantId != Guid.Empty) claims.Add(new Claim(Services.CurrentTenantService.ClaimType, user.TenantId.ToString()));
            if (!string.IsNullOrEmpty(user.EmpCode)) claims.Add(new Claim("emp_code", user.EmpCode));

            var (token, expires) = _jwt.GenerateToken(claims, lifetime);
            return new IssuedToken(token, expires);
        }
    }
}
