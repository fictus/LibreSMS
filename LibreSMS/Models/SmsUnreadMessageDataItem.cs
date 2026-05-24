using System;
using System.Collections.Generic;
using System.Text;

namespace LibreSMS.Models
{
    public class SmsUnreadMessageDataItem
    {
        public string date { get; set; }
        public string id { get; set; }
        public string message { get; set; }
        public string messageType { get; set; }
        public string number { get; set; }
        public bool read { get; set; }
        public string receiver { get; set; }
        public string sender { get; set; }
        public string serviceCenter { get; set; }
        public int? threadID { get; set; }
    }
}
