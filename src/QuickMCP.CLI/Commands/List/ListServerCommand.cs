using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickMCP.CLI.Commands.List;

[Description("List available servers")]
public class ListServerCommand : AsyncCommand<ListServerCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ListServerCommandSettings settings)
    {
        var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".quickmcp");
        var serverFolder = Path.Combine(localFolder, "servers");

        if(!Directory.Exists(serverFolder))
            Directory.CreateDirectory(serverFolder);
        var servers = Directory.GetDirectories(serverFolder);

        int count = 0;
        var table2 = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Server ID[/]")
            .AddColumn("[yellow]Server Name[/]");
            //.AddColumn("[yellow]Description[/]");
        
        foreach (var serve in servers)
        {
            var file = Path.Combine(serve, "server_info.json");
            if (File.Exists(file))
            {
                var fileContent = await File.ReadAllTextAsync(file);
                var info = JsonSerializer.Deserialize(fileContent,
                    QuickMcpJsonSerializerContext.Default.ServerConfiguration);

                if (info != null)
                {
                    
                    var id = Path.GetFileName(serve);
                    if (!string.IsNullOrEmpty(settings.Filter))
                    {
                        if(!id.Contains(settings.Filter, StringComparison.OrdinalIgnoreCase) 
                           && (info.Description!=null && !info.Description.Contains(settings.Filter,StringComparison.InvariantCultureIgnoreCase)) 
                           && (info.ServerName!=null && !info.ServerName.Contains(settings.Filter, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }
                    
                    table2.AddRow(
                        $"[green]{id.Replace("[","[[").Replace("]","]]")}[/]",
                        $"[cyan]{info.ServerName.Replace("[","[[").Replace("]","]]")}[/]"
                        //$"[cyan]{info.Description.Replace("[","[[").Replace("]","]]")}[/]"
                    );
                    count++;
                }
            }
        }
        if(count>0)
            AnsiConsole.Write(table2);
        AnsiConsole.MarkupLine($"[bold][green]{count}[/] servers found.[/]");
        return count;
    }
}