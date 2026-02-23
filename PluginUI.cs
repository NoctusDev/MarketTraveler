using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets; 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;

namespace MarketTraveler
{
    public class PluginUI : IDisposable
    {
        private Configuration config;
        private Plugin Plugin;
        private bool visible = false;

        public bool Visible
        {
            get => visible;
            set => visible = value;
        }

        private readonly string[] dataCenters = { 
            "Current Data Center", 
            "Aether", "Crystal", "Dynamis", "Primal", 
            "Chaos", "Light", "Shadow", 
            "Materia", 
            "Elemental", "Gaia", "Mana", "Meteor" 
        };

        public PluginUI(Configuration config, Plugin plugin)
        {
            this.config = config;
            this.Plugin = plugin;
        }

        public void Draw()
        {
            if (!Visible) return;

            ImGui.SetNextWindowSize(new Vector2(600, 450), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(500, 350), new Vector2(float.MaxValue, float.MaxValue));

            if (ImGui.Begin("Market Traveler", ref visible))
            {
                DrawStatusHeader();
                DrawActiveTaskPanel(); 
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                
                DrawTravelSettings();
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                
                DrawShoppingListTable();
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                
                DrawActionControls();
            }
            ImGui.End();
        }

        private void DrawStatusHeader()
        {
            string statusText = "STATUS: IDLE";
            Vector4 statusColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f); 

            if (Plugin.Controller.IsRunning)
            {
                statusText = $"STATUS: {Plugin.Controller.CurrentStateName.ToUpper()}";
                statusColor = new Vector4(0.2f, 0.8f, 0.2f, 1.0f); 
            }
            else if (!Plugin.MarketBoardAuto.IsDone && Plugin.MarketBoardAuto.CurrentStateName != "Idle")
            {
                statusText = $"SHOPPING: {Plugin.MarketBoardAuto.CurrentStateName.ToUpper()}";
                statusColor = new Vector4(0.2f, 0.6f, 1.0f, 1.0f); 
            }

            CenterTextColored(statusText, statusColor);
        }

        private void DrawActiveTaskPanel()
        {
            var activeItem = Plugin.Controller.CurrentActiveItem;
            if (activeItem == null) return;

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "Currently Processing");

            string itemName = "Unknown Item";
            uint iconId = 0;
            
