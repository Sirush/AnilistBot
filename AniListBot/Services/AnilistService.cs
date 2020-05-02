using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AniListBot.Domain;
using Carlabs.Getit;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using MangaUpdater.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace AniListBot.Services
{
    public class AnilistService
    {
        private readonly Getit _getit;
        private List<UserInfos> _users;

        private readonly DiscordSocketClient _discord;
        private readonly IServiceProvider _services;

        private string _usersFilePath;
        private string _regex;
        private Dictionary<ulong, MessageInfo> _messages;

        private const int PER_PAGE = 3;


        public AnilistService(IServiceProvider services)
        {
            _discord = services.GetRequiredService<DiscordSocketClient>();

            Config config = new Config("https://graphql.anilist.co");
            _getit = new Getit(config);
        }

        public void Init(string usersFile)
        {
            _usersFilePath = usersFile;
            _messages = new Dictionary<ulong, MessageInfo>();
            if (File.Exists(usersFile))
                _users = JsonConvert.DeserializeObject<List<UserInfos>>(File.ReadAllText(usersFile));
            else
                _users = new List<UserInfos>();

            Task.Run(() => Save(true));
            Task.Run(() => Scrap(true));
        }

        private async void Save(bool loop = false)
        {
            while (true)
            {
                File.WriteAllText(_usersFilePath, JsonConvert.SerializeObject(_users));

                if (!loop)
                    return;

                await Task.Delay(1000 * 120);
            }
        }

        private async void Scrap(bool loop = false)
        {
            while (true)
            {
                for (var i = 0; i < _users.Count; i++)
                {
                    var user = _users[i];
                    await ScrapUserList(user);
                    await Task.Delay(30000);
                }

                if (!loop)
                    return;
            }
        }

        /// <summary>
        /// Gives a user anilist link if it exists
        /// </summary>
        /// <returns></returns>
        public string GetUserLink(ulong userId)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return null;

            return user.AnilistName;
        }

        /// <summary>
        /// Add a user and scrap his data
        /// </summary>
        public async Task<string> AddUser(SocketUser user, string username)
        {
            if (_users.Any(u => u.Id == user.Id))
                return $"You are already in the list {user.Mention}";

            if (!await UserExists(username))
                return $"The username {username} couldn't be found on Anilist, please check the spelling {user.Mention}";

            var newUser = new UserInfos {Id = user.Id, AnilistName = username, Name = user.Username};
            _users.Add(newUser);
            ScrapUserList(newUser);
            return "Successfully added to the list, the data could take a few minutes to load.";
        }

        public async Task<string> RemoveUser(SocketUser user)
        {
            if (_users.All(u => u.Id != user.Id))
                return $"Sorry, you are not in the list {user.Mention}";

            _users.RemoveAll(u => u.Id == user.Id);
            return $"{user.Mention}, you have been removed from my database";
        }

        /// <summary>
        /// Check an anime and send message if it exists
        /// </summary>
        public async void CheckMedia(SocketMessage sentMessage, string url, int mediaId)
        {
            IQuery query, title;

            List<UserMediaInfo> userMediaInfos = new List<UserMediaInfo>();

            query = _getit.Query();
            title = _getit.Query();
            title.Name("title").Select("romaji", "english", "native", "userPreferred");
            query.Name("Media").Where("id", mediaId).Select("id").Select("type").Select(title);
            try
            {
                var json = await _getit.Get<string>(query);
                if (json == null)
                    return;
                var media = JsonUtils.Deserialize<AniListMedia>(json, true);
                List<UserInfos> users;

                if (media.Type == AniListMediaType.ANIME)
                    users = _users.Where(u => u.Animes.Any(a => a.MediaId == media.Id)).ToList();
                else
                    users = _users.Where(u => u.Mangas.Any(a => a.MediaId == media.Id)).ToList();

                EmbedBuilder builder;

                if (users.Count <= 0)
                {
                    builder = new EmbedBuilder()
                              .WithTitle($"Who saw {media.Title.UserPreferred}")
                              .WithColor(new Color(0x252425))
                              .WithFooter(footer =>
                              {
                                  footer
                                      .WithText("2AniB by @Sirus#0721");
                              })
                              .WithDescription("Nobody saw this");
                }
                else
                {
                    foreach (var user in users)
                    {
                        AniListMediaList userMedia;
                        if (media.Type == AniListMediaType.ANIME)
                            userMedia = user.Animes.First(a => a.MediaId == media.Id);
                        else
                            userMedia = user.Mangas.First(a => a.MediaId == media.Id);
                        userMediaInfos.Add(new UserMediaInfo
                                           {
                                               Username = user.Name,
                                               Progress = userMedia.Progress,
                                               Score = userMedia.Score,
                                               Status = userMedia.Status
                                           });
                    }
                }

                builder = GetEmbed(userMediaInfos, media, url, 0);
                var embed = builder.Build();
                var message = await sentMessage.Channel.SendMessageAsync(null, embed: embed)
                                               .ConfigureAwait(false);
                await message.AddReactionAsync(new Emoji("\u2B05"));
                await message.AddReactionAsync(new Emoji("\u27A1"));
                await sentMessage.DeleteAsync();
                _messages.Add(message.Id,
                              new MessageInfo
                              {
                                  CurrentPage = 0,
                                  RequestAuthor = sentMessage.Author.Id,
                                  MediaInfos = userMediaInfos,
                                  Message = message,
                                  Media = media,
                                  MediaLink = url
                              });
            }
            catch (Exception e)
            {
                return;
            }
        }

        /// <summary>
        /// Process a reaction
        /// </summary>
        /// <param name="reaction"></param>
        /// <param name="cachedMessage"></param>
        /// <exception cref="NotImplementedException"></exception>
        public async Task ProcessReaction(SocketReaction reaction, IMessage cachedMessage)
        {
            if (_messages.Any(m => m.Key == cachedMessage.Id))
            {
                var message = _messages.First(m => m.Key == cachedMessage.Id);

                if (message.Value.RequestAuthor == reaction.UserId)
                {
                    //Left
                    if (Equals(reaction.Emote, new Emoji("\u2B05")))
                    {
                        if (message.Value.CurrentPage > 0)
                        {
                            message.Value.CurrentPage--;
                            var builder = GetEmbed(message.Value.MediaInfos, message.Value.Media, message.Value.MediaLink,
                                                   message.Value.CurrentPage);
                            var embed = builder.Build();
                            await message.Value.Message.ModifyAsync(properties => { properties.Embed = embed; });
                        }
                    }

                    //Right
                    else if (Equals(reaction.Emote, new Emoji("\u27A1")))
                    {
                        if (message.Value.CurrentPage < message.Value.MediaInfos.Count / PER_PAGE - 1)
                        {
                            message.Value.CurrentPage++;
                            var builder = GetEmbed(message.Value.MediaInfos, message.Value.Media, message.Value.MediaLink,
                                                   message.Value.CurrentPage);
                            var embed = builder.Build();
                            await message.Value.Message.ModifyAsync(properties => { properties.Embed = embed; });
                        }
                    }
                }

                await message.Value.Message.RemoveReactionAsync(reaction.Emote, reaction.UserId);
            }
        }

        private EmbedBuilder GetEmbed(List<UserMediaInfo> userMediaInfos, AniListMedia media, string url, int pagination)
        {
            var mediaInfos = userMediaInfos.Skip(pagination * PER_PAGE).Take(pagination + 1 * PER_PAGE);
            var builder = new EmbedBuilder()
                          .WithTitle($"Who saw {media.Title.UserPreferred}")
                          .WithUrl(url)
                          .WithColor(new Color(0x252425))
                          .WithFooter(footer =>
                          {
                              footer
                                  .WithText($"Page {pagination + 1}/{userMediaInfos.Count / PER_PAGE} - 2AniB by @Sirus#0721");
                          });

            foreach (var u in mediaInfos)
            {
                builder.AddField(u.Username, $"Score : **{u.Score}** ({u.Status} / Progress : **{u.Progress}**)", true);
            }

            return builder;
        }

        private async Task<bool> UserExists(string username)
        {
            IQuery query;

            query = _getit.Query();
            query.Name("User").Where("name", username).Select("id");
            try
            {
                var json = await _getit.Get<string>(query);
                if (json == null)
                    return false;

                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// Scrap a user anime and manga list
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="anilistName"></param>
        /// <returns></returns>
        public async Task ScrapUserList(UserInfos user)
        {
            user.Animes = await ScrapUserAnimes(user.AnilistName);
            user.Mangas = await ScrapUserMangas(user.AnilistName);
            //Save();
        }

        private async Task<List<AniListMediaList>> ScrapUserAnimes(string anilistName)
        {
            List<AniListMediaList> mediaEntries = new List<AniListMediaList>();

            IQuery query;
            IQuery select;
            IQuery entries;

            bool nextChunk = true;
            int currentChunk = 0;
            while (nextChunk)
            {
                EnumHelper currentType = new EnumHelper(AniListMediaType.ANIME.ToString());

                query = _getit.Query();
                select = _getit.Query();
                entries = _getit.Query();
                entries.Name("entries").Select("mediaId").Select("status").Select("score(format: POINT_10)").Select("progress");
                select.Name("lists").Select(entries);
                query.Name("MediaListCollection").Where("userName", anilistName).Where("type", currentType).Where("perChunk", 500)
                     .Where("chunk", currentChunk).Select("hasNextChunk").Select(select);

                var json = await _getit.Get<string>(query);
                var mediaCollection = JsonUtils.Deserialize<AniListMediaListCollection>(json, true);
                if (mediaCollection?.Lists != null && mediaCollection.Lists.Count > 0)
                {
                    foreach (var l in mediaCollection.Lists)
                    {
                        mediaEntries.AddRange(l.Entries);
                    }
                }
                else
                {
                    break;
                }

                nextChunk = mediaCollection.HasNextChunk;
                currentChunk++;
                await Task.Delay(5000);
            }

            return mediaEntries;
        }

        private async Task<List<AniListMediaList>> ScrapUserMangas(string anilistName)
        {
            List<AniListMediaList> mediaEntries = new List<AniListMediaList>();

            IQuery query;
            IQuery select;
            IQuery entries;

            bool nextChunk = true;
            int currentChunk = 0;
            while (nextChunk)
            {
                EnumHelper currentType = new EnumHelper(AniListMediaType.MANGA.ToString());

                query = _getit.Query();
                select = _getit.Query();
                entries = _getit.Query();
                entries.Name("entries").Select("mediaId").Select("status").Select("score(format: POINT_10)").Select("progress");
                select.Name("lists").Select(entries);
                query.Name("MediaListCollection").Where("userName", anilistName).Where("type", currentType).Where("perChunk", 500)
                     .Where("chunk", currentChunk).Select("hasNextChunk").Select(select);

                var json = await _getit.Get<string>(query);
                var mediaCollection = JsonUtils.Deserialize<AniListMediaListCollection>(json, true);
                if (mediaCollection?.Lists != null && mediaCollection.Lists.Count > 0)
                {
                    foreach (var l in mediaCollection.Lists)
                    {
                        mediaEntries.AddRange(l.Entries);
                    }
                }
                else
                {
                    break;
                }

                nextChunk = mediaCollection.HasNextChunk;
                currentChunk++;
                await Task.Delay(5000);
            }

            return mediaEntries;
        }
    }
}