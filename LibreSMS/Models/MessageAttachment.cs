using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreSMS.Models
{
    public class MessageAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string Base64Data { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
