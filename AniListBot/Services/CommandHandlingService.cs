using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace AniListBot.Services
{
    public class CommandHandlingService
    {
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _discord;
        private readonly IServiceProvider _services;
        private readonly AnilistService _anilist;

        private string _prefix;
        private string _regex;

        public CommandHandlingService(IServiceProvider services)
        {
            _commands = services.GetRequiredService<CommandService>();
            _discord = services.GetRequiredService<DiscordSocketClient>();
            _anilist = services.GetRequiredService<AnilistService>();
            _services = services;

            // Hook CommandExecuted to handle post-command-execution logic.
            _commands.CommandExecuted += CommandExecutedAsync;
            // Hook MessageReceived so we can process each message to see
            // if it qualifies as a command.
            _discord.MessageReceived += MessageReceivedAsync;
            _discord.ReactionAdded += ReactionAddedAsync;
        }

        public async Task InitializeAsync(string prefix, string regex)
        {
            // Register modules that are public and inherit ModuleBase<T>.
            _prefix = prefix;
            _regex = regex;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        public async Task MessageReceivedAsync(SocketMessage rawMessage)
        {
            // Ignore system messages, or messages from other bots
            if (!(rawMessage is SocketUserMessage message)) return;
            if (message.Source != MessageSource.User) return;

            // This value holds the offset where the prefix ends
            var argPos = 0;
            // Perform prefix check. You may want to replace this with

            if (message.Content.Contains("anilist"))
            {
                var match = Regex.Match(message.Content, _regex);
                if (match.Success)
                {
                    var parse = Int32.TryParse(match.Groups[2].Value, out var id);
                    if (parse)
                        _anilist.CheckMedia(rawMessage, match.Value,id);
                }
            }

            if (!message.HasCharPrefix(_prefix[0], ref argPos))
                return;
            // for a more traditional command format like !help.
            //if (!message.HasMentionPrefix(_discord.CurrentUser, ref argPos)) return;

            var context = new SocketCommandContext(_discord, message);
            // Perform the execution of the command. In this method,
            // the command service will perform precondition and parsing check
            // then execute the command if one is matched.
            await _commands.ExecuteAsync(context, argPos, _services);
            // Note that normally a result will be returned by this format, but here
            // we will handle the result in CommandExecutedAsync,
        }

        public async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            // command is unspecified when there was a search failure (command not found); we don't care about these errors
            if (!command.IsSpecified)
                return;

            // the command was successful, we don't care about this result, unless we want to log that a command succeeded.
            if (result.IsSuccess)
                return;

            // the command failed, let's notify the user that something happened.
            await context.Channel.SendMessageAsync($"error: {result}");
        }

        private async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel,
                                              SocketReaction reaction)
        {
            var cachedMessage = await reaction.Channel.GetMessageAsync(reaction.MessageId);
            if (reaction.UserId != _discord.CurrentUser.Id && cachedMessage.Author.Id == _discord.CurrentUser.Id)
            {
                await _anilist.ProcessReaction(reaction, cachedMessage);
            }
        }
    }
}