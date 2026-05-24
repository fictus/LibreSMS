using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class SendMmsRequest
    {
        public string To { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> ImageBase64 { get; set; } = new();
        public List<string> ImageUrls { get; set; } = new();
    }
}
