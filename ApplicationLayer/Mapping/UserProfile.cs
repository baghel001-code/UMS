using ApplicationLayer.Features.Users.DTOs;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using VMM.Mapper;

namespace ApplicationLayer.Mapping
{
    /// <summary>
    /// Maps of the user feature (in-house VMM.Mapper). Only entity → DTO: a User is created and changed through its
    /// constructors and methods so the domain rules run, never by copying a DTO onto it.
    /// </summary>
    public class UserProfile : MapperProfile
    {
        public UserProfile()
        {
            CreateMap<User, UserDto>()
                .ForMember(d => d.Role, u => u.Roles.FirstOrDefault(r => r.IsPrimary) ?? u.Roles.FirstOrDefault()!)
                .ForMember(d => d.IsActive, u => u.Status == UserStatus.Active);
        }
    }
}
