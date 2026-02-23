using System;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketTraveler.Helpers
{
    public static unsafe class ClickHelper
    {
        public static void ClickRadioButton(AtkUnitBase* addon, int index, uint eventType = 3)
        {
            if (addon == null) return;
            
            var values = stackalloc AtkValue[5];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[0].Int = (int)eventType;
            values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[1].Int = index;
            values[2].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt;
            values[2].UInt = 0;
            values[3].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt;
            values[3].UInt = 0;
            values[4].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt;
            values[4].UInt = 0;
            
            addon->FireCallback(5, values);
        }
        
        public static void ClickListItem(AtkUnitBase* addon, int index)
        {
            if (addon == null) return;
            
            var values = stackalloc AtkValue[3];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[0].Int = 0;
            values[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[1].Int = index;
            values[2].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt;
            values[2].UInt = 0;
            
            addon->FireCallback(3, values);
        }
        
        public static void SendAction(AtkUnitBase* addon, params int[] values)
        {
            if (addon == null) return;
            
            var atkValues = stackalloc AtkValue[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                atkValues[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
                atkValues[i].Int = values[i];
            }
            
            addon->FireCallback((uint)values.Length, atkValues);
        }
    }
}