using DomainLayer.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.DTOs
{
    public record CreateUserDto(
    string Name,
    Email Email,
    string Password,   // plain in DTO from client — hash before saving
    UserRole Role);
}
