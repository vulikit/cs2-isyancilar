using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace cs2_isyancilar
{
    public class IsyancilarConfig : BasePluginConfig
    {
        [JsonPropertyName("Prefix")]
        public string Prefix { get; set; } = "{blue}⌈ Isyancilar ⌋ ";

        [JsonPropertyName("Database")]
        public Dictionary<string, string> Database { get; set; } = new Dictionary<string, string>() {
            { "host", string.Empty },
            { "port", "3306" },
            { "user", string.Empty },
            { "password", string.Empty },
            { "name", string.Empty }
        };

        [JsonPropertyName("BicakPoint")]
        public int BicakPoint { get; set; } = 1;

        [JsonPropertyName("NormalKillPoint")]
        public int NormalKillPoint { get; set; } = 1;

        [JsonPropertyName("HeadShotPoint")]
        public int HeadShotPoint { get; set; } = 1;

        [JsonPropertyName("NoScopePoint")]
        public int NoScopePoint { get; set; } = 1;

        [JsonPropertyName("BlindPoint")]
        public int BlindPoint { get; set; } = 1;
    }
}
