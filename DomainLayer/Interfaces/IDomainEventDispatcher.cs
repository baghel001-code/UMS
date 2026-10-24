using DomainLayer.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.Interfaces
{
    public interface IDomainEventDispatcher
    {
        Task DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default);
        Task DispatchAllAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken ct = default);
    }
}
