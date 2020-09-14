using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Test.Common;
using Test.Mq.Internal;
using Test.Mq.Models;
using Twino.Client.TMQ;
using Twino.Client.TMQ.Models;
using Twino.MQ.Clients;
using Twino.MQ.Queues;
using Twino.Protocols.TMQ;
using Xunit;

namespace Test.Queues
{
    public class ManagementTest
    {
        /// <summary>
        /// Client sends a channel join message to server
        /// </summary>
        [Fact]
        public async Task JoinChannel()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            TwinoResult joined = await client.Queues.Subscribe("broadcast-a", true);
            Assert.Equal(TwinoResultCode.Ok, joined.Code);

            TwinoQueue queue = server.Server.Queues.FirstOrDefault();
            Assert.NotNull(queue);

            List<QueueClient> clients = queue.ClientsClone;
            Assert.Single(clients);
        }

        /// <summary>
        /// Client sends a channel leave message to server
        /// </summary>
        [Fact]
        public async Task LeaveChannel()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            TwinoResult joined = await client.Queues.Subscribe("broadcast-a", true);
            Assert.Equal(TwinoResultCode.Ok, joined.Code);

            TwinoResult left = await client.Queues.Unsubscribe("broadcast-a", true);
            Assert.Equal(TwinoResultCode.Ok, left.Code);

            TwinoQueue queue = server.Server.Queues.FirstOrDefault();
            Assert.NotNull(queue);

            List<QueueClient> clients = queue.ClientsClone;
            Assert.Empty(clients);
        }


        /// <summary>
        /// Client sends a queue creation message
        /// </summary>
        [Fact]
        public async Task Create()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            TwinoResult created = await client.Queues.Create("queue-new");
            Assert.Equal(TwinoResultCode.Ok, created.Code);

            TwinoQueue queue = server.Server.Queues.FirstOrDefault(x => x.Name == "queue-new");
            Assert.NotNull(queue);
            Assert.Equal("queue-new", queue.Name);
        }

        [Fact]
        public async Task CreateWithProperties()
        {
            TestMqServer server = new TestMqServer();
            server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);
            Assert.True(client.IsConnected);

            TwinoResult created = await client.Queues.Create("queue-test", o =>
            {
                o.AcknowledgeTimeout = 33000;
                o.Status = MessagingQueueStatus.Pull;
            });
            Assert.Equal(TwinoResultCode.Ok, created.Code);

            TwinoQueue queue = server.Server.FindQueue("queue-test");
            Assert.NotNull(queue);

            Assert.Equal(TimeSpan.FromSeconds(33), queue.Options.AcknowledgeTimeout);
            Assert.Equal(QueueStatus.Pull, queue.Status);
        }

        [Fact]
        public async Task Update()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            Channel channel = server.Server.FindChannel("broadcast-a");
            Assert.NotNull(channel);

            TwinoQueue queue = channel.FindQueue(MessageA.ContentType);
            Assert.NotNull(queue);

            Assert.False(queue.Options.WaitForAcknowledge);
            Assert.False(queue.Options.SendOnlyFirstAcquirer);
            Assert.Equal(TimeSpan.FromSeconds(12), queue.Options.MessageTimeout);

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);
            Assert.True(client.IsConnected);

            TwinoResult updated = await client.Queues.SetOptions("broadcast-a", MessageA.ContentType, o =>
            {
                o.WaitForAcknowledge = true;
                o.MessageTimeout = 666000;
                o.SendOnlyFirstAcquirer = true;
            });
            Assert.Equal(TwinoResultCode.Ok, updated.Code);

            Assert.True(queue.Options.WaitForAcknowledge);
            Assert.True(queue.Options.SendOnlyFirstAcquirer);
            Assert.Equal(TimeSpan.FromSeconds(666), queue.Options.MessageTimeout);
        }

        [Fact]
        public async Task Delete()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            Channel channel = server.Server.FindChannel("broadcast-a");
            Assert.NotNull(channel);

            TwinoQueue queue = channel.FindQueue(MessageA.ContentType);
            Assert.NotNull(queue);

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);
            Assert.True(client.IsConnected);

            TwinoResult done = await client.Queues.Remove("broadcast-a", MessageA.ContentType);
            Assert.Equal(TwinoResultCode.Ok, done.Code);

            queue = channel.FindQueue(MessageA.ContentType);
            Assert.Null(queue);
        }

        [Fact]
        public async Task GetQueueInfo()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            var queues = await client.Queues.List();
            Assert.Equal(TwinoResultCode.Ok, queues.Result.Code);
            Assert.NotNull(queues.Model);
            var pushQueue = queues.Model.FirstOrDefault(x => x.Name == "push-a");
            Assert.NotNull(pushQueue);
        }

        [Fact]
        public async Task GetQueueList()
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            var result = await client.Queues.List("push-a");
            Assert.Equal(TwinoResultCode.Ok, result.Result.Code);
            Assert.NotNull(result.Model);
            var queue = result.Model.FirstOrDefault();
            Assert.NotNull(queue);
        }

        /// <summary>
        /// Clears messages in queue
        /// </summary>
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task ClearMessages(bool priorityMessages, bool messages)
        {
            TestTwinoMQ server = new TestTwinoMQ();
            await server.Initialize();
            int port = server.Start();

            TwinoQueue queue = server.Server.FindQueue("push-a");
            queue.AddStringMessageWithId("Hello, World", false, false);
            queue.AddStringMessageWithId("Hello, World", false, true);

            TmqClient client = new TmqClient();
            await client.ConnectAsync("tmq://localhost:" + port);

            var result = await client.Queues.ClearMessages("push-a", MessageA.ContentType, priorityMessages, messages);
            Assert.Equal(TwinoResultCode.Ok, result.Code);

            if (priorityMessages)
                Assert.Empty(queue.PriorityMessages);
            else
                Assert.NotEmpty(queue.PriorityMessages);

            if (messages)
                Assert.Empty(queue.Messages);
            else
                Assert.NotEmpty(queue.Messages);
        }
    }
}