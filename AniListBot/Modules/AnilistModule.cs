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

        [Command("anilist")]
        public Task GetAnilist(IUser user)
        {
            //_anilist.ScrapUserList("blabla", "sirush");
            return ReplyAsync($"Test custom prefix");
        }
    }
}