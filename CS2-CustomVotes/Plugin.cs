using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CS2_CustomVotes.Factories;
using CS2_CustomVotes.Services;
using CS2_CustomVotes.Shared;
using CSSharpUtils.Extensions;
using MenuManagerAPI.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CS2_CustomVotes;

[MinimumApiVersion(191)]
public class CustomVotes : BasePlugin, IPluginConfig<CustomVotesConfig>
{
    public override string ModuleName => "Custom Votes";
    public override string ModuleDescription => "Allows you to create custom votes for your server.";
    public override string ModuleVersion => "1.1.4";
    public override string ModuleAuthor => "imi-tat0r";
    
    public CustomVotesConfig Config { get; set; } = null!;
    
    private readonly ILogger<CustomVotes> _logger;
    private readonly IServiceProvider _serviceProvider;

    private static PluginCapability<ICustomVoteApi> CustomVoteCapability { get; } = new("custom_votes:api");
    private static PluginCapability<IMenuAPI?> MenuManagerCapability { get; } = new("menu:api");

    public IMenuAPI? MenuManagerApi { get; set; }

    public CustomVotes(ILogger<CustomVotes> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        
        _logger.LogInformation("[CustomVotes] Registering custom vote API");
        var customVoteApi = _serviceProvider.GetRequiredService<ICustomVoteApi>();
        Capabilities.RegisterPluginCapability(CustomVoteCapability, () => customVoteApi);
        
        _logger.LogInformation("[CustomVotes] Registering event handlers");
        var voteManager = _serviceProvider.GetRequiredService<IVoteManager>();
        RegisterEventHandler<EventPlayerConnectFull>(voteManager.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(voteManager.OnPlayerDisconnect);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        MenuManagerApi = MenuManagerCapability.Get();
        if (MenuManagerApi == null)
        {
            _logger.LogWarning("[CustomVotes] MenuManagerAPI not found - votes will use chat/center menus only");
        }
        else
        {
            _logger.LogDebug("[CustomVotes] MenuManagerAPI found");
        }

    }

    public void OnConfigParsed(CustomVotesConfig config)
    {
        Config = config;
        
        var voteManager = _serviceProvider.GetRequiredService<IVoteManager>();

        foreach (var customVote in Config.CustomVotes)
            voteManager.AddVote(customVote);
        
    }
    
    [ConsoleCommand("css_reload_cfg", "Reload the config in the current session without restarting the server")]
    [RequiresPermissions("@css/generic")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnReloadConfigCommand(CCSPlayerController? player, CommandInfo info)
    {
        try
        {
            OnConfigParsed(new CustomVotesConfig().Reload());
        }
        catch (Exception e)
        {
            info.ReplyToCommand($"[CustomVotes] Failed to reload config: {e.Message}");
        }
    }
}

public class CustomVotesServiceCollection : IPluginServiceCollection<CustomVotes>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton(this);
        serviceCollection.AddSingleton<IVoteManager, VoteManager>();
        serviceCollection.AddSingleton<ICustomVoteApi, CustomVoteApi>();
        serviceCollection.AddScoped<IActiveVoteFactory, ActiveVoteFactory>();
        
        serviceCollection.AddLogging(options =>
        {
            options.AddConsole();
        });
    }
}