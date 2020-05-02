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
        public bool IsUserInDatabase(ulong userId)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return false;

            return true;
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
            var authorId = sentMessage.Author.Id;
            var channel = sentMessage.Channel;
            await sentMessage.DeleteAsync();

            IQuery query, title, cover;

            List<UserMediaInfo> userMediaInfos = new List<UserMediaInfo>();

            query = _getit.Query();
            title = _getit.Query();
            cover = _getit.Query();

            cover.Name("coverImage").Select("large");
            title.Name("title").Select("romaji", "english", "native", "userPreferred");
            query.Name("Media").Where("id", mediaId).Select("id").Select("type").Select("description").Select("averageScore").Select(title)
                 .Select(cover);
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
                var message = await channel.SendMessageAsync(null, embed: embed)
                                           .ConfigureAwait(false);
                await message.AddReactionAsync(new Emoji("\u2B05"));
                await message.AddReactionAsync(new Emoji("\u27A1"));
                await message.AddReactionAsync(new Emoji("\u274C"));
                _messages.Add(message.Id,
                              new MessageInfo
                              {
                                  CurrentPage = 0,
                                  RequestAuthor = authorId,
                                  MediaInfos = userMediaInfos,
                                  Message = message,
                                  Media = media,
                                  MediaLink = url
                              });

                Task.Run(async () =>
                {
                    await Task.Delay(1000 * 900);
                    RemoveEmbed(message.Id);
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
                        if (message.Value.CurrentPage < MathF.Ceiling((float) message.Value.MediaInfos.Count / PER_PAGE) - 1)
                        {
                            message.Value.CurrentPage++;
                            var builder = GetEmbed(message.Value.MediaInfos, message.Value.Media, message.Value.MediaLink,
                                                   message.Value.CurrentPage);
                            var embed = builder.Build();
                            await message.Value.Message.ModifyAsync(properties => { properties.Embed = embed; });
                        }
                    }
                    //Delete
                    else if (Equals(reaction.Emote, new Emoji("\u274C")))
                    {
                        await RemoveEmbed(cachedMessage.Id);
                    }
                }

                await message.Value.Message.RemoveReactionAsync(reaction.Emote, reaction.UserId);
            }
        }

        private EmbedBuilder GetEmbed(List<UserMediaInfo> userMediaInfos, AniListMedia media, string url, int pagination)
        {
            bool showPage = MathF.Ceiling((float) userMediaInfos.Count / PER_PAGE) > 1;
            string footerText = "2AniB by @Sirus#0721";
            if (showPage)
                footerText = $"Page {pagination + 1}/{MathF.Ceiling((float) userMediaInfos.Count / PER_PAGE)} - 2AniB by @Sirus#0721";
            var nonZeroScores = userMediaInfos.Where(u => u.Score.Value != 0);
            float serverAverageScore = 0f;
            if (nonZeroScores != null && nonZeroScores.Any())
                serverAverageScore = nonZeroScores.Average(u => u.Score.Value);

            var description = media.Description.Trim().Replace("<br>", "");

            var mediaInfos = userMediaInfos.Skip(pagination * PER_PAGE).Take(pagination + 1 * PER_PAGE);
            var builder = new EmbedBuilder()
                          .WithAuthor($"AniList - Who has {(media.Type == AniListMediaType.ANIME ? "seen" : "read")}",
                                      "https://pbs.twimg.com/profile_images/1236103622636834816/5TFL-AFz_400x400.png")
                          .WithDescription($"{description.Substring(0, Math.Min(description.Length, 300))}")
                          .AddField("Average server score", $"**{serverAverageScore}**", true)
                          .AddField("Average anilist score", $"**{media.AverageScore / 10f}**", true)
                          .AddField("-", "-", true)
                          .WithThumbnailUrl(media.CoverImage.Large)
                          .WithTitle($"{media.Title.UserPreferred}")
                          .WithUrl(url)
                          .WithColor(new Color(0x252425))
                          .WithFooter(footer =>
                          {
                              footer
                                  .WithText(footerText);
                          });

            foreach (var u in mediaInfos)
            {
                builder.AddField(u.Username, $"Score : **{u.Score}** ({u.Status} / Progress : **{u.Progress}**)", true);
            }

            return builder;
        }

        public Embed GetUserEmbed(ulong userId)
        {
            var user = _users.First(u => u.Id == userId);

            var builder = new EmbedBuilder()
                          .WithAuthor($"AniList",
                                      "https://pbs.twimg.com/profile_images/1236103622636834816/5TFL-AFz_400x400.png")
                          .WithThumbnailUrl(user.AniListUser.Avatar.Large)
                          .WithTitle(user.AnilistName)
                          .WithUrl($"https://anilist.co/user/{user.AnilistName}/")
                          .WithColor(new Color(0x626944))
                          .AddField("Anime stats", $"Days wasted **{(user.AniListUser.Statistics.Anime.MinutesWatched / 1440f):0.0}**\r" +
                                                   $"Episodes watched **{user.AniListUser.Statistics.Anime.EpisodesWatched}**\r" +
                                                   $"Mean score **{(user.AniListUser.Statistics.Anime.MeanScore / 10f):0.0}**", true)
                          .AddField("Manga stats", $"Chapters read **{user.AniListUser.Statistics.Manga.ChaptersRead}**\r" +
                                                   $"Volumes read **{user.AniListUser.Statistics.Manga.VolumesRead}**\r" +
                                                   $"Mean score **{(user.AniListUser.Statistics.Manga.MeanScore / 10f):0.0}**", true)
                          .WithFooter(footer =>
                          {
                              footer
                                  .WithText("2AniB by @Sirus#0721");
                          });

            return builder.Build();
        }

        public async Task RemoveEmbed(ulong messageId)
        {
            var message = _messages.First(m => m.Key == messageId);
            await message.Value.Message.ModifyAsync(properties =>
            {
                properties.Content = $"<{message.Value.MediaLink}>";
                properties.Embed = null;
            });
            await message.Value.Message.RemoveAllReactionsAsync();

            _messages.Remove(messageId);
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
            user.AniListUser = await ScrapAnilistUser(user.AnilistName);
        }

        private async Task<AniListUser> ScrapAnilistUser(string anilistName)
        {
            IQuery query, statistics, anime, manga, avatar;

            query = _getit.Query();
            statistics = _getit.Query();
            anime = _getit.Query();
            manga = _getit.Query();
            avatar = _getit.Query();

            anime.Name("anime").Select("minutesWatched", "episodesWatched", "meanScore");
            manga.Name("manga").Select("chaptersRead", "volumesRead", "meanScore");
            statistics.Name("statistics").Select(anime).Select(manga);
            avatar.Name("avatar").Select("large");
            query.Name("User").Where("name", anilistName).Select(statistics).Select(avatar);

            var json = await _getit.Get<string>(query);
            return JsonUtils.Deserialize<AniListUser>(json, true);
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