            var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet != null)
            {
                var row = sheet.GetRow(activeItem.ItemId);
                
                // FIXED: Struct null check removed, relying only on the RowId
                if (row.RowId != 0)
                {
                    itemName = row.Name.ToString();
                    iconId = row.Icon;
                }
            }

            if (iconId > 0)
            {
                var iconTexture = Service.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(iconId)).GetWrapOrDefault();
                if (iconTexture != null)
                {
                    // FIXED: ImGuiHandle was officially renamed to Handle in Dalamud v13
                    ImGui.Image(iconTexture.Handle, new Vector2(40, 40));
                    ImGui.SameLine();
                }
            }

            ImGui.BeginGroup();
            ImGui.TextUnformatted($"{itemName} (Max: {activeItem.MaxUnitPrice}g)");
            
            float progress = activeItem.TargetQty > 0 ? (float)activeItem.PurchasedQty / activeItem.TargetQty : 0f;
            ImGui.ProgressBar(progress, new Vector2(-1, 20), $"{activeItem.PurchasedQty} / {activeItem.TargetQty} Purchased");
            ImGui.EndGroup();
        }

        private void DrawTravelSettings()
        {
            ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), "Travel Routing");

            bool checkAll = config.CheckAllPublicWorlds;
            if (ImGui.Checkbox("Check All Public Worlds (Cross-DC)", ref checkAll))
            {
                config.CheckAllPublicWorlds = checkAll;
                config.Save();
            }

            if (config.CheckAllPublicWorlds) ImGui.BeginDisabled();

            ImGui.PushItemWidth(200);
            int dcIndex = config.TargetDCIndex;
            if (ImGui.Combo("Target Data Center", ref dcIndex, dataCenters, dataCenters.Length))
            {
                config.TargetDCIndex = dcIndex;
                config.Save();
            }
            ImGui.PopItemWidth();

            if (config.CheckAllPublicWorlds) ImGui.EndDisabled();
        }

        private void DrawShoppingListTable()
        {
            ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1.0f), "Shopping List");

            if (ImGui.BeginTable("ShoppingListTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Target Qty", ImGuiTableColumnFlags.WidthFixed, 80f);
                ImGui.TableSetupColumn("Max Gil/Item", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 50f); 
                ImGui.TableHeadersRow();

                for (int i = 0; i < config.ShoppingList.Count; i++)
                {
                    var item = config.ShoppingList[i];
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    string name = item.ItemName;
                    if (ImGui.InputText($"##name_{i}", ref name, 100))
                    {
                        item.ItemName = name;
                        
                        var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        if (sheet != null)
                        {
                            var foundItem = sheet.FirstOrDefault(x => x.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (foundItem.RowId != 0) 
                            {
                                item.ResolvedItemId = foundItem.RowId;
                            }
                        }
                        config.Save();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    int qty = item.TargetQuantity;
                    if (ImGui.InputInt($"##qty_{i}", ref qty, 0, 0))
                    {
                        item.TargetQuantity = Math.Max(1, qty);
                        config.Save();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    int price = item.MaxUnitPrice;
                    if (ImGui.InputInt($"##price_{i}", ref price, 0, 0))
                    {
                        item.MaxUnitPrice = Math.Max(1, price);
                        config.Save();
                    }

                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                    if (ImGui.Button($"X##remove_{i}", new Vector2(-1, 0)))
                    {
                        config.ShoppingList.RemoveAt(i);
                        config.Save();
                        i--; 
                    }
                    ImGui.PopStyleColor();
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("+ Add New Item"))
            {
                config.ShoppingList.Add(new ShoppingItemConfig());
                config.Save();
            }
        }

        private void DrawActionControls()
        {
            float buttonWidth = 200f;
            float buttonHeight = 40f;
            
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - buttonWidth) * 0.5f);

            if (!Plugin.Controller.IsRunning && Plugin.MarketBoardAuto.CurrentStateName == "Idle")
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f)); 
                if (ImGui.Button("START SHOPPING", new Vector2(buttonWidth, buttonHeight)))
                {
                    var activeItems = new List<Logic.ShoppingItem>();
                    var sheet = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    
                    foreach (var c in config.ShoppingList)
                    {
                        if (c.ResolvedItemId == 0 && !string.IsNullOrWhiteSpace(c.ItemName) && sheet != null)
                        {
                            var foundItem = sheet.FirstOrDefault(x => x.Name.ToString().Equals(c.ItemName.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (foundItem.RowId != 0) 
                            {
                                c.ResolvedItemId = foundItem.RowId;
                                config.Save();
                            }
                        }

                        if (c.ResolvedItemId > 0 && c.TargetQuantity > 0)
                        {
                            activeItems.Add(new Logic.ShoppingItem
                            {
                                ItemId = c.ResolvedItemId,
                                TargetQty = c.TargetQuantity,
                                MaxUnitPrice = c.MaxUnitPrice
                            });
                        }
                        else
                        {
                            Service.Log.Error($"[MarketTraveler] Ignored '{c.ItemName}': Invalid ID or Quantity.");
                        }
                    }

                    if (activeItems.Count > 0)
                    {
                        var worldsToVisit = new List<string>();

                        var aether = new[] { "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren" };
                        var crystal = new[] { "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera" };
                        var dynamis = new[] { "Halicarnassus", "Maduin", "Marilith", "Seraph", "Cuchulainn", "Golem", "Kraken", "Rafflesia" };
                        var primal = new[] { "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros" };
                        var chaos = new[] { "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan" };
                        var light = new[] { "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark" };

                        if (config.CheckAllPublicWorlds)
                        {
                            worldsToVisit.AddRange(aether); worldsToVisit.AddRange(crystal);
                            worldsToVisit.AddRange(dynamis); worldsToVisit.AddRange(primal);
                            worldsToVisit.AddRange(chaos); worldsToVisit.AddRange(light);
                        }
                        else
                        {
                            string targetDc = dataCenters[config.TargetDCIndex];
                            
                            if (targetDc == "Aether") worldsToVisit.AddRange(aether);
                            else if (targetDc == "Crystal") worldsToVisit.AddRange(crystal);
                            else if (targetDc == "Dynamis") worldsToVisit.AddRange(dynamis);
                            else if (targetDc == "Primal") worldsToVisit.AddRange(primal);
                            else if (targetDc == "Chaos") worldsToVisit.AddRange(chaos);
                            else if (targetDc == "Light") worldsToVisit.AddRange(light);
                            else worldsToVisit.AddRange(crystal); 
                        }

                        Plugin.Controller.Start(activeItems, worldsToVisit);
                    }
                    else
                    {
                        Service.ChatGui.PrintError("[MarketTraveler] Cannot start: No valid items found! Did you spell the item correctly?");
                    }
                }
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); 
                if (ImGui.Button("STOP / ABORT", new Vector2(buttonWidth, buttonHeight)))
                {
                    Plugin.Controller.Stop();
                    Plugin.MarketBoardAuto.ForceDone();
                }
                ImGui.PopStyleColor();
            }
        }

        private void CenterTextColored(string text, Vector4 color)
        {
            var windowWidth = ImGui.GetWindowSize().X;
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.TextColored(color, text);
        }

        public void Dispose() { }
    }
}