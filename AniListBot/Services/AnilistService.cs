using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AniListBot.Domain;
using Carlabs.Getit;
using Discord.WebSocket;
using MangaUpdater.Utils;
using Newtonsoft.Json;

namespace AniListBot.Services
{
    public class AnilistService
    {
        private readonly Getit _getit;
        private List<UserInfos> _users;

        private string _usersFilePath;

        public AnilistService()
        {
            Config config = new Config("https://graphql.anilist.co");
            _getit = new Getit(config);
        }

        public void Init(string usersFile)
        {
            _usersFilePath = usersFile;
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
                entries.Name("entries").Select("id").Select("status").Select("score(format: POINT_10)").Select("progress");
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
                entries.Name("entries").Select("id").Select("status").Select("score(format: POINT_10)").Select("progress");
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