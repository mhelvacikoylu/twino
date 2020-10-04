using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Twino.Client.TMQ.Annotations;
using Twino.Client.TMQ.Models;

namespace Sample.Producer
{
    [QueueName("model-a")]
    [QueueStatus(MessagingQueueStatus.Push)]
    public class ModelA
    {
        [JsonProperty("no")]
        [JsonPropertyName("no")]
        public int No { get; set; }

        [JsonProperty("foo")]
        [JsonPropertyName("foo")]
        public string Foo { get; set; }
    }
}