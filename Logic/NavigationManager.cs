using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MarketTraveler.Logic
{
    public class NavigationManager : IDisposable
    {
        private readonly Plugin Plugin;
        private readonly List<string> WorldList = new();

        public NavigationManager(Plugin plugin)
        {
            this.Plugin = plugin;
            LoadWorlds();
        }

        private void LoadWorlds()
        {
            var worlds = Service.Data.GetExcelSheet<World>();
            if (worlds == null) return;
            
            var sortedWorlds = worlds
                .Where(w => w.IsPublic && w.DataCenter.RowId > 0)
                .OrderBy(w => w.DataCenter.Value.Name.ToString())
                .ThenBy(w => w.Name.ToString())
                .Select(w => w.Name.ToString())
                .ToList();

            WorldList.AddRange(sortedWorlds);
        }

        public List<string> GetAllWorlds() => WorldList;
        
        public Dictionary<string, List<string>> GetWorldsByDC()
        {
            var worlds = Service.Data.GetExcelSheet<World>();
            var dict = new Dictionary<string, List<string>>();
            if (worlds == null) return dict;

            foreach (var w in worlds.Where(w => w.IsPublic && w.DataCenter.RowId > 0))
            {
                var dcName = w.DataCenter.Value.Name.ToString();
                var wName = w.Name.ToString();
                if (!dict.ContainsKey(dcName)) dict[dcName] = new List<string>();
                dict[dcName].Add(wName);
            }
            return dict;
        }

        public bool IsInZone(uint territoryType)
        {
            return Service.ClientState.TerritoryType == territoryType;
        }

        public bool GoToWorld(string worldName)
        {
            // FIXED: Removed the HomeWorld bug!
            if (Service.ClientState.LocalPlayer?.CurrentWorld.Value.Name.ToString() == worldName)
            {
                return true; 
            }

            if (Plugin.Lifestream.IsBusy()) 
            {
                Service.Log.Warning("Waiting for Lifestream...");
                return false;
            }
            
            return Plugin.Lifestream.ChangeWorld(worldName);
        }

        public void TeleportToLimsa()
        {
             if (IsInZone(129)) return;
             if (!Plugin.Lifestream.IsBusy())
             {
                 Plugin.Lifestream.Teleport(8, 0); 
             }
        }

        public void MoveToMarketBoard()
        {
            if (!IsInZone(129)) return;
            
            var board = FindMarketBoard();
            if (board != null)
            {
                 if (Vector3.Distance(Service.ClientState.LocalPlayer.Position, board.Position) < 5.0f)
                 {
                     return;
                 }
                 if (Plugin.Vnavmesh.IsReady())
                 {
                     Plugin.Vnavmesh.PathfindAndMoveTo(board.Position, false);
                 }
                 return;
            }

            if (!Plugin.Vnavmesh.IsRunning() && !Plugin.Lifestream.IsBusy())
            {
                Plugin.Lifestream.AethernetTeleport("Hawkers' Alley");
            }
        }

        public unsafe bool InteractWithMarketBoard()
        {
            var board = FindMarketBoard();
            if (board != null)
            {
                if (Vector3.Distance(Service.ClientState.LocalPlayer.Position, board.Position) > 6.0f)
                {
                    return false; 
                }
                Service.TargetManager.Target = board;
                TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)board.Address, false);
                return true;
            }
            return false;
        }

        private IGameObject? FindMarketBoard()
        {
            return Service.ObjectTable.FirstOrDefault(o => o.Name.ToString() == "Market Board");
        }

        public void Dispose() { }
    }
}