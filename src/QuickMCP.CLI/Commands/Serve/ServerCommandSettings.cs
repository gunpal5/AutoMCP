using System.ComponentModel;
using QuickMCP.Types;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickMCP.CLI.Commands.Serve;

public class ServerCommandSettings : CommandSettings
{
    [Description("The path to the configuration file.")]
    [CommandOption("-c|--config-path <CONFIG_PATH>")]
    public string? ConfigPath { get; set; }
    
    [Description("The environment variable name pointing to the configuration file.")]
    [CommandOption("-e|--config-env-var <CONFIG_ENV_VAR>")]
    public string? ConfigEnvVar { get; set; }
    
    [Description("The URL of the OpenAPI or Google Discovery specification.")]
    
    [CommandOption("-s|--spec-url <SPEC_URL>")]
    public string? SpecUrl{ get; set;}
    
    [Description("The Path of the OpenAPI or Google Discovery specification.")]
    
    [CommandOption("-p|--spec-path <SPEC_PATH>")]
    public string? SpecPath { get; set; }
    
    [Description("The name of the server to use.")]
    [CommandOption("-i|--server-id <SERVER_ID>")]
    public string? ServerId { get; set; }
    
    [Description("Base URL for the API.")]
    [CommandOption("-a|--api-base-url <API_BASE_URL>")]
    public string? ApiBaseUrl { get; set; }
    
    [Description("Enable verbose console logging.")]
    [CommandOption("-l|--logging")]
    public bool? EnableLogging { get; set; }

    [Description("Host Protocol.STDIO or HTTP.")]
    [CommandOption("-h|--host-protocol")]
    public string? HostProtocol { get; set; }


    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath) && string.IsNullOrWhiteSpace(ConfigEnvVar) && string.IsNullOrEmpty(SpecPath) && string.IsNullOrEmpty(SpecUrl) && string.IsNullOrEmpty(ServerId))
            return ValidationResult.Error("You must specify a Server name or configuration file or environment variable or a specification file or url.");
        
        if(!string.IsNullOrEmpty(ConfigPath) && !File.Exists(ConfigPath))
            return ValidationResult.Error($"The configuration file {ConfigPath} does not exist.");
        if (!string.IsNullOrEmpty(ConfigEnvVar))
        {
            var envVar = Environment.GetEnvironmentVariable(ConfigEnvVar);
            if (string.IsNullOrEmpty(envVar))
                return ValidationResult.Error($"The environment variable {ConfigEnvVar} does not exist.");
            if(!File.Exists(envVar))
                return ValidationResult.Error($"The configuration file {envVar} does not exist.");
        }
        if (!string.IsNullOrEmpty(SpecPath) && !File.Exists(SpecPath) )
            return ValidationResult.Error($"The specification file {SpecPath} does not exist.");
        
        
        
        return base.Validate();
    }

    public BuilderConfig GetBuilderConfig()
    {
        return new BuilderConfig()
        {
            ApiBaseUrl = ApiBaseUrl,
            ApiSpecPath = SpecPath,
            ServerName = ServerId,
            ApiSpecUrl = SpecUrl,
            HostProtocol= HostProtocol
        };
    }
}