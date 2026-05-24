using System;
using System.Collections.Generic;
using System.Text;

namespace LibreSMS.Models
{
    internal class SmsGetMessagesDataItem
    {
        public List<SmsUnreadMessageDataItem> messages { get; set; }
        public string description { get; set; }
        public bool isSuccessful { get; set; }
        public string requestMethod { get; set; }
    }
}
