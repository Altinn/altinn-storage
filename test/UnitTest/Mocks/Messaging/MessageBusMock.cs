using System;
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
    }
}
