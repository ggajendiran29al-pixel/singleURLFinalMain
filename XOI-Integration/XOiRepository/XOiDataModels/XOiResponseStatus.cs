using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataModels.Enums;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiResponseStatus
    {
        public string Message { get; set; }
        public JobResponseResult ResponseResult { get; set; }
    }
}
