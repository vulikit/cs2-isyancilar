using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace cs2_isyancilar
{
    public class CS2Isyancilar : BasePlugin, IPluginConfig<IsyancilarConfig>
    {
        public override string ModuleName => "CS2 Isyancılar";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "Varkit";

        public IsyancilarConfig Config { get; set; } = new();
        public string Prefix { get; private set; } = string.Empty;
        private static string DatabaseConnectionString { get; set; } = string.Empty;
        private readonly Dictionary<CCSPlayerController, bool> _cooldowns = [];
        private bool _isyanAcik = true;
        private static readonly List<PlayerDetails> _playerList = [];

        public override void Load(bool hotReload)
        {
            SetupDB();
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        }

        public void OnConfigParsed(IsyancilarConfig config)
        {
            Config = config;
            Prefix = config.Prefix.ReplaceColorTags();
            DatabaseConnectionString = new MySqlConnectionStringBuilder
            {
                Server = config.Database["host"],
                Database = config.Database["name"],
                UserID = config.Database["user"],
                Password = config.Database["password"],
                Port = uint.Parse(config.Database["port"])
            }.ConnectionString;
        }

        private async void SetupDB()
        {
            using var connection = await ConnectAsync();
            if (connection == null)
            {
                Console.WriteLine("[IsyanPlugin] Veritabanı bağlantısı başarısız.");
                return;
            }

            try
            {
                await connection.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS `isyancilar` (
                        `playername` VARCHAR(128) NOT NULL,
                        `steamid` VARCHAR(32) PRIMARY KEY NOT NULL,
                        `puan` INT(10) NOT NULL DEFAULT 0
                    )");
                Console.WriteLine("[IsyanPlugin] Veritabanı tablosu başarıyla oluşturuldu.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IsyanPlugin] Tablo oluşturma hatası: {ex.Message}");
            }
        }

        private static async Task<MySqlConnection?> ConnectAsync()
        {
            try
            {
                var connection = new MySqlConnection(DatabaseConnectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IsyanPlugin] Veritabanı bağlantı hatası: {ex.Message}");
                return null;
            }
        }

        private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player?.IsValid == true && !player.IsBot && !player.IsHLTV)
            {
                _cooldowns[player] = false;
                Task.Run(() => LoadPlayerData(player));
            }
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player?.IsValid == true)
            {
                if (_playerList.FirstOrDefault(p => p.Player == player) is { } target)
                {
                    SavePlayerData(target);
                }
                _cooldowns.Remove(player);
            }
            return HookResult.Continue;
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false }))
            {
                if (_playerList.FirstOrDefault(p => p.Player == player) is { } target && target.IsyanPuan > 0)
                {
                    SavePlayerData(target);
                }
            }
            return HookResult.Continue;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            if (!_isyanAcik || @event.Attacker == null || @event.Userid == null || @event.Weapon == null)
                return HookResult.Continue;

            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (victim.TeamNum != 3 || attacker.TeamNum != 2)
                return HookResult.Continue;

            int points = 0;
            var weapon = @event.Weapon;
            bool isKnife = weapon.Contains("knife") || weapon.Contains("bayonet") || weapon.Contains("karambit") ||
                          weapon.Contains("flip") || weapon.Contains("gut") || weapon.Contains("falchion") ||
                          weapon.Contains("butterfly") || weapon.Contains("tactical") || weapon.Contains("m9") ||
                          weapon.Contains("survival");

            if (isKnife)
                points = Config.BicakPoint;
            else if (@event.Headshot)
                points = Config.HeadShotPoint;
            else if (@event.Noscope)
                points = Config.NoScopePoint;
            else if (@event.Attackerblind)
                points = Config.BlindPoint;
            else
                points = Config.NormalKillPoint;

            if (points > 0)
            {
                AddPoint(attacker, points);
            }

            return HookResult.Continue;
        }


        [ConsoleCommand("css_puanekle", "Belirtilen oyuncuya puan ekler")]
        [RequiresPermissions("@css/root")]
        [CommandHelper(minArgs: 2, usage: "<oyuncu> <puan>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnPuanEkle(CCSPlayerController? caller, CommandInfo command)
        {
            if (caller == null)
                return;

            var targets = GetTarget(command);
            if (targets == null || !targets.Players.Any())
            {
                caller.PrintToChat($" {Prefix} {ChatColors.Red}Geçerli oyuncu bulunamadı.");
                return;
            }

            if (!int.TryParse(command.GetArg(2), out int points) || points <= 0)
            {
                caller.PrintToChat($" {Prefix} {ChatColors.Red}Geçersiz puan değeri.");
                return;
            }

            foreach (var player in targets.Players.Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false }))
            {
                if (_playerList.FirstOrDefault(p => p.Player == player) is { } target)
                {
                    target.IsyanPuan += points;
                    Server.PrintToChatAll($" {Prefix} {ChatColors.Lime}{caller.PlayerName} {ChatColors.White}{ChatColors.Lime}{player.PlayerName} {ChatColors.White}adlı oyuncuya {ChatColors.Lime}{points} {ChatColors.White}puan ekledi.");
                }
            }
        }

        [ConsoleCommand("css_islerim", "Mevcut isyan puanınızı gösterir")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnIsyanPuan(CCSPlayerController? player, CommandInfo command)
        {
            if (player?.IsValid == true && _playerList.FirstOrDefault(p => p.Player == player) is { } target)
            {
                player.PrintToChat($" {Prefix} {ChatColors.Green}Puanınız: {ChatColors.Gold}{target.IsyanPuan}");
            }
        }

        [ConsoleCommand("css_isyancilar", "En yüksek puanlı oyuncuların sıralamasını gösterir")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnIsyancilar(CCSPlayerController? player, CommandInfo command)
        {
            if (player?.IsValid != true || player.IsBot || player.IsHLTV)
                return;

            if (_cooldowns[player])
            {
                player.PrintToChat($" {Prefix} {ChatColors.White}Bu komutu tekrar kullanmak için lütfen bekleyin.");
                return;
            }

            _cooldowns[player] = true;
            Task.Run(() => ShowIsyancilarMenu(player));
            AddTimer(10, () => _cooldowns[player] = false);
        }

        private async Task ShowIsyancilarMenu(CCSPlayerController player)
        {
            using var connection = await ConnectAsync();
            if (connection == null)
            {
                Console.WriteLine("[IsyanPlugin] Lider tablosu için veritabanı bağlantısı başarısız.");
                return;
            }

            try
            {
                var menu = new ChatMenu($" {Prefix} - {ChatColors.Gold}ISYANCILAR");
                var topPlayers = await GetTopPlayers(connection);
                int rank = 1;

                foreach (var row in topPlayers)
                {
                    menu.AddMenuOption(
                        $"{ChatColors.Red}{rank}) {ChatColors.Green}{row.playername} --> {ChatColors.Gold}{row.puan} puan",
                        (_, _) => { }, true);
                    rank++;
                }

                Server.NextFrame(() => ChatMenus.OpenMenu(player, menu));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IsyanPlugin] Lider tablosu alınırken hata: {ex.Message}");
            }
        }

        private static async Task<List<CTBanRow>> GetTopPlayers(MySqlConnection connection)
        {
            try
            {
                return (await connection.QueryAsync<CTBanRow>(
                    "SELECT steamid, playername, puan FROM isyancilar ORDER BY puan DESC LIMIT 20")).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IsyanPlugin] En iyi oyuncular sorgulanırken hata: {ex.Message}");
                return [];
            }
        }

        private async void LoadPlayerData(CCSPlayerController player)
        {
            var steamId = player.AuthorizedSteamID?.SteamId64.ToString();
            if (string.IsNullOrEmpty(steamId))
                return;

            var playerData = new PlayerDetails(steamId, player.PlayerName, 0, player);
            using var connection = await ConnectAsync();
            if (connection != null)
            {
                try
                {
                    var dbData = await connection.QueryFirstOrDefaultAsync<PlayerDataFromDB>(
                        "SELECT * FROM isyancilar WHERE steamid = @SteamId", new { SteamId = steamId });
                    if (dbData != null)
                    {
                        playerData.IsyanPuan = dbData.puan;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IsyanPlugin] Oyuncu verileri yüklenirken hata: {ex.Message}");
                }
            }

            lock (_playerList)
            {
                _playerList.Add(playerData);
            }
        }

        private static async void SavePlayerData(PlayerDetails player)
        {
            if (player.IsyanPuan <= 0)
                return;

            using var connection = await ConnectAsync();
            if (connection == null)
            {
                Console.WriteLine("[IsyanPlugin] Veritabanı bağlantısı başarısız, veri kaydedilemedi.");
                return;
            }

            try
            {
                await connection.ExecuteAsync(
                    "INSERT INTO isyancilar (playername, steamid, puan) VALUES (@PlayerName, @SteamId, @Points) " +
                    "ON DUPLICATE KEY UPDATE playername = @PlayerName, puan = @Points",
                    new { player.PlayerName, player.SteamId, Points = player.IsyanPuan });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IsyanPlugin] Oyuncu verileri kaydedilirken hata: {ex.Message}");
            }
        }

        private static void AddPoint(CCSPlayerController player, int points)
        {
            if (_playerList.FirstOrDefault(p => p.Player == player) is { } target)
            {
                target.IsyanPuan += points;
            }
        }

        private TargetResult? GetTarget(CommandInfo command)
        {
            var matches = command.GetArgTargetResult(1);
            if (!matches.Any())
            {
                command.ReplyToCommand($"{Prefix} {ChatColors.Red}Oyuncu bulunamadı.");
                return null;
            }

            if (command.GetArg(1).StartsWith('@') || matches.Count() == 1)
                return matches;

            command.ReplyToCommand($"{Prefix} {ChatColors.Red}Birden fazla oyuncu bulundu.");
            return null;
        }

        public class PlayerDetails
        {
            public CCSPlayerController Player { get; }
            public string SteamId { get; }
            public string PlayerName { get; set; }
            public int IsyanPuan { get; set; }

            public PlayerDetails(string steamId, string playerName, int isyanPuan, CCSPlayerController player)
            {
                SteamId = steamId;
                PlayerName = playerName;
                IsyanPuan = isyanPuan;
                Player = player;
            }
        }

        public class PlayerDataFromDB
        {
            public string steamid { get; set; } = string.Empty;
            public string playername { get; set; } = string.Empty;
            public int puan { get; set; }
        }

        public class CTBanRow
        {
            public string steamid { get; set; } = string.Empty;
            public string playername { get; set; } = string.Empty;
            public int puan { get; set; }
        }
    }
}