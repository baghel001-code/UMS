using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace VMM.Mediator
{
    /// <summary>
    /// Resolves the handler and the behaviours for the request's type from the DI container and runs them.
    /// The generic plumbing for each request type is built once and cached, so a Send costs no reflection.
    /// </summary>
    public sealed class Mediator : IMediator
    {
        private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Pipelines = new();
        private readonly IServiceProvider _services;

        public Mediator(IServiceProvider services) => _services = services;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var pipeline = (Pipeline<TResponse>)Pipelines.GetOrAdd((request.GetType(), typeof(TResponse)),
                key => Activator.CreateInstance(typeof(Pipeline<,>).MakeGenericType(key.Request, key.Response))!);
            return pipeline.Run(request, _services, cancellationToken);
        }

        public async Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);
            var publishers = Publishers.GetOrAdd(notification.GetType(), type =>
            {
                // the event's own type first, then its base classes, so a handler of DomainEvent sees every domain event
                var chain = new List<Publisher>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                    chain.Add((Publisher)Activator.CreateInstance(typeof(Publisher<>).MakeGenericType(t))!);
                return chain.ToArray();
            });
            foreach (var publisher in publishers)
                await publisher.Run(notification, _services, cancellationToken);
        }

        private static readonly ConcurrentDictionary<Type, Publisher[]> Publishers = new();

        private abstract class Publisher
        {
            public abstract Task Run(object notification, IServiceProvider services, CancellationToken cancellationToken);
        }

        private sealed class Publisher<TNotification> : Publisher where TNotification : notnull
        {
            public override async Task Run(object notification, IServiceProvider services, CancellationToken cancellationToken)
            {
                foreach (var handler in services.GetServices<INotificationHandler<TNotification>>())
                    await handler.Handle((TNotification)notification, cancellationToken);
            }
        }

        private abstract class Pipeline<TResponse>
        {
            public abstract Task<TResponse> Run(object request, IServiceProvider services, CancellationToken cancellationToken);
        }

        private sealed class Pipeline<TRequest, TResponse> : Pipeline<TResponse> where TRequest : IRequest<TResponse>
        {
            public override Task<TResponse> Run(object request, IServiceProvider services, CancellationToken cancellationToken)
            {
                var typed = (TRequest)request;
                var handler = services.GetService<IRequestHandler<TRequest, TResponse>>() ?? throw new HandlerNotFoundException(typeof(TRequest));

                RequestHandlerDelegate<TResponse> next = ct => handler.Handle(typed, ct == default ? cancellationToken : ct);
                // wrap from the innermost (last registered) outwards so the first registered behaviour runs first
                var behaviours = services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
                for (var i = behaviours.Length - 1; i >= 0; i--)
                {
                    var behaviour = behaviours[i];
                    var inner = next;
                    next = ct => behaviour.Handle(typed, inner, ct == default ? cancellationToken : ct);
                }
                return next(cancellationToken);
            }
        }
    }
}
