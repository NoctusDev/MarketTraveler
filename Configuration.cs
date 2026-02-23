using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace MarketTraveler
{
    [Serializable]
    public class ShoppingItemConfig
{
    public string ItemName { get; set; } = ""; 
    public uint ResolvedItemId { get; set; } = 0; 
    
    public int TargetQuantity { get; set; } = 99;
    public int MaxUnitPrice { get; set; } = 1000; // <--- CHANGED THIS NAME AND DEFAULT
}

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public List<ShoppingItemConfig> ShoppingList { get; set; } = new List<ShoppingItemConfig>();
        
        public bool EnableAutoBuy { get; set; } = false;
        public bool EnableAutoConfirm { get; set; } = false;
        // Replace your old DC variables (CheckAllDCs, EnabledDCs, etc.) with these two:
public bool CheckAllPublicWorlds { get; set; } = false;
public int TargetDCIndex { get; set; } = 0; // 0 = Current DC, 1 = Aether, 2 = Crystal, etc.
        public bool DebugStepMode { get; set; } = true; // Turn on debugger by default for now
        public List<string> EnabledDCs { get; set; } = new List<string>();

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}