using Infrastructure.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Infrastructure.Identity
{
    public class JwtTokenService
    {
        private readonly JwtOptions _opts;
        private readonly TimeProvider _time;

        public JwtTokenService(JwtOptions opts, TimeProvider? time = null)
        {
            _opts = opts;
            _time = time ?? TimeProvider.System;
        }

        public string GenerateToken(IEnumerable<Claim> claims) => GenerateToken(claims, TimeSpan.FromMinutes(_opts.ExpiryMinutes)).Token;

        public (string Token, DateTime ExpiresAtUtc) GenerateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var now = _time.GetUtcNow().UtcDateTime;
            var expires = now + lifetime;
            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: creds);
            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }
    }
}
