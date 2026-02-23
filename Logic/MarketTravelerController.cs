using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;

namespace MarketTraveler.Logic
{
    public class ShoppingItem
    {
        public uint ItemId { get; set; }
        public int TargetQty { get; set; }
        public int MaxUnitPrice { get; set; }
        public int PurchasedQty { get; set; } = 0;
    }

    public enum State
    {
        Idle,
        Traveling,
        TeleportingToLimsa,
        MovingToBoard,
        Shopping,
        Finished
    }

    public unsafe class MarketTravelerController : IDisposable
    {
        private Plugin Plugin;
        public State CurrentState { get; private set; } = State.Idle;
        
        public bool IsRunning => CurrentState != State.Idle && CurrentState != State.Finished;
        public string CurrentStateName => CurrentState.ToString();
        
        public string CurrentWorld { get; private set; } = "";
        
        // FIXED: Added nullable '?' to silence the CS8603 warning
        public ShoppingItem? CurrentActiveItem
        {
            get
            {
                if (CurrentState == State.Idle || CurrentState == State.Finished) return null;
                if (ShoppingList == null || CurrentItemIndex >= ShoppingList.Count) return null;
                return ShoppingList[CurrentItemIndex];
            }
        }
        
        private Queue<string> WorldQueue = new();
        private List<ShoppingItem> ShoppingList = new();
        private int CurrentItemIndex = 0;
        
        private HashSet<string> BlacklistedWorlds = new();
        
        private DateTime LastActionTime = DateTime.MinValue;
        private TimeSpan ActionDelay = TimeSpan.FromSeconds(2); 
        
        private DateTime StateEnterTime = DateTime.MinValue; 
        private int ItemsBoughtOnCurrentWorld = 0; 
        
        private const int MaxTravelSeconds = 30; 

        public MarketTravelerController(Plugin plugin)
        {
            this.Plugin = plugin;
            Service.Framework.Update += OnUpdate;
        }

        public void Start(List<ShoppingItem> itemsToBuy, List<string> worlds)
        {
            if (CurrentState != State.Idle) return;
            
            ShoppingList = itemsToBuy;
            WorldQueue.Clear();
            
            int skippedCount = 0;
            foreach (var w in worlds) 
            {
                if (!BlacklistedWorlds.Contains(w))
                {
                    WorldQueue.Enqueue(w);
                }
                else
                {
                    skippedCount++;
                }
            }
            
            string skipMsg = skippedCount > 0 ? $" (Skipped {skippedCount} blacklisted worlds)" : "";
            Service.Log.Info($"Starting MarketTraveler with {ShoppingList.Count} items across {WorldQueue.Count} worlds.{skipMsg}");
            
            if (WorldQueue.Count == 0)
            {
                Service.ChatGui.PrintError("[MarketTraveler] All requested worlds are currently blacklisted for congestion! Click 'Stop' to clear the blacklist.");
                return;
            }

            ProcessNextWorld();
        }

        public void Stop()
        {
            CurrentState = State.Idle;
            BlacklistedWorlds.Clear(); 
            
            Plugin.Vnavmesh.Stop();
            Plugin.MarketBoardAuto.Stop();
            Service.Log.Info("MarketTraveler stopped and blacklist cleared.");
        }

        private void ProcessNextWorld()
        {
            CurrentItemIndex = 0; 
            ItemsBoughtOnCurrentWorld = 0; 

            if (WorldQueue.Count == 0)
            {
                CurrentState = State.Finished;
                Service.ChatGui.Print("[MarketTraveler] Fully completed the world queue! Stopping.");
                Stop(); 
                return;
            }

            CurrentWorld = WorldQueue.Dequeue();
            Service.Log.Info($"Queue: Traveling to {CurrentWorld}...");
            
            CurrentState = State.Traveling; 
            StateEnterTime = DateTime.Now; 
        }

