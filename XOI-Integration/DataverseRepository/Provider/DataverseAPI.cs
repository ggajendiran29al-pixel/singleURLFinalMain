using System;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace XOI_Integration.DataverseRepository.Provider
{
    public sealed class DataverseApi
    {
        private static ServiceClient instance;
        private static object _lockObject = new object();
        private static string _connectionString;

        private DataverseApi() { }

        public static void Initialize(string connectionString)
        {
            _connectionString = connectionString;
        }

        public static ServiceClient Instance
        {
            get
            {
                try
                {
                    lock (_lockObject)
                    {
                        if (instance == null)
                        {
                            if (string.IsNullOrEmpty(_connectionString))
                            {
                                throw new InvalidOperationException("Connection string not initialized. Call Initialize method first.");
                            }
                            instance = CreateServiceClient();
                        }
                        return instance;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Unable to connect to Dataverse", ex);
                }

            }
        }

        private static ServiceClient CreateServiceClient()
        {
            ServiceClient serviceClient = new ServiceClient(_connectionString);
            return serviceClient;
        }
    }
}
