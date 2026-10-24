using DomainLayer.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.Entities
{
    public sealed class UserPosition
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public Guid OrganizationUnitId { get; private set; }
        public Guid DepartmentId { get; private set; }
        public Guid DivisionId { get; private set; }
        public Guid SectionId { get; private set; }
        public Guid RegionalOfficeId { get; private set; }
        public Guid DesignationId { get; private set; }
        public PostType PostType { get; private set; }
        public string? Notes { get; private set; }
        public DateTime EffectiveFrom { get; private set; }
        public DateTime? EffectiveTo { get; private set; }


        // Parameterless ctor for Dapper/ORM
        private UserPosition() { }


        public UserPosition(Guid id, Guid userId, Guid organizationUnitId, Guid departmentId, Guid divisionId,
        Guid sectionId, Guid regionalOfficeId, Guid designationId, PostType postType, DateTime effectiveFrom,
        DateTime? effectiveTo = null, string? notes = null)
        {
            Id = id;
            UserId = userId;
            OrganizationUnitId = organizationUnitId;
            DepartmentId = departmentId;
            DivisionId = divisionId;
            SectionId = sectionId;
            RegionalOfficeId = regionalOfficeId;
            DesignationId = designationId;
            PostType = postType;
            EffectiveFrom = effectiveFrom;
            EffectiveTo = effectiveTo;
            Notes = notes;
        }
    }
}
