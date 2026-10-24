using Microsoft.Extensions.DependencyInjection;
using VMM.Mapper;

namespace BuildingBlocks.Tests
{
    public enum Status { Active, Locked }

    public sealed class Employee
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public Status Status { get; set; }
        public int? Grade { get; set; }
        public Address Home { get; set; } = new();
        public List<Address> Offices { get; set; } = new();
        public string Secret { get; set; } = "hash";
    }

    public sealed class Address
    {
        public string City { get; set; } = "";
    }

    public record AddressDto(string City);

    public record EmployeeDto(Guid Id, string FullName, string Status, int Grade, AddressDto Home, IReadOnlyList<AddressDto> Offices);

    public sealed class EmployeeForm
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public sealed class EmployeeProfile : MapperProfile
    {
        public EmployeeProfile()
        {
            CreateMap<Address, AddressDto>().ReverseMap();
            CreateMap<Employee, EmployeeDto>().ForMember(d => d.FullName, e => $"{e.FirstName} {e.LastName}");
            CreateMap<EmployeeForm, Employee>().Ignore(d => d.Id).Ignore(d => d.Grade).Ignore(d => d.Home).Ignore(d => d.Offices).Ignore(d => d.Secret);
        }
    }

    public sealed class BrokenProfile : MapperProfile
    {
        public BrokenProfile() => CreateMap<EmployeeForm, EmployeeDto>();
    }

    public class MapperTests
    {
        private static readonly IMapper Mapper = new MapperConfiguration(new EmployeeProfile()).CreateMapper();

        private static Employee Sample() => new()
        {
            FirstName = "Priya", LastName = "Verma", Status = Status.Locked, Grade = 4, Home = new Address { City = "Delhi" },
            Offices = { new Address { City = "Gurugram" }, new Address { City = "Noida" } }
        };

        [Fact]
        public void Maps_into_a_record_through_its_constructor_with_conversions_and_nested_maps()
        {
            var e = Sample();
            var dto = Mapper.Map<EmployeeDto>(e);

            Assert.Equal(e.Id, dto.Id);
            Assert.Equal("Priya Verma", dto.FullName);
            Assert.Equal("Locked", dto.Status);           // enum -> string
            Assert.Equal(4, dto.Grade);                   // int? -> int
            Assert.Equal("Delhi", dto.Home.City);         // nested map
            Assert.Equal(new[] { "Gurugram", "Noida" }, dto.Offices.Select(o => o.City));   // collection of maps
        }

        [Fact]
        public void Maps_into_settable_properties_and_skips_ignored_ones()
        {
            var e = Mapper.Map<EmployeeForm, Employee>(new EmployeeForm { FirstName = "Rahul", LastName = "Agarwal", Status = "active" });

            Assert.Equal("Rahul", e.FirstName);
            Assert.Equal(Status.Active, e.Status);        // string -> enum, case-insensitive
            Assert.Equal("hash", e.Secret);
        }

        [Fact]
        public void Copies_onto_an_existing_object()
        {
            var existing = Sample();
            var id = existing.Id;

            Mapper.Map(new EmployeeForm { FirstName = "P.", LastName = "Verma", Status = "Active" }, existing);

            Assert.Equal("P.", existing.FirstName);
            Assert.Equal(id, existing.Id);
            Assert.Equal("Delhi", existing.Home.City);
        }

        [Fact]
        public void Reverse_maps_work_by_convention()
        {
            Assert.Equal("Pune", Mapper.Map<Address>(new AddressDto("Pune")).City);
        }

        [Fact]
        public void Valid_configuration_passes_and_an_unmapped_member_is_reported()
        {
            new MapperConfiguration(new EmployeeProfile()).AssertConfigurationIsValid();

            var ex = Assert.Throws<MappingException>(() => new MapperConfiguration(new BrokenProfile()).AssertConfigurationIsValid());
            Assert.Contains("EmployeeForm -> EmployeeDto", ex.Message);
        }

        [Fact]
        public void A_missing_map_fails_clearly()
        {
            var ex = Assert.Throws<MappingException>(() => Mapper.Map<EmployeeForm>(new Address()));
            Assert.Contains("No map from Address to EmployeeForm", ex.Message);
        }

        [Fact]
        public void Registers_a_validated_singleton_mapper()
        {
            var services = new ServiceCollection();
            Assert.Throws<MappingException>(() => services.AddObjectMapper(typeof(MapperTests).Assembly));   // BrokenProfile lives here too

            var sp = new ServiceCollection().AddSingleton(new MapperConfiguration(new EmployeeProfile()).CreateMapper()).BuildServiceProvider();
            Assert.Equal("Delhi", sp.GetRequiredService<IMapper>().Map<AddressDto>(new Address { City = "Delhi" }).City);
        }
    }
}
