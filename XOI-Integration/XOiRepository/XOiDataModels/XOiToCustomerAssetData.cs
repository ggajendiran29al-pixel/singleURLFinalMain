using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataModels;

namespace XOI_Integration.XOiRepository.XOiDataModels
{
    public class XOiToCustomerAssetData : AssetProperty
    {
        public DateTime? ManufactureDate { get; set; }
    }
}
