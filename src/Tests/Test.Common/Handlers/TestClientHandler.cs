using System;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Horse.Mq;
using Horse.Mq.Clients;

namespace Test.Common.Handlers
{
    public class TestClientHandler : IClientHandler
    {
        private readonly TestHorseMq _mq;

        public TestClientHandler(TestHorseMq mq)
        {
            _mq = mq;
        }

        public Task Connected(HorseMq server, MqClient client)
        {
            Console.WriteLine("Client Connected");
            _mq.ClientConnected++;
            return Task.CompletedTask;
        }

        public Task Disconnected(HorseMq server, MqClient client)
        {
            Console.WriteLine("Client Disconnected");
            _mq.ClientDisconnected++;
            return Task.CompletedTask;
        }
    }
}