namespace VMM.Mediator
{
    /// <summary>Marker for every request.</summary>
    public interface IBaseRequest { }

    /// <summary>A command or query answered by exactly one <see cref="IRequestHandler{TRequest, TResponse}"/>.</summary>
    public interface IRequest<out TResponse> : IBaseRequest { }

    /// <summary>A request without a result.</summary>
    public interface IRequest : IRequest<Unit> { }

    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    /// <summary>The next step of the pipeline (the next behaviour, or finally the handler).</summary>
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-cutting step around every handler (validation, logging, timing…). Behaviours run in registration order:
    /// the first registered is the outermost.
    /// </summary>
    public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
    {
        Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    /// <summary>Sends a request to its handler through the pipeline.</summary>
    public interface ISender
    {
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    }

    /// <summary>Marker for an event (optional: any object can be published).</summary>
    public interface INotification { }

    /// <summary>Reacts to a published event. Every handler of the event's type (or a base type of it) runs, in registration order.</summary>
    public interface INotificationHandler<in TNotification> where TNotification : notnull
    {
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }

    /// <summary>Publishes an event to all its handlers (none is fine).</summary>
    public interface IPublisher
    {
        Task Publish(object notification, CancellationToken cancellationToken = default);
    }

    public interface IMediator : ISender, IPublisher { }

    /// <summary>The "void" result.</summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Value = default;
        public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);
        public bool Equals(Unit other) => true;
        public override bool Equals(object? obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public override string ToString() => "()";
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }

    public sealed class HandlerNotFoundException : InvalidOperationException
    {
        public HandlerNotFoundException(Type requestType)
            : base($"No handler is registered for {requestType.FullName}. Register its assembly with AddMediator.") { }
    }
}
