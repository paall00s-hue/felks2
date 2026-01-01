using Newtonsoft.Json;
using System;
using System.Text;

namespace WolfLive.Api.Models
{
	public class WolfMessage
	{
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("recipient")]
        public IdHash Recipient { get; set; }

        [JsonProperty("originator")]
        public IdHash Originator { get; set; }

        [JsonProperty("isGroup")]
        public bool IsGroup { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("data")]
        public byte[] ByteData { get; set; }

        [JsonProperty("flightId")]
        public string FlightId { get; set; }

        [JsonProperty("metadata")]
        public Metadata Metadata { get; set; }

        [JsonProperty("edited")]
        public Edited Edited { get; set; }
    }

    public class Metadata
	{
        public bool IsDeleted { get; set; }
	}

    public class Edited
	{
        public string SubscriberId { get; set; }
        public long Timestamp { get; set; }
	}

    public class Message
	{
        public static Encoding DataEncoding = Encoding.UTF8;

        public string FlightId { get; set; }
        public string MessageId { get; set; }
        public string UserId { get; set; }
        public string GroupId { get; set; }
        public bool IsGroup { get; set; }
        public DateTime Timestamp { get; set; }
        public string MimeType { get; set; }
        public string Content { get; set; }

        public Message(WolfMessage message)
		{
            FlightId = message.FlightId;
            MessageId = message.Id;
            UserId = message.Originator.Id;
            GroupId = message.IsGroup ? message.Recipient.Id : null;
            IsGroup = message.IsGroup;
            
            // Smart timestamp detection
            // Ticks for year 2000 is about 630,000,000,000,000,000 (18 digits)
            // Unix ms for year 2000 is about 946,000,000,000 (12 digits)
            // If value is small (less than year 3000 in Ticks), treat as Unix Milliseconds
            if (message.Timestamp < 1000000000000000) // Less than 16 digits -> definitely not Ticks for current era
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.Timestamp).UtcDateTime;
            }
            else
            {
                // Ensure Ticks are treated as UTC
                Timestamp = new DateTime(message.Timestamp, DateTimeKind.Utc);
            }

            MimeType = message.MimeType;
            Content = DataEncoding.GetString(message.ByteData);
		}
	}
}
