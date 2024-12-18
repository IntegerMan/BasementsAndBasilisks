using System.Text;
using MattEland.DigitalDungeonMaster.Agents.GameMaster;
using MattEland.DigitalDungeonMaster.Agents.WorldBuilder;
using MattEland.DigitalDungeonMaster.Agents.WorldBuilder.Models;
using MattEland.DigitalDungeonMaster.Services;
using MattEland.DigitalDungeonMaster.Shared;
using MattEland.DigitalDungeonMaster.WebAPI.Models;
using Newtonsoft.Json;

namespace MattEland.DigitalDungeonMaster.WebAPI.Services;

public class ChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly AppUser _user;
    private readonly IServiceProvider _services;
    private readonly IFileStorageService _storage;
    private readonly AgentConfigurationService _agentConfigService;
    private readonly RequestContextService _context;

    public ChatService(ILogger<ChatService> logger,
        AppUser user,
        IServiceProvider services,
        IFileStorageService storage,
        AgentConfigurationService agentConfigService,
        RequestContextService context)
    {
        _logger = logger;
        _user = user;
        _services = services;
        _storage = storage;
        _agentConfigService = agentConfigService;
        _context = context;
    }

    public async Task<ChatResult> ChatAsync(AdventureInfo adventure, ChatRequest request)
    {
        // Store context
        _context.CurrentUser = _user.Name;
        _context.CurrentAdventure = adventure;
        
        // Log the request
        _logger.LogInformation("{User} to {Bot}: {Message}", _user.Name, request.RecipientName, request.Message);
        if (request.History is null || !request.History.Any())
        {
            _logger.LogWarning("No history was provided in the request");
        }
        
        // Initialize the agent
        AgentConfig config = _agentConfigService.GetAgentConfiguration(request.RecipientName ?? "Game Master");
        GameMasterAgent agent = _services.GetRequiredService<GameMasterAgent>();
        await LoadGameMasterPromptAsync(adventure, config);
        agent.Initialize(_services, config);

        // Chat
        return await SendChatAsync(agent, request);
    }

    private async Task LoadGameMasterPromptAsync(AdventureInfo adventure, AgentConfig config)
    {
        // Load any contextual prompt information
        StringBuilder promptBuilder = new();
        await AddStoryDetailsToPromptBuilderAsync(adventure, promptBuilder);
        if (adventure.Status == AdventureStatus.InProgress)
        {
            await AddRecapToPromptBuilderAsync(adventure, promptBuilder);
        }
        config.AdditionalPrompt = promptBuilder.ToString();
    }
    
    private async Task LoadWorldBuilderPromptAsync(AdventureInfo adventure, AgentConfig config)
    {
        // Load any contextual prompt information
        StringBuilder promptBuilder = new();
        config.AdditionalPrompt = promptBuilder.ToString();
    }

    public async Task<ChatResult> StartChatAsync(AdventureInfo adventure)
    {
        // Store context
        _context.CurrentUser = _user.Name;
        _context.CurrentAdventure = adventure;

        // Assign an ID
        Guid chatId = Guid.NewGuid();
        _logger.LogInformation("Chat {Id} started with {User} in adventure {Adventure}", chatId, _user.Name,
            adventure.Name);
        
        // Initialize the agent
        AgentConfig config = _agentConfigService.GetAgentConfiguration("Game Master");
        GameMasterAgent agent = _services.GetRequiredService<GameMasterAgent>();
        await LoadGameMasterPromptAsync(adventure, config);
        agent.Initialize(_services, config);

        // Make the initial request
        ChatRequest request = new ChatRequest
        {
            User = _user.Name,
            RecipientName = agent.Name,
            Message = (adventure.Status == AdventureStatus.New) switch
            {
                true => config.NewCampaignPrompt ?? throw new InvalidOperationException("No new campaign prompt found"),
                false => config.ResumeCampaignPrompt ?? throw new InvalidOperationException("No resume campaign prompt found")
            }
        };

        return await SendChatAsync(agent, request);
    }

    private async Task<ChatResult> SendChatAsync(IChatAgent agent, ChatRequest request)
    {
        ChatResult result = await agent.ChatAsync(request, _user.Name);

        // Send the result back
        _logger.LogInformation("{Bot} to {User}: {Message}", agent.Name, _user.Name,
            result.Replies.Count() != 1
                ? "Multiple replies"
                : result.Replies.First().Message);

        return result;
    }

    private async Task AddRecapToPromptBuilderAsync(AdventureInfo adventure, StringBuilder promptBuilder)
    {
        string? recap = await _storage.LoadTextOrDefaultAsync("adventures", $"{adventure.Container}/Recap.md");
        if (string.IsNullOrWhiteSpace(recap))
        {
            _logger.LogWarning("No recap was found for the last session");
        }
        else
        {
            _logger.LogDebug("Session recap loaded: {Recap}", recap);

            promptBuilder.AppendLine("Here's a recap of the last session:");
            promptBuilder.AppendLine(recap);
        }
    }

    private async Task AddStoryDetailsToPromptBuilderAsync(AdventureInfo adventure, StringBuilder promptBuilder)
    {
        string settingsPath = $"{adventure.Container}/StorySetting.json";
        string? json = await _storage.LoadTextOrDefaultAsync("adventures", settingsPath);
        if (!string.IsNullOrWhiteSpace(json))
        {
            _logger.LogDebug("Settings found for adventure {Adventure} at {SettingsPath}", adventure, settingsPath);

            NewGameSettingInfo? setting = JsonConvert.DeserializeObject<NewGameSettingInfo>(json);
            if (setting is not null)
            {
                promptBuilder.AppendLine("The adventure description is " + setting.GameSettingDescription);
                promptBuilder.AppendLine("The desired gameplay style is " + setting.DesiredGameplayStyle);
                promptBuilder.AppendLine("The main character is " + setting.PlayerCharacterName + ", a " +
                                         setting.PlayerCharacterClass + ". " + setting.PlayerDescription);
                promptBuilder.AppendLine("The campaign objective is " + setting.CampaignObjective);
                if (adventure.Status == AdventureStatus.New)
                {
                    promptBuilder.AppendLine("The first session objective is " + setting.FirstSessionObjective);
                }
            }
        }
        else
        {
            _logger.LogWarning("No settings found for adventure {Adventure} at {SettingsPath}", adventure,
                settingsPath);
        }
    }

    public async Task<ChatResult> StartWorldBuilderChatAsync(AdventureInfo adventure)
    {
        // Store context
        _context.CurrentUser = _user.Name;
        _context.CurrentAdventure = adventure;

        // Assign an ID
        Guid chatId = Guid.NewGuid();
        _logger.LogInformation("World Builder Chat {Id} started with {User} in adventure {Adventure}", chatId, _user.Name,
            adventure.Name);
        
        // Initialize the agent
        AgentConfig config = _agentConfigService.GetAgentConfiguration("World Builder");
        WorldBuilderAgent agent = _services.GetRequiredService<WorldBuilderAgent>();
        await LoadWorldBuilderPromptAsync(adventure, config);
        agent.Initialize(_services, config);

        // Make the initial request
        ChatRequest request = new ChatRequest
        {
            User = _user.Name,
            RecipientName = agent.Name,
            Message = (adventure.Status == AdventureStatus.New) switch
            {
                true => config.NewCampaignPrompt ?? throw new InvalidOperationException("No new campaign prompt found"),
                false => config.ResumeCampaignPrompt ?? throw new InvalidOperationException("No resume campaign prompt found")
            }
        };

        return await SendChatAsync(agent, request);
    }
}