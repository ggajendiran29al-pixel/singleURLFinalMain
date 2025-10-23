using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.DataModels
{
    public class BookableResourceBookingTimeline
    {
        public string Title { get; set; }   
        public string Timelinetext { get; set; }
        public Guid TimelineId { get; set; }
    }
}
