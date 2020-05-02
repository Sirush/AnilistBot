using System.Collections.Generic;
using Discord.Rest;

namespace AniListBot.Domain
{
    public class MessageInfo
    {
        public ulong RequestAuthor { get; set; }
        public int CurrentPage { get; set; }
        public List<UserMediaInfo> MediaInfos { get; set; }
        public RestUserMessage Message { get; set; }
        public AniListMedia Media { get; set; }
        public string MediaLink { get; set; }
    }
}