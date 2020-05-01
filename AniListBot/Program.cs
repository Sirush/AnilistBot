using System;
using System.IO;
using System.Threading.Tasks;
using AniListBot.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AniListBot
{
    class Program
    {
        static void Main(string[] args)
            => new Program().MainAsync(args[0]).GetAwaiter().GetResult();

        private DiscordSocketClient _client;
        private IConfiguration _config;

        public async Task MainAsync(string config)
        {
            _client = new DiscordSocketClient();
            _config = BuildConfig(config);


            var services = ConfigureServices();
            services.GetRequiredService<CommandService>().Log += LogAsync;

            await _client.LoginAsync(TokenType.Bot, _config["token"]);
            await _client.StartAsync();

            await services.GetRequiredService<CommandHandlingService>().InitializeAsync(_config["prefix"]);
            services.GetRequiredService<AnilistService>().Init(_config["userfile"]);

            await Task.Delay(-1);
        }

        private IServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                   // Base
                   .AddSingleton(_client)
                   .AddSingleton<CommandService>()
                   .AddSingleton<CommandHandlingService>()
                   .AddSingleton<AnilistService>()
                   // Extra
                   .AddSingleton(_config)
                   // Add additional services here...
                   .BuildServiceProvider();
        }

        private IConfiguration BuildConfig(string config)
        {
            return new ConfigurationBuilder()
                   .SetBasePath(config)
                   .AddJsonFile("settings.json")
                   .Build();
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());

            return Task.CompletedTask;
        }
    }
}