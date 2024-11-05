using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CS2GamingAPIShared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using static CounterStrikeSharp.API.Core.Listeners;

namespace MVP
{
    public class Plugin : BasePlugin
    {
        public override string ModuleName => "The MVP Acheivement";
        public override string ModuleVersion => "1.0";

        private ICS2GamingAPIShared? _cs2gamingAPI { get; set; }
        public static PluginCapability<ICS2GamingAPIShared> _capability { get; } = new("cs2gamingAPI");
        public ConVar? _timelimit = null;
        public Dictionary<CCSPlayerController, PlayerData> _playerData { get; set; } = new();
        public string? filePath { get; set; }
        public readonly ILogger<Plugin> _logger;

        public override void Load(bool hotReload)
        {
            RegisterListener<OnMapStart>(OnMapStart);
            InitializeData();
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _cs2gamingAPI = _capability.Get();
        }

        public Plugin(ILogger<Plugin> logger)
        {
            _logger = logger;
        }

        bool matchPoint = false;
        bool final = false;
        int teamwin = 0;
        double startTime = 0.0;
        bool reachMaxRound = false;

        public void OnMapStart(string mapname)
        {
            startTime = Server.EngineTime;
            _timelimit = ConVar.Find("mp_timelimit");
            reachMaxRound = false;
        }

        public void InitializeData()
        {
            filePath = Path.Combine(ModuleDirectory, "playerdata.json");

            if (!File.Exists(filePath))
            {
                var empty = "{}";

                File.WriteAllText(filePath, empty);
                _logger.LogInformation("Data file is not found creating a new one.");
            }

            _logger.LogInformation("Found Data file at {0}.", filePath);
        }

        [GameEventHandler]
        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var client = @event.Userid;

            if (!IsValidPlayer(client))
                return HookResult.Continue;

            var steamID = client!.AuthorizedSteamID!.SteamId64;

            var data = GetPlayerData(steamID);

            var dateStart = DateTime.Today.ToShortDateString();
            var dateEnd = DateTime.Today.AddDays(7.0f).ToShortDateString();

            if (data == null)
                _playerData.Add(client!, new(dateStart, dateEnd, false));

            else
            {
                var complete = data.Complete;
                dateStart = data.TimeAcheived;
                dateEnd = data.TimeReset;

                if (data.TimeReset == DateTime.Today.ToShortDateString())
                {
                    complete = false;
                    dateStart = DateTime.Today.ToShortDateString();
                    dateEnd = DateTime.Today.AddDays(7.0f).ToShortDateString();
                    Task.Run(async () => await SaveClientData(steamID, complete, true));
                }

                _playerData.Add(client!, new(dateStart, dateEnd, complete));
            }

            return HookResult.Continue;
        }

        public void OnClientDisconnect(int playerslot)
        {
            var client = Utilities.GetPlayerFromSlot(playerslot);

            if (!IsValidPlayer(client))
                return;

            var steamID = client!.AuthorizedSteamID!.SteamId64;
            var complete = _playerData[client].Complete;

            Task.Run(async () => await SaveClientData(steamID, complete, !complete));

            _playerData.Remove(client!);
        }

        [GameEventHandler]
        public HookResult OnMatchPoint(EventRoundAnnounceMatchPoint @event, GameEventInfo info)
        {
            var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

            var ct = 0;
            var t = 0;

            foreach (var teamManager in teamManagers)
            {
                if (teamManager.TeamNum == 2)
                    t = teamManager.Score;

                else if(teamManager.TeamNum == 3)
                    ct = teamManager.Score;
            }

            if (ct > t)
                teamwin = 3;

            else if(ct < t)
                teamwin = 2;


            //Server.PrintToChatAll($"expect winner = {teamwin}");
            matchPoint = true;
            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnFinalRound(EventRoundAnnounceFinal @event, GameEventInfo info)
        {
            // Server.PrintToChatAll($"Final round get trigger.");
            final = true;
            reachMaxRound = true;
            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            var winner = @event.Winner;
            var timelimit = _timelimit?.GetPrimitiveValue<float>();
            var total = (Server.EngineTime - startTime) / 60.0;

            bool actualFinal = timelimit > 0 && (total > timelimit);

            if (reachMaxRound)
                actualFinal = true;

            //Server.PrintToChatAll($"win team = {winner}");

            if ((final && actualFinal) || (matchPoint && winner == teamwin))
            {
                var mvpMap = Utilities.GetPlayers().OrderByDescending(player => player.MVPs).First();

                AddTimer(0.1f, () =>
                {
                    if (!IsValidPlayer(mvpMap))
                        return;

                    var steamid = mvpMap.AuthorizedSteamID?.SteamId64;
                    Task.Run(async () => await TaskComplete(mvpMap!, (ulong)steamid!));
                });
            }

            matchPoint = false;
            final = false;

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            var timelimit = _timelimit?.GetPrimitiveValue<float>();

            var roundTime = ConVar.Find("mp_roundtime")?.GetPrimitiveValue<float>();

            if (timelimit == null)
                return HookResult.Continue;

            var total = (Server.EngineTime - startTime) / 60.0;

            if (total + roundTime > timelimit)
                final = true;

            return HookResult.Continue;
        }

        public async Task TaskComplete(CCSPlayerController client, ulong steamid)
        {
            var response = await _cs2gamingAPI?.RequestSteamID(steamid!)!;
            if (response != null)
            {
                if (response.Status != 200)
                    return;

                Server.NextFrame(() =>
                {
                    client.PrintToChat($" {ChatColors.Green}[Acheivement]{ChatColors.Default} You acheive 'The MVP' (Having the most MVP scores in game!)");
                    client.PrintToChat($" {ChatColors.Green}[Acheivement]{ChatColors.Default} {response.Message}");
                });

                await SaveClientData(steamid!, true, true);
            }
        }

        private async Task SaveClientData(ulong steamid, bool complete, bool settime)
        {
            var finishTime = DateTime.Today.ToShortDateString();
            var resetTime = DateTime.Today.AddDays(7.0).ToShortDateString();
            var steamKey = steamid.ToString();

            var data = new PlayerData(finishTime, resetTime, complete);

            var jsonObject = ParseFileToJsonObject();

            if (jsonObject == null)
                return;

            if (jsonObject.ContainsKey(steamKey))
            {
                jsonObject[steamKey].Complete = complete;

                if (settime)
                {
                    jsonObject[steamKey].TimeAcheived = finishTime;
                    jsonObject[steamKey].TimeReset = resetTime;
                }

                var updated = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                await File.WriteAllTextAsync(filePath!, updated);
            }

            else
            {
                jsonObject.Add(steamKey, data);
                var updated = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                await File.WriteAllTextAsync(filePath!, updated);
            }
        }

        private PlayerData? GetPlayerData(ulong steamid)
        {
            var jsonObject = ParseFileToJsonObject();

            if (jsonObject == null)
                return null;

            var steamKey = steamid.ToString();

            if (jsonObject.ContainsKey(steamKey))
                return jsonObject[steamKey];

            return null;
        }

        private Dictionary<string, PlayerData>? ParseFileToJsonObject()
        {
            if (!File.Exists(filePath))
                return null;

            return JsonConvert.DeserializeObject<Dictionary<string, PlayerData>>(File.ReadAllText(filePath));
        }

        public bool IsValidPlayer(CCSPlayerController? client)
        {
            return client != null && client.IsValid && !client.IsBot;
        }
    }
}
