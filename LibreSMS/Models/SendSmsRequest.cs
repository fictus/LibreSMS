using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class SendSmsRequest
    {
        public string To { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
