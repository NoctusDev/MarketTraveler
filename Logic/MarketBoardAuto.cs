using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Text;
using System.Linq;

namespace MarketTraveler.Logic
{
    public unsafe class MarketBoardAuto : IDisposable
    {
        private Plugin Plugin;
        
        public bool IsDone { get; private set; } = false; 
        public int SessionPurchasedQty { get; private set; } = 0; 
        
        public bool StepRequested { get; set; } = false;
        public string CurrentStateName => State.ToString();

        private enum MarketState
        {
            Idle,
            WaitForSearchWindow,
            WaitAfterTextSearch,
            WaitForResultWindow,
            ProcessResultWindow,
            WaitForConfirmWindow,
            ProcessConfirmWindow,
            CleanUpConfirmWindow, 
            WaitAfterPurchase,
            Done
        }
        
        private MarketState State = MarketState.Idle;
        private uint TargetItemId = 0;
        private string TargetItemName = "";
        
        private int MaxUnitPrice = 0; 
        private int TargetQty = 0;
        private int PendingQty = 0; 
        private int RetryCount = 0; 

        private DateTime NextActionTime = DateTime.MinValue;
        private DateTime TimeoutTime = DateTime.MinValue;

        public MarketBoardAuto(Plugin plugin)
        {
            this.Plugin = plugin;
            Service.Framework.Update += OnUpdate;
        }

        private void ChangeState(MarketState newState, int delayMs = 0)
        {
            if (State != newState)
            {
                Service.Log.Debug($"[MarketTraveler] State Transition: {State} -> {newState}");
                State = newState;
                if (delayMs > 0)
                {
                    NextActionTime = DateTime.Now.AddMilliseconds(delayMs);
                }
            }
        }

        public void StartBuying(uint itemId, int totalNeeded, int maxUnitPrice)
        {
            if (!IsDone && TargetItemId == itemId && State != MarketState.Idle && State != MarketState.Done) 
                return; 

            TargetItemId = itemId;
            TargetQty = totalNeeded;
            MaxUnitPrice = maxUnitPrice; 
            
            var item = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow(itemId);
            TargetItemName = item?.Name.ToString() ?? "";
            
            SessionPurchasedQty = 0;
            PendingQty = 0;
            RetryCount = 0; 
            IsDone = false;
            StepRequested = false;
            
            TimeoutTime = DateTime.Now.AddSeconds(45); 
            
            Service.Log.Info($"[MarketTraveler] Worker started! Item: {TargetItemName} ({itemId}) | Need: {TargetQty} | Max Price: {MaxUnitPrice}");
            
            ChangeState(MarketState.WaitForSearchWindow, 250);
        }

        public void Stop()
        {
            Service.Log.Info("[MarketTraveler] Worker manually stopped.");
            ChangeState(MarketState.Idle);
            IsDone = true;
        }

        public void ForceDone()
        {
            Service.Log.Info("[MarketTraveler] Worker forced to complete/abort current item.");
            SessionPurchasedQty = 0;
            IsDone = true;
            ChangeState(MarketState.Idle);
        }

