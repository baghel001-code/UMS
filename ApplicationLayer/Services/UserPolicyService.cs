using DomainLayer.Interfaces;
using DomainLayer.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Services
{
    public class UserPolicyService : IUserPolicyService
    {
        public bool IsRoleCombinationAllowed(List<UserRole> roles)
        {
            // Example conflicting roles
            var invalid = roles.Any(r => r.Name == "SYSTEM_ADMIN") && roles.Any(r => r.Name == "USER_ADMIN");

            return !invalid;
        }
    }
}
