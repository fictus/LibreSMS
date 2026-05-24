using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class GatewayStatus
    {
        public bool IsRunning { get; set; }
        public string ListenUrl { get; set; } = string.Empty;
        public int MessagesSent { get; set; }
        public int MessagesReceived { get; set; }
        public int WebhookDeliveries { get; set; }
        public int WebhookFailures { get; set; }
        public DateTime? StartedAt { get; set; }
        public bool IsDefaultSmsApp { get; set; }
    }
}
