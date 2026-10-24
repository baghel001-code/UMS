using DomainLayer.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.Entities
{
    public abstract class BaseEntity
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        private readonly List<DomainEvent> _events = new();
        public IReadOnlyCollection<DomainEvent> DomainEvents => _events;
        protected void AddDomainEvent(DomainEvent ev) => _events.Add(ev);
        public void ClearDomainEvents() => _events.Clear();
    }

}
