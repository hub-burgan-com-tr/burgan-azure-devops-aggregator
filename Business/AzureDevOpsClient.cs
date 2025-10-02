using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BurganAzureDevopsAggregator.Business
{
    public class AzureDevOpsClient
    {
        private const string ApiVersion = "7.1-preview.3";

        private readonly HttpClient _httpClient;
        private readonly ILogger<AzureDevOpsClient> _logger;
        private readonly string _organization;
        public AzureDevOpsClient(HttpClient httpClient, IConfiguration configuration, ILogger<AzureDevOpsClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _organization = configuration["AzureDevOps:Organization"]
                ?? throw new ArgumentNullException("AzureDevOps:Organization");

            var pat = configuration["AzureDevOps:PersonalAccessToken"]
                ?? throw new ArgumentNullException("AzureDevOps:PersonalAccessToken");

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        public async Task AddCommentToWorkItemAsync(int workItemId, string comment,string _project)
        {
            var url = $"{_organization}/{_project}/_apis/wit/workItems/{workItemId}/comments?api-version={ApiVersion}";

            var content = new
            {
                text = comment
            };

            var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json")
            );

            await EnsureSuccessStatusCodeAsync(response, $"Yorum eklenemedi. WorkItem ID: {workItemId}");
        }

public async Task UpdateWorkItemFieldAsync(int workItemId, Dictionary<string, string> fields, string project)
{
    var url = $"{_organization}/{project}/_apis/wit/workitems/{workItemId}?api-version={ApiVersion}";

    var patchDoc = fields.Select(kv => new
    {
        op = "replace",
        path = $"/fields/{kv.Key}",
        value = kv.Value
    }).ToArray();

    var request = new HttpRequestMessage(HttpMethod.Patch, url)
    {
        Content = new StringContent(JsonSerializer.Serialize(patchDoc), Encoding.UTF8, "application/json-patch+json")
    };

    var response = await _httpClient.SendAsync(request);

    await EnsureSuccessStatusCodeAsync(response, $"Alanlar güncellenemedi. WorkItem ID: {workItemId}");
}



        private async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, string errorMessage)
        {
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync();
                _logger.LogError("💥 {ErrorMessage}. Status: {StatusCode}, Details: {Details}", errorMessage, response.StatusCode, details);
                throw new HttpRequestException($"{errorMessage}. Status: {response.StatusCode}");
            }
        }
    }
}
