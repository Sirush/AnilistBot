using System.Threading.Tasks;
using AniListBot.Services;
using Discord;
using Discord.Commands;

namespace AniListBot.Modules
{
    public class AnilistModule : ModuleBase<SocketCommandContext>
    {
        private readonly AnilistService _anilist;

        public AnilistModule(AnilistService anilist)
        {
            _anilist = anilist;
        }

        [Command("adduser")]
        [Alias("add")]
        public async Task AddUser(string username)
        {
            await ReplyAsync(await _anilist.AddUser(Context.User, username));
        }

        [Command("remove")]
        [Alias("delete")]
        public async Task RemoveSelf()
        {
            await ReplyAsync(await _anilist.RemoveUser(Context.User));
        }

        [Command("anilist")]
        [Alias("link", "ani")]
        public Task GetAnilist(IUser user)
        {
            var link = _anilist.GetUserLink(user.Id);

            if (link == null)
                return
                    ReplyAsync($"Sorry {Context.Message.Author.Mention}, the user **{user.Username}** hasn't added his account yet. You can add your account by doing $add AniListUserName.");

            return ReplyAsync($"https://anilist.co/user/{link}/");
        }

        [Command("help")]
        [Alias("info")]
        public Task Help()
        {
            return ReplyAsync("**$add AnilistUsername** - link your account to your anilist profile\n" +
                              "**$remove** - remove the link to your anilist profile if you've made a mistake\n" +
                              "**$anilist** @someone - display someone anilist profile link\n" +
                              "**$help** - display this");
        }
    }
}