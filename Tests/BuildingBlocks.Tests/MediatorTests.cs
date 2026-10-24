using Microsoft.Extensions.DependencyInjection;
using VMM.Mediator;

namespace BuildingBlocks.Tests
{
    public record Ping(string Text) : IRequest<string>;
    public record Fire(List<string> Log) : IRequest;

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong: " + request.Text);
    }

    public sealed class FireHandler : IRequestHandler<Fire, Unit>
    {
        public Task<Unit> Handle(Fire request, CancellationToken cancellationToken)
        {
            request.Log.Add("handler");
            return Unit.Task;
        }
    }

    public class SomethingHappened { public string What { get; init; } = ""; }
    public sealed class OrderPlaced : SomethingHappened { }

    public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced>
    {
        private readonly Trace _trace;
        public OrderPlacedHandler(Trace trace) => _trace = trace;
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) { _trace.Steps.Add("order: " + notification.What); return Task.CompletedTask; }
    }

    public sealed class AnyEventHandler : INotificationHandler<SomethingHappened>
    {
        private readonly Trace _trace;
        public AnyEventHandler(Trace trace) => _trace = trace;
        public Task Handle(SomethingHappened notification, CancellationToken cancellationToken) { _trace.Steps.Add("any: " + notification.What); return Task.CompletedTask; }
    }

    public sealed class Trace
    {
        public List<string> Steps { get; } = new();
    }

    public sealed class OuterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly Trace _trace;
        public OuterBehavior(Trace trace) => _trace = trace;

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _trace.Steps.Add("outer in");
            var r = await next();
            _trace.Steps.Add("outer out");
            return r;
        }
    }

    public sealed class InnerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly Trace _trace;
        public InnerBehavior(Trace trace) => _trace = trace;

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _trace.Steps.Add("inner in");
            var r = await next();
            _trace.Steps.Add("inner out");
            return r;
        }
    }

    public sealed class Rejecting<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("rejected before the handler");
    }

    public class MediatorTests
    {
        private static IServiceProvider Build(Action<MediatorOptions>? more = null)
        {
            var services = new ServiceCollection().AddSingleton<Trace>();
            services.AddMediator(o =>
            {
                o.RegisterServicesFromAssemblyContaining<MediatorTests>();
                more?.Invoke(o);
            });
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task Sends_a_request_to_its_handler()
        {
            var mediator = Build().GetRequiredService<IMediator>();
            Assert.Equal("pong: hi", await mediator.Send(new Ping("hi")));
        }

        [Fact]
        public async Task Requests_without_a_result_return_Unit()
        {
            var log = new List<string>();
            Assert.Equal(Unit.Value, await Build().GetRequiredService<ISender>().Send(new Fire(log)));
            Assert.Equal(new[] { "handler" }, log);
        }

        [Fact]
        public async Task Behaviours_wrap_the_handler_in_registration_order()
        {
            var sp = Build(o => o.AddOpenBehavior(typeof(OuterBehavior<,>)).AddOpenBehavior(typeof(InnerBehavior<,>)));

            await sp.GetRequiredService<IMediator>().Send(new Ping("x"));

            Assert.Equal(new[] { "outer in", "inner in", "inner out", "outer out" }, sp.GetRequiredService<Trace>().Steps);
        }

        [Fact]
        public async Task Behaviours_registered_directly_in_the_container_also_run()
        {
            var services = new ServiceCollection().AddSingleton<Trace>();
            services.AddMediator(o => o.RegisterServicesFromAssemblyContaining<MediatorTests>());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Rejecting<,>));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => services.BuildServiceProvider().GetRequiredService<IMediator>().Send(new Ping("x")));
            Assert.Equal("rejected before the handler", ex.Message);
        }

        [Fact]
        public async Task A_request_without_a_handler_fails_clearly()
        {
            var sp = new ServiceCollection().AddMediator(_ => { }).BuildServiceProvider();
            await Assert.ThrowsAsync<HandlerNotFoundException>(() => sp.GetRequiredService<IMediator>().Send(new Ping("x")));
        }

        [Fact]
        public async Task Published_events_reach_handlers_of_their_type_and_base_types()
        {
            var sp = Build();

            await sp.GetRequiredService<IPublisher>().Publish(new OrderPlaced { What = "A1" });
            await sp.GetRequiredService<IMediator>().Publish(new SomethingHappened { What = "B2" });
            await sp.GetRequiredService<IMediator>().Publish("an event nobody handles");

            Assert.Equal(new[] { "order: A1", "any: A1", "any: B2" }, sp.GetRequiredService<Trace>().Steps);
        }

        [Fact]
        public void Only_open_generic_behaviours_are_accepted() =>
            Assert.Throws<ArgumentException>(() => new MediatorOptions().AddOpenBehavior(typeof(PingHandler)));
    }
}
