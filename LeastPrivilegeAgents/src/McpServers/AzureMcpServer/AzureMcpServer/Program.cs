using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// DefaultAzureCredential: uses env vars, managed identity, or az login — never write credentials
var credential = new DefaultAzureCredential();
var armClient = new ArmClient(credential);

app.MapGet("/.well-known/mcp", () => new
{
    name = "azure-readonly-mcp",
    version = "1.0.0",
    tools = new[] { "list_subscriptions", "list_resource_groups", "list_resources", "get_resource" }
});

app.MapPost("/tools/list_subscriptions", async () =>
{
    var subscriptions = new List<object>();
    await foreach (var sub in armClient.GetSubscriptions().GetAllAsync())
    {
        subscriptions.Add(new
        {
            subscriptionId = sub.Data.SubscriptionId,
            displayName = sub.Data.DisplayName,
            state = sub.Data.State?.ToString()
        });
    }
    return Results.Ok(new { subscriptions });
});

app.MapPost("/tools/list_resource_groups", async (ListResourceGroupsRequest req) =>
{
    var subscription = armClient.GetSubscriptionResource(
        SubscriptionResource.CreateResourceIdentifier(req.SubscriptionId));

    var groups = new List<object>();
    await foreach (var rg in subscription.GetResourceGroups().GetAllAsync())
    {
        groups.Add(new
        {
            name = rg.Data.Name,
            location = rg.Data.Location.ToString()
        });
    }
    return Results.Ok(new { resourceGroups = groups });
});

app.MapPost("/tools/list_resources", async (ListResourcesRequest req) =>
{
    var subscription = armClient.GetSubscriptionResource(
        SubscriptionResource.CreateResourceIdentifier(req.SubscriptionId));
    var rg = await subscription.GetResourceGroups().GetAsync(req.ResourceGroupName);

    var resources = new List<object>();
    await foreach (var resource in rg.Value.GetGenericResourcesAsync())
    {
        resources.Add(new
        {
            id = resource.Data.Id?.ToString(),
            name = resource.Data.Name,
            type = resource.Data.ResourceType.ToString(),
            location = resource.Data.Location.ToString()
        });
    }
    return Results.Ok(new { resources });
});

app.MapPost("/tools/get_resource", async (GetResourceRequest req) =>
{
    var resourceId = Azure.Core.ResourceIdentifier.Parse(req.ResourceId);
    var resource = await armClient.GetGenericResource(resourceId).GetAsync();
    return Results.Ok(new
    {
        id = resource.Value.Data.Id?.ToString(),
        name = resource.Value.Data.Name,
        type = resource.Value.Data.ResourceType.ToString(),
        location = resource.Value.Data.Location.ToString(),
        tags = resource.Value.Data.Tags
    });
});

app.Run();

record ListResourceGroupsRequest(string SubscriptionId);
record ListResourcesRequest(string SubscriptionId, string ResourceGroupName);
record GetResourceRequest(string ResourceId);
