﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Horse.Mq;
using Horse.Mq.Client;
using Horse.Mq.Client.Bus;
using Horse.Mq.Client.Connectors;
using Horse.Mq.Client.Models;
using Horse.Mq.Delivery;
using Horse.Mq.Handlers;
using Horse.Mq.Queues;
using Horse.Protocols.Hmq;
using Horse.Server;

namespace Sample.Cache
{
    class Program
    {
        static async Task Main(string[] args)
        {
            HorseMq mq = StartServer();

            HmqStickyConnector producer = new HmqStickyConnector(TimeSpan.FromSeconds(2));
            producer.AddHost("hmq://localhost:26223");
            producer.ContentSerializer = new NewtonsoftContentSerializer();
            producer.Run();

            HmqStickyConnector consumer = new HmqStickyConnector(TimeSpan.FromSeconds(2));
            consumer.AddHost("hmq://localhost:26223");
            consumer.ContentSerializer = new NewtonsoftContentSerializer();
            consumer.Run();

            IHorseQueueBus producerBus = producer.Bus.Queue;
            IHorseQueueBus consumerBus = consumer.Bus.Queue;

            CacheModel model = new CacheModel {Foo = "Hello, Cache #1"};
            HorseResult result = await producerBus.PushJson(model);
            if (result.Code == HorseResultCode.Ok)
                Console.WriteLine($"Cached message added as Cache #1");

            var serializer = new NewtonsoftContentSerializer();

            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(1000);
                PullRequest pullRequest = PullRequest.Single("KeyA");
                PullContainer container = await consumerBus.Pull(pullRequest);

                if (container.ReceivedCount > 0)
                {
                    HorseMessage message = container.ReceivedMessages.FirstOrDefault();
                    CacheModel receivedModel = (CacheModel) serializer.Deserialize(message, typeof(CacheModel));
                    //or.. Newtonsoft.Json.JsonConvert.DeserializeObject<CacheModel>(message.GetStringContent())
                    Console.WriteLine($"{i + 1} times received: {receivedModel.Foo}");
                }
                else
                    Console.WriteLine("Cache expired");
            }

            CacheModel model2 = new CacheModel {Foo = "Hello, Cache #2"};
            HorseResult result2 = await producerBus.PushJson(model2);
            if (result2.Code == HorseResultCode.Ok)
                Console.WriteLine($"Cached message updated as Cache #2");


            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                PullRequest pullRequest = PullRequest.Single("KeyA");
                PullContainer container = await consumerBus.Pull(pullRequest);

                if (container.ReceivedCount > 0)
                {
                    HorseMessage message = container.ReceivedMessages.FirstOrDefault();
                    CacheModel receivedModel = (CacheModel) serializer.Deserialize(message, typeof(CacheModel));
                    //or.. Newtonsoft.Json.JsonConvert.DeserializeObject<CacheModel>(message.GetStringContent())
                    Console.WriteLine($"{i + 1} times received: {receivedModel.Foo}");
                }
                else
                    Console.WriteLine("Cache expired");
            }

            Console.ReadLine();
        }

        private static HorseMq StartServer()
        {
            HorseMq mq = HorseMqBuilder.Create()
                                       .AddOptions(o => o.Status = QueueStatus.Cache)
                                       .UseAckDeliveryHandler(AcknowledgeWhen.AfterReceived, PutBackDecision.No)
                                       .Build();

            HorseServer server = new HorseServer();
            server.UseHorseMq(mq);
            server.Start(26223);
            return mq;
        }
    }
}