using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using XOI_Integration.DataModels.Enums;

namespace XOI_Integration.Helper
{
    public class DeserializeJSON
    {
        public static Guid GetBookableResourceBookingId(string jsonString)
        {
            JObject jsonObject = JObject.Parse(jsonString);

            Guid bookableresourcebookingid = (Guid)jsonObject["InputParameters"][0]["value"]["Attributes"]
                .FirstOrDefault(attr => (string)attr["key"] == "bookableresourcebookingid")?["value"];

            return bookableresourcebookingid;
        }

        public static OperationType GetDataverseOperationType(string jsonString)
        {
            JObject jsonObject = JObject.Parse(jsonString);

            string messageName = (string)jsonObject["MessageName"];

            OperationType operationType = default;
            switch (messageName)
            {
                case "Create":
                    operationType = OperationType.Create;
                    break;
                case "Update":
                    operationType = OperationType.Update;
                    break;
                default:
                    break;
            }

            return operationType;
        }
    }
}