        private void OnUpdate(IFramework framework)
        {
            if (State == MarketState.Idle || State == MarketState.Done) return;
            
            if (Plugin.Configuration.DebugStepMode)
            {
                if (!StepRequested) return; 
                StepRequested = false;      
            }
            else
            {
                if (DateTime.Now > TimeoutTime)
                {
                    Service.Log.Error($"[MarketTraveler] TIMEOUT ERROR: Worker got stuck in state: {State} for too long! Aborting item.");
                    ForceDone();
                    return;
                }
                if (DateTime.Now < NextActionTime) return;
            }

            switch (State)
            {
                case MarketState.WaitForSearchWindow:
                    var searchAddonPtr = Service.GameGui.GetAddonByName("ItemSearch", 1);
                    if (searchAddonPtr.Address != IntPtr.Zero)
                    {
                        var searchAddon = (AtkUnitBase*)searchAddonPtr.Address;
                        
                        if (!searchAddon->IsVisible || searchAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        Service.Log.Debug("[MarketTraveler] ItemSearch window loaded. Typing item name...");

                        byte[] nameBytes = Encoding.UTF8.GetBytes(TargetItemName + "\0");
                        fixed (byte* ptr = nameBytes)
                        {
                            var values = stackalloc AtkValue[8];
                            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 0;
                            values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[1].Int = -1;
                            values[2].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[2].Int = 0;
                            values[3].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String; values[3].String = ptr;
                            values[4].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String; values[4].String = ptr;
                            values[5].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[5].Int = 100;
                            values[6].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[6].Int = 100;
                            values[7].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[7].Int = 40;
                            searchAddon->FireCallback(8, values);
                        }

                        ChangeState(MarketState.WaitAfterTextSearch, 800);
                    }
                    break;
                    
                case MarketState.WaitAfterTextSearch:
                    var saPtr = Service.GameGui.GetAddonByName("ItemSearch", 1);
                    if (saPtr.Address != IntPtr.Zero)
                    {
                        var searchAddon = (AtkUnitBase*)saPtr.Address;
                        
                        if (!searchAddon->IsVisible || searchAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        var listNode = searchAddon->GetNodeById(139); 
                        if (listNode == null) 
                        {
                            Service.Log.Warning("[MarketTraveler] WaitAfterTextSearch: Node 139 is null. Waiting...");
                            return; 
                        }
                        
                        var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                        if (listComponent == null || listComponent->ListLength == 0) return; 

                        int exactMatchIndex = -1;
                        string cleanTarget = CleanString(TargetItemName);
                        
                        for (int i = 0; i < listComponent->ListLength; i++)
                        {
                            string cleanName = CleanString(GetSearchItemName(searchAddon, i));
                            if (cleanName.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase))
                            {
                                exactMatchIndex = i;
                                break;
                            }
                        }

                        if (exactMatchIndex == -1)
                        {
                            Service.Log.Warning($"[MarketTraveler] Could not find an exact match for {cleanTarget} in the search results.");
                            ForceDone();
                            return;
                        }

                        Service.Log.Debug($"[MarketTraveler] Found {TargetItemName} at index {exactMatchIndex}. Clicking item...");

                        var values = stackalloc AtkValue[2];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 5;
                        values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[1].Int = exactMatchIndex; 
                        searchAddon->FireCallback(2, values);
                        
                        ChangeState(MarketState.WaitForResultWindow, 500);
                    }
                    break;

                case MarketState.WaitForResultWindow:
                    var resultAddonPtr = Service.GameGui.GetAddonByName("ItemSearchResult", 1);
                    if (resultAddonPtr.Address != IntPtr.Zero)
                    {
                        var resultAddon = (AtkUnitBase*)resultAddonPtr.Address;
                        
                        if (!resultAddon->IsVisible || resultAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        var listNode = resultAddon->GetNodeById(26);
                        if (listNode == null) return; 
                        
                        var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                        if (listComponent == null || listComponent->ListLength == 0) return; 

                        Service.Log.Debug("[MarketTraveler] ItemSearchResult window fully loaded. Processing prices...");
                        
                        ChangeState(MarketState.ProcessResultWindow, 500);
                    }
                    break;

                case MarketState.ProcessResultWindow:
                    var procAddonPtr = Service.GameGui.GetAddonByName("ItemSearchResult", 1);
                    if (procAddonPtr.Address != IntPtr.Zero)
                    {
                        var resultAddon = (AtkUnitBase*)procAddonPtr.Address;
                        
                        if (!resultAddon->IsVisible || resultAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        var agentModule = AgentModule.Instance();
                        var agent = (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
                        
                        if (agent != null && agent->ResultItemId != TargetItemId) 
                        { 
                            Service.Log.Debug("[MarketTraveler] Agent ResultItemId mismatch. Finishing shopping.");
                            FinishShopping(resultAddon); 
                            return; 
                        }

                        int currentInv = InventoryManager.Instance()->GetInventoryItemCount(TargetItemId);
                        if (currentInv >= TargetQty) 
                        { 
                            Service.Log.Info($"[MarketTraveler] Target quantity reached ({currentInv}/{TargetQty}). Wrapping up.");
                            FinishShopping(resultAddon); 
                            return; 
                        }

                        var listComponent = (AtkComponentList*)((AtkComponentNode*)resultAddon->GetNodeById(26))->Component;
                        int targetIndex = -1;

                        for (int i = 0; i < Math.Min(10, listComponent->ListLength); i++)
                        {
                            int unitPrice = GetListingPrice(resultAddon, i);
                            int qty = GetListingQuantity(resultAddon, i);
                            if (unitPrice == int.MaxValue || unitPrice == 0 || qty == 0) continue; 

                            if (unitPrice <= MaxUnitPrice && targetIndex == -1)
                            {
                                targetIndex = i;
                                PendingQty = qty; 
                            }
                        }

                        if (targetIndex == -1)
                        {
                            if (RetryCount < 3)
                            {
                                RetryCount++;
                                Service.Log.Debug($"[MarketTraveler] No suitable prices found. Retry {RetryCount}/3...");
                                ChangeState(State, 800); 
                                return;
                            }
                            else 
                            { 
                                Service.Log.Info("[MarketTraveler] All retries exhausted. No valid items under max price.");
                                FinishShopping(resultAddon); 
                                return; 
                            }
                        }

                        Service.Log.Debug($"[MarketTraveler] Selecting listing at index {targetIndex}. Quantity: {PendingQty}.");

                        var values = stackalloc AtkValue[2];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 2; 
                        values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[1].Int = targetIndex; 
                        resultAddon->FireCallback(2, values);
                        
                        ChangeState(MarketState.WaitForConfirmWindow, 400);
                    }
                    break;
                    
                case MarketState.WaitForConfirmWindow:
                    var confirmAddonPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (confirmAddonPtr.Address != IntPtr.Zero)
                    {
                        var confirmAddon = (AtkUnitBase*)confirmAddonPtr.Address;
                        
                        if (!confirmAddon->IsVisible || confirmAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        ChangeState(MarketState.ProcessConfirmWindow, 200);
                    }
                    break;

                case MarketState.ProcessConfirmWindow:
                    var procConfirmPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (procConfirmPtr.Address != IntPtr.Zero)
                    {
                        var procConfirmAddon = (AtkUnitBase*)procConfirmPtr.Address;
                        if (!procConfirmAddon->IsVisible || procConfirmAddon->UldManager.LoadedState != AtkLoadState.Loaded) return;

                        Service.Log.Debug("[MarketTraveler] Confirming purchase...");

                        var values = stackalloc AtkValue[1];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 0; 
                        procConfirmAddon->FireCallback(1, values);
                        
                        SessionPurchasedQty += PendingQty; 
                        RetryCount = 0; 
                        TimeoutTime = DateTime.Now.AddSeconds(20); 
                        
                        ChangeState(MarketState.CleanUpConfirmWindow, 800);
                    }
                    break;

                case MarketState.CleanUpConfirmWindow:
                    var cleanupPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (cleanupPtr.Address != IntPtr.Zero)
                    {
                        var cleanupAddon = (AtkUnitBase*)cleanupPtr.Address;
                        if (cleanupAddon->IsVisible)
                        {
                            Service.Log.Debug("[MarketTraveler] Closing confirm dialog...");
                            var closeValues = stackalloc AtkValue[1];
                            closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; closeValues[0].Int = -1; 
                            cleanupAddon->FireCallback(1, closeValues);
                            try { cleanupAddon->Close(true); } catch { }
                        }
                    }
                    
                    ChangeState(MarketState.WaitAfterPurchase, 250);
                    break;

                case MarketState.WaitAfterPurchase:
                    ChangeState(MarketState.WaitForResultWindow);
                    break;
            }
        }

        private void FinishShopping(AtkUnitBase* resultAddon)
        {
            Service.Log.Info("[MarketTraveler] Closing Market Board UI and finishing state.");
            var closeValues = stackalloc AtkValue[1];
            closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; closeValues[0].Int = -1; 
            resultAddon->FireCallback(1, closeValues);
            ChangeState(MarketState.Done);
            IsDone = true; 
        }

        private string GetSearchItemName(AtkUnitBase* addon, int index)
        {
            try
            {
                var listNode = addon->GetNodeById(139); 
                if (listNode == null) return "";
                var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                if (index < 0 || index >= listComponent->ListLength) return "";
                var renderer = listComponent->ItemRendererList[index].AtkComponentListItemRenderer;
                return renderer != null && renderer->AtkComponentButton.ButtonTextNode != null 
                    ? renderer->AtkComponentButton.ButtonTextNode->NodeText.ToString() : "";
            }
            catch { return ""; }
        }

        private string CleanString(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string cleaned = new string(input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();
            if (cleaned.StartsWith("HI")) cleaned = cleaned.Substring(2);
            if (cleaned.EndsWith("IH")) cleaned = cleaned.Substring(0, cleaned.Length - 2);
            return cleaned.Trim();
        }

        private int GetListingPrice(AtkUnitBase* addon, int index)
        {
            try
            {
                var listNode = addon->GetNodeById(26); 
                if (listNode == null) return int.MaxValue;
                var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                if (index < 0 || index >= listComponent->ListLength) return int.MaxValue;
                var renderer = listComponent->ItemRendererList[index].AtkComponentListItemRenderer;
                if (renderer == null) return int.MaxValue;
                var priceNode = (AtkTextNode*)renderer->AtkComponentButton.AtkComponentBase.UldManager.SearchNodeById(5); 
                if (priceNode == null) return int.MaxValue;
                string cleanText = new string(priceNode->NodeText.ToString().Where(char.IsDigit).ToArray()); 
                return int.TryParse(cleanText, out int price) ? price : int.MaxValue;
            }
            catch { return int.MaxValue; }
        }

        private int GetListingQuantity(AtkUnitBase* addon, int index)
        {
            try
            {
                var listNode = addon->GetNodeById(26);
                if (listNode == null) return 0;
                var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                if (index < 0 || index >= listComponent->ListLength) return 0;
                var renderer = listComponent->ItemRendererList[index].AtkComponentListItemRenderer;
                if (renderer == null) return 0;
                var qtyNode = (AtkTextNode*)renderer->AtkComponentButton.AtkComponentBase.UldManager.SearchNodeById(6); 
                if (qtyNode == null) return 0;
                string cleanText = new string(qtyNode->NodeText.ToString().Where(char.IsDigit).ToArray()); 
                return int.TryParse(cleanText, out int qty) ? qty : 0;
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            Service.Framework.Update -= OnUpdate;
        }
    }
}