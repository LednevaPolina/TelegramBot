using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI.ChatGpt;
using OpenAI.ChatGpt.AspNetCore;
using OpenAI.ChatGpt.AspNetCore.Models;
using OpenAI.ChatGpt.EntityFrameworkCore.Extensions;
using OpenAI.ChatGpt.Models;
using OpenAI.ChatGpt.Models.ChatCompletion.Messaging;
using System;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var token = Environment.GetEnvironmentVariable("TG_BOT_TOKEN_ONLINE_ASSISTENT");
var botClient = new TelegramBotClient(token);

var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
await using var serviceProvider = CreateServiceProvider(
    openAiKey,
    initialMessage: "You are ChatGPT helpful assistant worked inside Telegram.",
    maxTokens: 300,
    host: "https://api.pawan.krd/v1/" //delete this line if you use default openAI host
);

using CancellationTokenSource cts = new();

// StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
};

botClient.StartReceiving(
    updateHandler: HandleUpdateAsync,
    pollingErrorHandler: HandlePollingErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

var me = await botClient.GetMeAsync();

Console.WriteLine($"Start listening for @{me.Username}");
Console.ReadLine();

// Send cancellation request to stop bot
cts.Cancel();

ServiceProvider CreateServiceProvider(
    string openaikey, string initialMessage, int maxTokens, string? host = null)
{
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddOptions<OpenAICredentials>()
        .Configure(cred =>
        {
            cred.ApiKey = openaikey;
            if (host is not null) cred.ApiHost = host;
        });
    services.AddOptions<ChatGPTConfig>()
        .Configure(config =>
        {
            config.InitialSystemMessage = initialMessage;
            config.MaxTokens = maxTokens;
        });
    services.AddChatGptEntityFrameworkIntegration(
        options => options.UseSqlite("Data Source=dialogs.db"));
    services.RemoveAll<ChatGPTFactory>();
    services.AddTransient<ChatGPTFactory>();
    return services.BuildServiceProvider();
}


async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    // Only process Message updates: https://core.telegram.org/bots/api#message
    if (update.Message is null) return;
    Message message= update.Message;
    // Only process text messages
    if (message.Text is not { } messageText)
        return;

    long chatId = message.Chat.Id;
    Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
    var typingTask = botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);
    var chatGptFactory = serviceProvider.GetRequiredService<ChatGPTFactory>();
    var chatGpt = await chatGptFactory.Create(message.Chat.Id.ToString(), cancellationToken: cancellationToken);
    var chat = await chatGpt.ContinueOrStartNewTopic(cancellationToken);
    var response = await chat.GetNextMessageResponse(messageText, cancellationToken: cancellationToken);
    await typingTask;
    _ = await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: response,
                cancellationToken: cancellationToken);
}

Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    var errorMessage = exception switch
    {
        ApiRequestException apiRequestException
            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        _ => exception.ToString()
    };

    Console.WriteLine(errorMessage);
    return Task.CompletedTask;
}




