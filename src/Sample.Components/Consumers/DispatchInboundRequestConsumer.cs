namespace Sample.Components.Consumers
{
    using System;
    using System.Threading.Tasks;
    using Contracts;
    using MassTransit;
    using Services;


    public class DispatchInboundRequestConsumer :
        IConsumer<DispatchRequest>
    {
        readonly IServiceEndpointLocator _endpointLocator;
        readonly IRequestRoutingService _routingService;

        public DispatchInboundRequestConsumer(IRequestRoutingService routingService, IServiceEndpointLocator endpointLocator)
        {
            _routingService = routingService;
            _endpointLocator = endpointLocator;
        }

        public async Task Consume(ConsumeContext<DispatchRequest> context)
        {
            // When redelivered, push through the state machine to avoid duplicate transactions
            if (context.ReceiveContext.Redelivered)
            {
                await context.Forward(_endpointLocator.TransactionStateEndpointAddress);
                return;
            }

            var routeResult = await _routingService.RouteRequest(context.Message);

            if (routeResult.Disposition is RouteDisposition.Unhandled or RouteDisposition.Ambiguous)
            {
                var completedTimestamp = DateTime.UtcNow;

                await context.RespondAsync(new DispatchInboundRequestCompleted
                {
                    TransactionId = context.Message.TransactionId,
                    RoutingKey = context.Message.RoutingKey,
                    Body = context.Message.Body,
                    CompletedTimestamp = completedTimestamp
                });

                await context.Publish(new RequestNotDispatched
                {
                    RequestMessageId = context.MessageId,
                    TransactionId = context.Message.TransactionId,
                    RoutingKey = context.Message.RoutingKey,
                    ReceiveTimestamp = context.Message.ReceiveTimestamp,
                    Deadline = context.ExpirationTime
                });

                return;
            }

            await context.Forward(routeResult.DestinationAddress);
        }
    }
}