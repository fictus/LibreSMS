using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Level { get; set; } = "INFO";
        public string Message { get; set; } = string.Empty;
    }
}
