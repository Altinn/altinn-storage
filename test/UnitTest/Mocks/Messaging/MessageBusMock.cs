using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wolverine;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Messaging
{
    /// <summary>
    /// Simple mock implementation of <see cref="IMessageBus"/> used in unit tests.
    /// </summary>
    public class MessageBusMock : IMessageBus
    {
        public string TenantId { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Task PublishAsync<T>(T message, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, string? destination = null, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync<T>(T message, string? queueName = null, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task SchedulePublishAsync<T>(T message, DateTimeOffset time, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task SchedulePublishAsync<T>(T message, TimeSpan delay, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task ScheduleSendAsync<T>(T message, string destination, DateTimeOffset time, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task ScheduleSendAsync<T>(T message, string destination, TimeSpan delay, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task InvokeAsync<T>(T message, CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }

        public Task<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellation = default)
        {
            return Task.FromResult(default(TResponse)!);
        }

        public Task InvokeForTenantAsync(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            return Task.CompletedTask;
        }

        public Task<T> InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            return Task.FromResult(default(T)!);
        }

        public IDestinationEndpoint EndpointFor(string endpointName)
        {
            throw new NotImplementedException();
        }

        public IDestinationEndpoint EndpointFor(Uri uri)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message)
        {
            throw new NotImplementedException();
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions options = null)
        {
            throw new NotImplementedException();
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions options = null)
        {
            throw new NotImplementedException();
        }

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions options = null)
        {
            throw new NotImplementedException();
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            throw new NotImplementedException();
        }

        public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            throw new NotImplementedException();
        }
    }
}
