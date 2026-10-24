using DomainLayer.Events;
using DomainLayer.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Events
{
    public class DapperDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly IMediator _mediator;


        public DapperDomainEventDispatcher(IMediator mediator) => _mediator = mediator;


        public async Task DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default)
        {
            await _mediator.Publish(domainEvent, ct);
        }


        public async Task DispatchAllAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken ct = default)
        {
            foreach (var ev in domainEvents)
                await DispatchAsync(ev, ct);
        }
    }
}
