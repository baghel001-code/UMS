using DomainLayer.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.DTOs
{
    public record UpdateUserDto(
    Guid Id,
    string Name,
    Email Email,
    string? Password,  // optional: if present, update password
    UserRole Role,
    bool IsActive);
}
