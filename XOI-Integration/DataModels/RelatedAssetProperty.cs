using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XOI_Integration.DataModels
{
    public class RelatedAssetProperty : AssetProperty
    {
        public Guid AssetId { get; set; }
    }
}
