using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiTokenProvider;

namespace XOI_Integration.XOiRepository.Provider
{
    public class XOiAPIConnectionClient
    {
        private static readonly Lazy<GraphQLHttpClient> _lazyGraphQLClient = new Lazy<GraphQLHttpClient>(() =>
        {
            var graphQlUrl = Environment.GetEnvironmentVariable("XOiGrahpQLURL", EnvironmentVariableTarget.Process);
            var authentificationToken = new XOiAuthentificationToken();
            var token = authentificationToken.GetAuthTokenAsync().Result;

            var graphQlClient = new GraphQLHttpClient(graphQlUrl, new NewtonsoftJsonSerializer());
            graphQlClient.HttpClient.DefaultRequestHeaders.Add("Authorization", token.Token);

            return graphQlClient;
        });

        public static GraphQLHttpClient Instance => _lazyGraphQLClient.Value;
    }
}
