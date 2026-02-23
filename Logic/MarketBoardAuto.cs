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
            
            State = MarketState.WaitForSearchWindow;
            
            // [TIMER TWEAK] Wait for the Market Board to fully open before typing.
            // Risk: If too fast, it types the name before the search box exists.
            NextActionTime = DateTime.Now.AddMilliseconds(250); 
            TimeoutTime = DateTime.Now.AddSeconds(45); 
            
            Service.Log.Info($"Worker started! Item: {TargetItemName} ({itemId}) | Need: {TargetQty}");
        }

        public void Stop()
        {
            State = MarketState.Idle;
            IsDone = true;
        }

        public void ForceDone()
        {
            SessionPurchasedQty = 0;
            IsDone = true;
            State = MarketState.Idle;
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
                    Service.Log.Error($"MarketBoardAuto Timed Out while in state: {State}! Aborting item.");
                    ForceDone();
                    return;
                }
                if (DateTime.Now < NextActionTime) return;
            }

            switch (State)
            {
                case MarketState.WaitForSearchWindow:
                    var searchAddonPtr = Service.GameGui.GetAddonByName("ItemSearch", 1);
                    if (searchAddonPtr.Address != IntPtr.Zero && ((AtkUnitBase*)searchAddonPtr.Address)->IsVisible)
                    {
                        var searchAddon = (AtkUnitBase*)searchAddonPtr.Address;
                        
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

                        State = MarketState.WaitAfterTextSearch;
                        
                        // [TIMER TWEAK] Time needed for the Server to process your text search and return the list of items.
                        // Risk: If too fast, the bot tries to click the item before the server populates the list.
                        NextActionTime = DateTime.Now.AddMilliseconds(800); 
                    }
                    break;
                    
                case MarketState.WaitAfterTextSearch:
                    var saPtr = Service.GameGui.GetAddonByName("ItemSearch", 1);
                    if (saPtr.Address != IntPtr.Zero && ((AtkUnitBase*)saPtr.Address)->IsVisible)
                    {
                        var searchAddon = (AtkUnitBase*)saPtr.Address;
                        var listNode = searchAddon->GetNodeById(139); 
                        if (listNode == null) return; 
                        
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
                            ForceDone();
                            return;
                        }

                        var values = stackalloc AtkValue[2];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 5;
                        values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[1].Int = exactMatchIndex; 
                        searchAddon->FireCallback(2, values);
                        
                        State = MarketState.WaitForResultWindow;
                        
                        // [TIMER TWEAK] Time it takes for the item's specific price listing window to visibly open on screen.
                        // Risk: If too fast, it tries to read prices before the window exists.
                        NextActionTime = DateTime.Now.AddMilliseconds(500);
                    }
                    break;

                case MarketState.WaitForResultWindow:
                    var resultAddonPtr = Service.GameGui.GetAddonByName("ItemSearchResult", 1);
                    if (resultAddonPtr.Address != IntPtr.Zero && ((AtkUnitBase*)resultAddonPtr.Address)->IsVisible)
                    {
                        var resultAddon = (AtkUnitBase*)resultAddonPtr.Address;
                        var listNode = resultAddon->GetNodeById(26);
                        if (listNode == null) return; 
                        
                        var listComponent = (AtkComponentList*)((AtkComponentNode*)listNode)->Component;
                        if (listComponent == null || listComponent->ListLength == 0) return; 

                        State = MarketState.ProcessResultWindow;
                        
                        // [TIMER TWEAK] Time needed for the Server to transmit the Gil prices into the window.
                        // Risk: If too fast, the bot reads the prices as '0' and assumes everything is too expensive.
                        NextActionTime = DateTime.Now.AddMilliseconds(500); 
                    }
                    break;

                case MarketState.ProcessResultWindow:
                    var procAddonPtr = Service.GameGui.GetAddonByName("ItemSearchResult", 1);
                    if (procAddonPtr.Address != IntPtr.Zero)
                    {
                        var resultAddon = (AtkUnitBase*)procAddonPtr.Address;
                        var agentModule = AgentModule.Instance();
                        var agent = (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
                        
                        if (agent != null && agent->ResultItemId != TargetItemId) { FinishShopping(resultAddon); return; }

                        int currentInv = InventoryManager.Instance()->GetInventoryItemCount(TargetItemId);
                        if (currentInv >= TargetQty) { FinishShopping(resultAddon); return; }

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
                                // [TIMER TWEAK] How long to wait before double-checking a completely empty board.
                                NextActionTime = DateTime.Now.AddMilliseconds(800); 
                                return;
                            }
                            else { FinishShopping(resultAddon); return; }
                        }

                        var values = stackalloc AtkValue[2];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 2; 
                        values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[1].Int = targetIndex; 
                        resultAddon->FireCallback(2, values);
                        
                        State = MarketState.WaitForConfirmWindow;
                        
                        // [TIMER TWEAK] Time for the "Are you sure you want to buy this?" pop-up window to appear.
                        NextActionTime = DateTime.Now.AddMilliseconds(400); 
                    }
                    break;
                    
                case MarketState.WaitForConfirmWindow:
                    var confirmAddonPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (confirmAddonPtr.Address != IntPtr.Zero && ((AtkUnitBase*)confirmAddonPtr.Address)->IsVisible)
                    {
                        State = MarketState.ProcessConfirmWindow;
                        
                        // [TIMER TWEAK] Gives the UI a tiny moment to render the Yes/No buttons before clicking.
                        NextActionTime = DateTime.Now.AddMilliseconds(200); 
                    }
                    break;

                case MarketState.ProcessConfirmWindow:
                    var procConfirmPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (procConfirmPtr.Address != IntPtr.Zero)
                    {
                        var values = stackalloc AtkValue[1];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; values[0].Int = 0; 
                        ((AtkUnitBase*)procConfirmPtr.Address)->FireCallback(1, values);
                        
                        SessionPurchasedQty += PendingQty; 
                        RetryCount = 0; 
                        TimeoutTime = DateTime.Now.AddSeconds(20); 
                        
                        State = MarketState.CleanUpConfirmWindow;
                        
                        // [TIMER TWEAK] *** THE MOST IMPORTANT TIMER ***
                        // This is how long the bot waits for the Server to physically deduct your Gil and hand you the item.
                        // Risk: If too fast, the bot tries to buy the NEXT item before the server finishes processing this one, 
                        // causing the server to reject the purchase with a "Transaction in progress" red error text.
                        NextActionTime = DateTime.Now.AddMilliseconds(800); 
                    }
                    break;

                case MarketState.CleanUpConfirmWindow:
                    var cleanupPtr = Service.GameGui.GetAddonByName("SelectYesno", 1); 
                    if (cleanupPtr.Address != IntPtr.Zero && ((AtkUnitBase*)cleanupPtr.Address)->IsVisible)
                    {
                        var closeValues = stackalloc AtkValue[1];
                        closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; closeValues[0].Int = -1; 
                        ((AtkUnitBase*)cleanupPtr.Address)->FireCallback(1, closeValues);
                        try { ((AtkUnitBase*)cleanupPtr.Address)->Close(true); } catch { }
                    }
                    
                    State = MarketState.WaitAfterPurchase;
                    
                    // [TIMER TWEAK] A tiny breather before it loops back around to read the result window again.
                    NextActionTime = DateTime.Now.AddMilliseconds(250); 
                    break;

                case MarketState.WaitAfterPurchase:
                    State = MarketState.WaitForResultWindow;
                    break;
            }
        }

        private void FinishShopping(AtkUnitBase* resultAddon)
        {
            var closeValues = stackalloc AtkValue[1];
            closeValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int; closeValues[0].Int = -1; 
            resultAddon->FireCallback(1, closeValues);
            State = MarketState.Done;
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