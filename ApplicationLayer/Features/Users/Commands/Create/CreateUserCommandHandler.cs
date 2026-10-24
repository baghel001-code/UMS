using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using DomainLayer.Entities;
using DomainLayer.Interfaces;
using DomainLayer.ValueObjects;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.Commands.Create
{
  
    public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<Guid>>
    {
        private readonly IRepository<User> _repo;
        private readonly IUserPolicyService _policy;

        public CreateUserCommandHandler(IRepository<User> repo, IUserPolicyService policy)
        {
            _repo = repo;
            _policy = policy;
        }


        public async Task<Result<Guid>> Handle(CreateUserCommand request, CancellationToken ct)
        {
            // Application Rule: Validate role combinations
            var roles = request.Roles
                .Select(r => new UserRole(r.Name, r.IsPrimary))
                .ToList();

            if (!_policy.IsRoleCombinationAllowed(roles))
                throw new AppException("Role combination is not allowed.");

            // Domain Rule: validated inside entity
            var user = new User(request.Name, request.Email);

            foreach (var role in roles)
                user.AddRole(role);

            //if (await _repo.EmailExists(request.Email))
            //    throw new AppException("Email already exists.");

            await _repo.AddAsync(user, ct);
           
            return Result<Guid>.Success(user.Id);
        }
    }
}
