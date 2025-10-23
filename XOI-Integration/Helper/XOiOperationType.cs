using System;
using XOI_Integration.DataModels.Enums;

namespace XOI_Integration.Helper
{
    public static class XOiOperationType
    {
        public static OperationType DetermineOperationType(string myQueueItem, string xOiJobId)
        {
            var dataverseOperationType = DeserializeJSON.GetDataverseOperationType(myQueueItem);

            if (dataverseOperationType == OperationType.Create)
            {
                return OperationType.Create;
            }
            else if (String.IsNullOrEmpty(xOiJobId) && dataverseOperationType == OperationType.Update)
            {
                return OperationType.Create;
            }
            else if (dataverseOperationType == OperationType.Update)
            {
                return OperationType.Update;
            }
            else
            {
                throw new Exception("Undefined operation type");
            }
        }
    }
}
