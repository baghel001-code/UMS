using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationLayer.Features.Users.Commands.Create
{
    public class CreateUserValidator : AbstractValidator<CreateUserCommand>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MinimumLength(3).WithMessage("Name is required"); ;
            RuleFor(x => x.Email.Value).EmailAddress().NotEmpty().WithMessage("Invalid email."); 

            RuleFor(x => x.Roles)
                .NotEmpty()
                .WithMessage("At least one role is required.");

            RuleForEach(x => x.Roles).ChildRules(role =>
            {
                role.RuleFor(r => r.Name).NotEmpty();
            });

            RuleFor(x => x.Roles.Count(r => r.IsPrimary))
                .Equal(1).WithMessage("Exactly one primary role is required.");
        }
    }

}
