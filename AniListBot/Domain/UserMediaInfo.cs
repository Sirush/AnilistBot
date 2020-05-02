using System.Net.NetworkInformation;

namespace AniListBot.Domain
{
    public class UserMediaInfo
    {
        public string Username { get; set; }
        public AniListMediaListStatus? Status { get; set; }
        public int? Progress { get; set; }
        public float? Score { get; set; }
    }
}