        private void OnUpdate(IFramework framework)
        {
            if (CurrentState == State.Idle || CurrentState == State.Finished) return;
            if (DateTime.Now - LastActionTime < ActionDelay) return;
            
            switch (CurrentState)
            {
                case State.Traveling:
                    if (DateTime.Now - StateEnterTime > TimeSpan.FromSeconds(MaxTravelSeconds))
                    {
                        Service.ChatGui.PrintError($"[MarketTraveler] Skipping {CurrentWorld} due to congestion or queue. Blacklisting for this run!");
                        
                        BlacklistedWorlds.Add(CurrentWorld);
                        
                        string[] blockingAddons = { "SelectString", "SelectOk", "CrossWorldTravelLobby" };
                        
                        // FIXED: Moved stackalloc safely outside the loop to prevent stack overflow!
                        var closeValues = stackalloc AtkValue[1];
                        closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
                        closeValues[0].Int = -1; 
                        
                        foreach (var addonName in blockingAddons)
                        {
                            var addonPtr = Service.GameGui.GetAddonByName(addonName, 1);
                            if (addonPtr.Address != IntPtr.Zero)
                            {
                                ((AtkUnitBase*)addonPtr.Address)->FireCallback(1, closeValues);
                                try { ((AtkUnitBase*)addonPtr.Address)->Close(true); } catch { }
                            }
                        }
                        
                        ProcessNextWorld();
                        LastActionTime = DateTime.Now;
                        return;
                    }

                    if (Service.ClientState.LocalPlayer != null && Service.ClientState.LocalPlayer.CurrentWorld.Value.Name.ToString() == CurrentWorld)
                    {
                        if (Service.Condition[ConditionFlag.BetweenAreas] || Plugin.Lifestream.IsBusy()) return;
                        
                        CurrentState = State.TeleportingToLimsa;
                        StateEnterTime = DateTime.Now;
                        Plugin.Navigation.TeleportToLimsa();
                        LastActionTime = DateTime.Now;
                    }
                    else
                    {
                        if (!Plugin.Lifestream.IsBusy())
                        {
                             Plugin.Navigation.GoToWorld(CurrentWorld);
                             LastActionTime = DateTime.Now;
                        }
                    }
                    break;

                case State.TeleportingToLimsa:
                     if (Plugin.Navigation.IsInZone(129)) 
                     {
                         if (Service.Condition[ConditionFlag.BetweenAreas]) return;
                         CurrentState = State.MovingToBoard;
                         StateEnterTime = DateTime.Now;
                         Plugin.Navigation.MoveToMarketBoard();
                         LastActionTime = DateTime.Now;
                     }
                     else if (!Plugin.Lifestream.IsBusy()) 
                     {
                         Plugin.Navigation.TeleportToLimsa();
                         LastActionTime = DateTime.Now;
                     }
                     break;

                case State.MovingToBoard:
                     if (Service.GameGui.GetAddonByName("ItemSearch", 1).Address != IntPtr.Zero)
                     {
                         CurrentState = State.Shopping;
                         StateEnterTime = DateTime.Now;
                         StartNextShoppingItem();
                         LastActionTime = DateTime.Now;
                         break;
                     }

                     Plugin.Navigation.MoveToMarketBoard(); 
                     if (Plugin.Navigation.InteractWithMarketBoard())
                     {
                         Plugin.Vnavmesh.Stop();
                         LastActionTime = DateTime.Now;
                     }
                     break;
                     
                case State.Shopping:
                     if (Plugin.MarketBoardAuto.IsDone)
                     {
                         int purchasedThisRun = Plugin.MarketBoardAuto.SessionPurchasedQty;
                         if (CurrentActiveItem != null)
                         {
                             CurrentActiveItem.PurchasedQty += purchasedThisRun;
                         }
                         ItemsBoughtOnCurrentWorld += purchasedThisRun;
                         
                         CurrentItemIndex++; 
                         
                         if (CurrentItemIndex >= ShoppingList.Count)
                         {
                             if (ItemsBoughtOnCurrentWorld == 0)
                             {
                                 Service.ChatGui.Print($"[MarketTraveler] Leaving {CurrentWorld}: Bought 0 items.");
                             }
                             else
                             {
                                 Service.ChatGui.Print($"[MarketTraveler] Leaving {CurrentWorld}: Successfully purchased {ItemsBoughtOnCurrentWorld} items!");
                             }
                             
                             var searchAddonPtr = Service.GameGui.GetAddonByName("ItemSearch", 1);
                             if (searchAddonPtr.Address != IntPtr.Zero)
                             {
                                 var searchAddon = (AtkUnitBase*)searchAddonPtr.Address;
                                 if (searchAddon->IsVisible)
                                 {
                                     var closeValues = stackalloc AtkValue[1];
                                     closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
                                     closeValues[0].Int = -1; 
                                     searchAddon->FireCallback(1, closeValues);
                                 }
                             }

                             ProcessNextWorld();
                             LastActionTime = DateTime.Now;
                         }
                         else
                         {
                             StartNextShoppingItem();
                             LastActionTime = DateTime.Now;
                         }
                     }
                     break;
            }
        }

        private void StartNextShoppingItem()
        {
            var item = ShoppingList[CurrentItemIndex];
            int needed = item.TargetQty - item.PurchasedQty;
            
            if (needed > 0)
            {
                Plugin.MarketBoardAuto.StartBuying(item.ItemId, needed, item.MaxUnitPrice);
            }
            else
            {
                Service.Log.Info($"Already have enough of ItemID {item.ItemId}. Skipping.");
                Plugin.MarketBoardAuto.ForceDone();
            }
        }

        public void Dispose()
        {
            Service.Framework.Update -= OnUpdate;
        }
    }
}