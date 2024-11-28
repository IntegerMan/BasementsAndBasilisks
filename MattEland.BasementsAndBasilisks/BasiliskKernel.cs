﻿using System.ClientModel;
using MattEland.BasementsAndBasilisks.Blocks;
using MattEland.BasementsAndBasilisks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Core;

namespace MattEland.BasementsAndBasilisks;

public class BasiliskKernel : IDisposable
{
    private readonly Kernel _kernel;
    private readonly OpenAIPromptExecutionSettings _executionSettings;
    private readonly IChatCompletionService _chat;
    private readonly ChatHistory _history;
    private bool _disposedValue;

    private readonly Logger _logger;
    private readonly RequestContextService _context;

    public BasiliskKernel(IServiceProvider services, 
        string openAiDeploymentName, 
        string openAiEndpoint,
        string openAiApiKey, string logPath)
    {
        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(openAiDeploymentName,
            openAiEndpoint,
            openAiApiKey);

        _logger = new LoggerConfiguration()
           .MinimumLevel.Verbose()
           .WriteTo.File(new CompactJsonFormatter(), path: logPath)
           .CreateLogger();

        builder.Services.AddLogging(s => s.AddSerilog(_logger, dispose: true));

        _kernel = builder.Build();

        // Set execution settings
        _executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        // Set up services
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = new ChatHistory();
        _history.AddSystemMessage("""
        You are a dungeon master directing play of a game called Basements and Basilisks. 
        The user represents the only player in the game. Let the player make their own decisions, 
        ask for skill checks and saving rolls when needed, and call functions to get your responses as needed.
        Feel free to use markdown in your responses, but avoid lists.
        Ask the player what they'd like to do, but avoid railroading them or nudging them too much.
        """);

        // Add Plugins
        _kernel.RegisterBasiliskPlugins(services);
        _context = services.GetRequiredService<RequestContextService>();
    }

    public async Task<ChatResult> ChatAsync(string message)
    {
        _logger.Information("{Agent}: {Message}", "User", message);
        _history.AddUserMessage(message);
        _context.BeginNewRequest(message);
        
        List<FunctionCallContent> allCalls = new();


        try
        {
            ChatMessageContent result = await _chat.GetChatMessageContentAsync(_history, _executionSettings, _kernel);
            _history.Add(result);

            _logger.Information("{Agent}: {Message}", "User", message);

            FunctionCallContent[] calls = FunctionCallContent.GetFunctionCalls(result).ToArray();
            while (calls.Length > 0)
            {
                allCalls.AddRange(calls);

                foreach (var call in calls)
                {
                    FunctionResultContent funcResult = await call.InvokeAsync(_kernel);
                    _history.Add(funcResult.ToChatMessage());
                }

                result = await _chat.GetChatMessageContentAsync(_history, _executionSettings, _kernel);
                _history.Add(result);

                calls = FunctionCallContent.GetFunctionCalls(result).ToArray();
            }

            _context.AddBlock(new MessageBlock
            {
                Message = result.Content ?? "I'm afraid I can't respond to that right now",
                IsUserMessage = false
            });

            return new ChatResult
            {
                Message = result.Content ?? "I'm afraid I can't respond to that right now",
                Blocks = _context.Blocks,
                FunctionsCalled = allCalls.Select(c => $"{c.PluginName}:{c.FunctionName}")
            };
        }
        catch (HttpOperationException ex)
        {
            string error;
            if (ex.InnerException is ClientResultException && ex.Message.Contains("content management", StringComparison.OrdinalIgnoreCase)) 
            {
                error = "I'm afraid that message is a bit too spicy for what I'm allowed to process. Can you try something else?";
            }
            else
            {
                error = $"Could not handle your request: {ex.Message}";
            }

            return new ChatResult
            {
                Message = error,
                Blocks = _context.Blocks,
                FunctionsCalled = allCalls.Select(c => $"{c.PluginName}:{c.FunctionName}")
            };
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _logger.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}