using GraphQL.Client.Abstractions;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using GraphQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiTokenProvider;
using Newtonsoft.Json;

namespace XOI_Integration.XOiRepository.Provider
{
    public class XOiAPI
    {
        private readonly GraphQLHttpClient _graphQlClient;

        public XOiAPI()
        {
            _graphQlClient = XOiAPIConnectionClient.Instance;
        }

        public async Task<GraphQLResponse<T>> SendRequestAsync<T>(string query, dynamic variables)
        {
            var graphQlRequest = new GraphQLRequest
            {
                Query = query,
                Variables = variables
            };

            var graphQlResponse = await _graphQlClient.SendQueryAsync<T>(graphQlRequest);

            if (graphQlResponse == null)
            {
                throw new Exception("GraphQL response is null.");
            }

            return graphQlResponse;
        }
    }
}
