using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.DataModels
{
    public class BookableResourceBookingNote
    {
        public string Note { get; set; }
        public Guid NoteId { get; set; }
        public Guid BookingId { get; set; } //added on 6th feb
        public string Hash { get; set; } //added on 10 march
    }
}
