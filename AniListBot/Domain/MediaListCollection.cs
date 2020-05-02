using System.Collections.Generic;

namespace AniListBot.Domain
{
    public class AniListMediaListCollection
    {
        public bool HasNextChunk { get; set; }
        public List<EntryList> Lists { get; set; }
    }

    public class EntryList
    {
        public List<AniListMediaList> Entries { get; set; }
    }
}