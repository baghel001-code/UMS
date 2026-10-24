using ApplicationLayer.Features.Users.DTOs;
using AutoMapper;
using DomainLayer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ApplicationLayer.Mapping
{
    public class UserProfile : Profile
    {
        public UserProfile()
        {
            CreateMap<User, UserDto>().ReverseMap();
            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());    // repository sets Id
                                                                     // .ForMember(dest => dest.PasswordHash, opt => opt.Ignore()); // set after hashing

            CreateMap<UpdateUserDto, User>();
               // .ForMember(dest => dest.PasswordHash, opt => opt.Ignore()); // handled in handler
        }
    }
}
