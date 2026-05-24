using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class IncomingMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public MessageType Type { get; set; } = MessageType.SMS;
        public List<MessageAttachment> Attachments { get; set; } = new();
    }
}
