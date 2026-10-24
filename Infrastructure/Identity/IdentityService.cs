using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Identity
{
    public class IdentityService : ITokenIssuer
    {
        private readonly JwtTokenService _jwt;
        public IdentityService(JwtTokenService jwt) => _jwt = jwt;


        public string Authenticate(User user)
        {
            var claims = new List<Claim>
                        {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Name),
                        new Claim(ClaimTypes.Email, user.Email.Value)
                        };

            // UMS accounts also carry their tenant and employee code
            if (user.TenantId != Guid.Empty) claims.Add(new Claim(Services.CurrentTenantService.ClaimType, user.TenantId.ToString()));
            if (!string.IsNullOrEmpty(user.EmpCode)) claims.Add(new Claim("emp_code", user.EmpCode));


            return _jwt.GenerateToken(claims);
        }

        public string IssueToken(User user) => Authenticate(user);
    }
}
