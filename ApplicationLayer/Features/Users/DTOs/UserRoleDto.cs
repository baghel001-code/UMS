using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.DTOs
{
    public class UserRoleDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }
}
