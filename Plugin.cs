using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MarketTraveler.Ipc;
using MarketTraveler.Logic;
using System.Reflection;

namespace MarketTraveler
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "MarketTraveler";
        private const string CommandName = "/market";

        private IDalamudPluginInterface PluginInterface { get; init; }
        private ICommandManager CommandManager { get; init; }
        public Configuration Configuration { get; init; }
        public PluginUI PluginUi { get; init; }
        
        // Logic components
        public LifestreamIpc Lifestream { get; init; }
        public VnavmeshIpc Vnavmesh { get; init; }
        public NavigationManager Navigation { get; init; }
        public MarketBoardAuto MarketBoardAuto { get; init; } 
        public MarketTravelerController Controller { get; init; }

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

            // Initialize Services
            pluginInterface.Create<Service>();

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // Initialize Components
            this.Lifestream = new LifestreamIpc();
            this.Vnavmesh = new VnavmeshIpc();
            
            // UI
            this.PluginUi = new PluginUI(this.Configuration, this);

            this.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the MarketTraveler configuration window"
            });
            
            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            
            // --- ADDED: The Main UI Callback for the Dalamud Plugin Installer ---
            this.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
            
            // Logic initialization
            this.Navigation = new NavigationManager(this);
            this.MarketBoardAuto = new MarketBoardAuto(this); 
            this.Controller = new MarketTravelerController(this);
        }

        public void Dispose()
        {
            this.PluginInterface.UiBuilder.Draw -= DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            
            // --- ADDED: Cleanup for the Main UI Callback ---
            this.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
            
            this.CommandManager.RemoveHandler(CommandName);
            
            this.Controller.Dispose();
            this.MarketBoardAuto.Dispose(); 
            this.Navigation.Dispose();
            this.PluginUi.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            // in response to the slash command
            this.PluginUi.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.Visible = true;
        }

        // --- ADDED: The toggle method triggered by the Dalamud menu button ---
        private void ToggleMainUI()
        {
            this.PluginUi.Visible = !this.PluginUi.Visible;
        }
    }
}