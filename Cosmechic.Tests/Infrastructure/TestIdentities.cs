using System.Net.Http;

namespace Cosmechic.Tests.Infrastructure
{
    // Identités de test réutilisées par les classes de test. Les Id correspondent aux
    // AspNetUser seedés dans CosmechicsContext par chaque fixture de données.
    public static class TestIdentities
    {
        public const string CustomerAId = "user-a";
        public const string CustomerBId = "user-b";
        public const string AdminId = "user-admin";

        public static HttpClient AsAnonymous(this HttpClient client)
        {
            client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
            client.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeader);
            return client;
        }

        public static HttpClient AsUser(this HttpClient client, string userId, params string[] roles)
        {
            client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
            client.DefaultRequestHeaders.Remove(TestAuthHandler.RolesHeader);
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
            if (roles.Length > 0)
            {
                client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
            }
            return client;
        }

        public static HttpClient AsCustomerA(this HttpClient client) => client.AsUser(CustomerAId);
        public static HttpClient AsCustomerB(this HttpClient client) => client.AsUser(CustomerBId);
        public static HttpClient AsAdmin(this HttpClient client) => client.AsUser(AdminId, "Admin");
    }
}
