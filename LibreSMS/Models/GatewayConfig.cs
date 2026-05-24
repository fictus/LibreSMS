using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class GatewayConfig
    {
        public string WebhookUrl { get; set; } = string.Empty;
        public int HttpPort { get; set; } = 8686;
        public bool AutoStart { get; set; } = false;
        public bool ForwardSms { get; set; } = true;
        public bool ForwardMms { get; set; } = true;
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
