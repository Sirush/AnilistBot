using System.Collections.Generic;

namespace AniListBot.Domain
{
    public class UserInfos
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string AnilistName { get; set; }
        public List<AniListMediaList> Animes { get; set; }
        public List<AniListMediaList> Mangas { get; set; }
    }
}