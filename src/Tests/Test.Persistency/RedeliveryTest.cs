using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horse.Mq;
using Horse.Mq.Data;
using Horse.Mq.Data.Configuration;
using Horse.Mq.Queues;
using Horse.Protocols.Hmq;
using Horse.Server;
using Xunit;

namespace Test.Persistency
{
    public class RedeliveryTest
    {
        [Fact]
        public async Task ServiceMethods()
        {
            RedeliveryService service = new RedeliveryService("1.delivery");
            await service.Load();
            await service.Clear();

            List<KeyValuePair<string, int>> deliveries = service.GetDeliveries();
            Assert.Empty(deliveries);

            await service.Set("msg1", 1);
            deliveries = service.GetDeliveries();
            Assert.NotEmpty(deliveries);
            Assert.Equal("msg1", deliveries[0].Key);
            Assert.Equal(1, deliveries[0].Value);

            await service.Set("msg1", 2);
            deliveries = service.GetDeliveries();
            Assert.Single(deliveries);
            Assert.Equal(2, deliveries[0].Value);

            await service.Remove("msg1");
            deliveries = service.GetDeliveries();
            Assert.Empty(deliveries);

            await service.Close();
            service.Delete();
        }

        [Fact]
        public async Task ReloadAfterRestart()
        {
            await Task.Delay(500);
            ConfigurationFactory.Destroy();
            RedeliveryService service = new RedeliveryService("data/reload-test.tdb.delivery");
            await service.Load();
            await service.Clear();
            await service.Set("id", 4);
            await service.Close();

            if (System.IO.File.Exists("data/config.json"))
                System.IO.File.Delete("data/config.json");

            if (System.IO.File.Exists("data/reload-test.tdb"))
                System.IO.File.Delete("data/reload-test.tdb");

            if (System.IO.File.Exists("data/reload-test.tdb.delivery"))
                System.IO.File.Delete("data/reload-test.tdb.delivery");

            HorseServer server = new HorseServer();
            PersistentDeliveryHandler handler = null;
            Func<DeliveryHandlerBuilder, Task<IMessageDeliveryHandler>> fac = async builder =>
            {
                DatabaseOptions options = new DatabaseOptions
                                          {
                                              Filename = "data/reload-test.tdb",
                                              InstantFlush = true,
                                              CreateBackupOnShrink = false,
                                              ShrinkInterval = TimeSpan.FromSeconds(60)
                                          };

                handler = (PersistentDeliveryHandler) await builder.CreatePersistentDeliveryHandler(o =>
                {
                    return new PersistentDeliveryHandler(builder.Queue,
                                                         options,
                                                         DeleteWhen.AfterSend,
                                                         ProducerAckDecision.None,
                                                         true);
                });
                return handler;
            };

            HorseMq mq = server.UseHorseMq(cfg => cfg
                                                  .AddPersistentQueues(q => q.KeepLastBackup())
                                                  .UseDeliveryHandler(fac));

            HorseQueue queue = await mq.CreateQueue("reload-test",
                                                    o => o.Status = QueueStatus.Push);

            HorseMessage msg = new HorseMessage(MessageType.QueueMessage, "reload-test");
            msg.SetMessageId("id");
            msg.SetStringContent("Hello, World!");
            await queue.Push(msg);

            QueueMessage queueMsg = queue.Messages.FirstOrDefault();
            await handler.BeginSend(queue, queueMsg);

            await handler.RedeliveryService.Close();
            ConfigurationFactory.Destroy();

            mq = server.UseHorseMq(cfg => cfg
                                          .AddPersistentQueues(q => q.KeepLastBackup())
                                          .UseDeliveryHandler(fac));

            await mq.LoadPersistentQueues();
            HorseQueue queue2 = mq.FindQueue("reload-test");
            Assert.NotNull(queue2);
            Assert.NotEmpty(queue2.Messages);
            QueueMessage loadedMsg = queue2.Messages.FirstOrDefault();
            Assert.NotNull(loadedMsg);
            Assert.Equal(1, loadedMsg.DeliveryCount);
        }
    }
}