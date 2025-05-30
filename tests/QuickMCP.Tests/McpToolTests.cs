using System.Text.Json;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using QuickMCP.Builders;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using Moq;
namespace QuickMCP.Tests;

public class McpToolTests
{
    [Fact]
    public async Task ShouldCallMcpTool_Get_WithQuery()
    {

        var mockServer = new Mock<IMcpServer>().Object;

        var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
            .FromUrl("https://petstore.swagger.io/v2/swagger.json").OnlyForPaths(["pet"]).BuildAsync();
        var tools = builder.GetMcpTools().ToList();

        //Query test
        var first = tools.FirstOrDefault(s => s.ProtocolTool.Name.Contains("status", StringComparison.OrdinalIgnoreCase));
        // Update the instantiation of RequestContext to include all required parameters based on its definition.
        var context = new RequestContext<CallToolRequestParams>(mockServer);
        context.Params = new CallToolRequestParams
        {
            Name = first.ProtocolTool.Name,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["status"] = JsonDocument.Parse("\"sold\"").RootElement
            }
        };


        var val = await first.InvokeAsync(context);

        val.ShouldNotBeNull();

        val.Content.ShouldNotBeNull();
        val.Content[0].Text.ShouldContain("sold");
    }

    // [Fact]
    // public async Task ShouldCallMcpTool_Get_WithPathParams()
    // {
    //     var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
    //         .FromConfiguration("C:\\Users\\Gunpa\\.quickmcp\\servers\\usStreetAddressApi\\config.json").BuildAsync();
    //     var tools = builder.GetMcpTools().ToList();
    //
    //     //Get with path params
    //     var second =
    //         tools.FirstOrDefault(s => s.ProtocolTool.Name.Contains("address", StringComparison.OrdinalIgnoreCase));
    //
    //     var secondResult = await second.InvokeAsync(new RequestContext<CallToolRequestParams>(null,
    //         new CallToolRequestParams()
    //         {
    //             Name = second.ProtocolTool.Name,
    //             Arguments = new Dictionary<string, JsonElement>()
    //             {
    //                 ["auth-id"] = JsonDocument.Parse("\"sample_auth_id\"").RootElement,
    //                 ["auth-token"] = JsonDocument.Parse("\"sample_auth_token\"").RootElement,
    //                 ["street"] = JsonDocument.Parse("\"1234 Maple Grove Lane\"").RootElement,
    //                 ["city"] = JsonDocument.Parse("\"Springfield\"").RootElement,
    //                 ["state"] = JsonDocument.Parse("\"IL\"").RootElement,
    //                 ["zipcode"] = JsonDocument.Parse("\"62701\"").RootElement,
    //                 ["match"] = JsonDocument.Parse("\"invalid\"").RootElement
    //             }
    //         }));
    //
    //     secondResult.Content.ShouldNotBeNull();
    //     secondResult.Content[0].Text.ShouldContain("status");
    // }

    [Fact]
    public async Task ShouldCallMcpTool_Post_WithBody()
    {
        var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
            .FromUrl("https://petstore.swagger.io/v2/swagger.json").OnlyForPaths(["pet"]).BuildAsync();
        var tools = builder.GetMcpTools().ToList();

        //Post
        var third = tools.FirstOrDefault(s => s.ProtocolTool.Name.Contains("addPet", StringComparison.OrdinalIgnoreCase));
        var mockServer = new Mock<IMcpServer>().Object;
        var context = new RequestContext<CallToolRequestParams>(mockServer);
        context.Params = new CallToolRequestParams()
        {
            Name = third.ProtocolTool.Name,
            Arguments = new Dictionary<string, JsonElement>()
            {
                ["body"] = JsonDocument
                        .Parse(
                            "{\n    \"id\": 12345,\n    \"category\": {\n      \"id\": 1,\n      \"name\": \"Dogs\"\n    },\n    \"name\": \"Buddy\",\n    \"photoUrls\": [\n      \"http://example.com/buddy1.jpg\",\n      \"http://example.com/buddy2.jpg\"\n    ],\n    \"tags\": [\n      {\n        \"id\": 10,\n        \"name\": \"friendly\"\n      },\n      {\n        \"id\": 11,\n        \"name\": \"playful\"\n      }\n    ],\n    \"status\": \"available\"\n }")
                        .RootElement
            }
        };

        var thirdResult = await third.InvokeAsync(context);

        thirdResult.Content.ShouldNotBeNull();
        thirdResult.Content[0].Text.ShouldContain("Buddy");
    }

    [Fact]
    public async Task ShouldCallMcpTool_Post_WithBody2()
    {
        // var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
        //     .FromUrl("https://petstore.swagger.io/v2/swagger.json").OnlyForPaths(["pet","order"]).BuildAsync();
        // var tools = builder.GetMcpTools().ToList();
        //
        // //Post
        // var third = tools.FirstOrDefault(s=>s.ProtocolTool.Name.Contains("placeOrder", StringComparison.OrdinalIgnoreCase));
        //
        // var thirdResult = await third.InvokeAsync(new RequestContext<CallToolRequestParams>(null, new CallToolRequestParams()
        // {
        //     Name = third.ProtocolTool.Name,
        //     Arguments = new Dictionary<string, JsonElement>()
        //     {
        //         ["body"] =  JsonDocument.Parse("{\n    \"id\": 1,\n    \"petId\": 9223372036854776000,\n    \"status\": \"placed\",\n    \"quantity\": 1\n, \"shipDate\": \"2025-04-06T19:00:04.767Z\",\"complete\": true  }").RootElement
        //     }
        // } ));
        //
        // thirdResult.Content.ShouldNotBeNull();
        // thirdResult.Content[0].Text.ShouldContain("Buddy");
    }

    [Fact]
    public async Task ShouldCallMcpTool_Put_WithBody()
    {
        var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
            .FromUrl("https://petstore.swagger.io/v2/swagger.json").OnlyForPaths(["pet"]).BuildAsync();
        var tools = builder.GetMcpTools().ToList();

        //Put
        var third = tools.FirstOrDefault(s => s.ProtocolTool.Name.Contains("updatePet", StringComparison.OrdinalIgnoreCase));
        var mockServer = new Mock<IMcpServer>().Object;
        var context = new RequestContext<CallToolRequestParams>(mockServer);
        context.Params = new CallToolRequestParams()
        {
            Name = third.ProtocolTool.Name,
            Arguments = new Dictionary<string, JsonElement>()
            {
                ["petId"] = JsonDocument.Parse("9876").RootElement,
                ["body"] = JsonDocument.Parse("{\n    \"id\": 9876,\n    \"name\": \"Sparky\",\n    \"status\": \"available\"\n  }").RootElement
            }
        };

        var thirdResult = await third.InvokeAsync(context);

        thirdResult.Content.ShouldNotBeNull();
        thirdResult.Content[0].Text.ShouldContain("Sparky");
    }

    [Fact]
    public async Task ShouldCallMcpTool_OpenAlex_WithBody()
    {
        var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
            .FromFile("specs/openalex.json").BuildAsync();
        var tools = builder.GetMcpTools().ToList();

        
        //Put
        var third = tools.FirstOrDefault(s => s.ProtocolTool.Name.Contains("getAuthors", StringComparison.OrdinalIgnoreCase));
        var mockServer = new Mock<IMcpServer>().Object;
        var context = new RequestContext<CallToolRequestParams>(mockServer);
        context.Params = new CallToolRequestParams()
        {
            Name = third.ProtocolTool.Name,
            Arguments = new Dictionary<string, JsonElement>()
            {
                ["petId"] = JsonDocument.Parse("9876").RootElement,
                ["body"] = JsonDocument
                        .Parse(
                            "{\n  \"mailto\": \"user@example.com\",\n  \"per_page\": 10,\n  \"User-Agent\": \"Claude\"\n}")
                        .RootElement
            }
        };

        var thirdResult = await third.InvokeAsync(context);

        thirdResult.Content.ShouldNotBeNull();
        thirdResult.Content[0].Text.ShouldContain("db_response_time_ms");
    }
    // [Fact]
    // public async Task ShouldCallMcpTool_Delete_WithBody()
    // {
    //     var builder = await McpServerInfoBuilder.ForOpenApi("Test_Server")
    //         .FromUrl("https://petstore.swagger.io/v2/swagger.json").OnlyForPaths(["pet"]).BuildAsync();
    //     var tools = builder.GetMcpTools().ToList();
    //     
    //     //Put
    //     var third = tools.FirstOrDefault(s=>s.ProtocolTool.Name.Contains("deletePet", StringComparison.OrdinalIgnoreCase));
    //     
    //     var thirdResult = await third.InvokeAsync(new RequestContext<CallToolRequestParams>(null, new CallToolRequestParams()
    //     {
    //         Name = third.ProtocolTool.Name,
    //         Arguments = new Dictionary<string, JsonElement>()
    //         {
    //             ["petId"] =  JsonDocument.Parse("9876").RootElement,
    //         }
    //     } ));
    //     
    //     thirdResult.Content.ShouldNotBeNull();
    //     thirdResult.Content[0].Text.ShouldContain("Sparky");
    // }
}