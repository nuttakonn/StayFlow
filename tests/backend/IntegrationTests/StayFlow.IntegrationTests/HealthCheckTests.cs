using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StayFlow.IntegrationTests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnOkWithHealthyStatus()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(content.TryGetProperty("status", out var statusProp));
        Assert.Equal("Healthy", statusProp.GetString());

        Assert.True(content.TryGetProperty("service", out var serviceProp));
        Assert.Equal("StayFlow.Api", serviceProp.GetString());

        Assert.True(content.TryGetProperty("deploymentMode", out var modeProp));
        Assert.Equal("SaaS", modeProp.GetString());
    }

    [Fact]
    public async Task RootEndpoint_ShouldReturnOkWithApiMetadata()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(content.TryGetProperty("name", out var nameProp));
        Assert.Equal("StayFlow PMS API", nameProp.GetString());
    }

    [Fact]
    public async Task SwaggerEndpoint_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiEndpoint_ShouldReturnValidJson()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonString = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(jsonString);
        Assert.True(json.TryGetProperty("openapi", out var openapiProp));
        Assert.StartsWith("3.", openapiProp.GetString()!);

        // Save generated API spec to docs/api/openapi-v1.json
        var docDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../docs/api"));
        if (!Directory.Exists(docDir))
        {
            Directory.CreateDirectory(docDir);
        }
        var filePath = Path.Combine(docDir, "openapi-v1.json");
        await File.WriteAllTextAsync(filePath, jsonString);
    }
}
