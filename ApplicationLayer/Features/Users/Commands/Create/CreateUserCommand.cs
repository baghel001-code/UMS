using ApplicationLayer.Common;
using ApplicationLayer.Features.Users.DTOs;
using DomainLayer.ValueObjects;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.Commands.Create
{
    public record CreateUserCommand(string Name, Email Email, List<UserRoleDto> Roles) : IRequest<Result<Guid>>;
   
}
