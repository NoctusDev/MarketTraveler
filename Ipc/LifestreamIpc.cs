using Dalamud.Plugin.Ipc;
using System;

namespace MarketTraveler.Ipc
{
    public class LifestreamIpc
    {
        private readonly ICallGateSubscriber<string, bool> _changeWorld;
        private readonly ICallGateSubscriber<bool> _isBusy;
        private readonly ICallGateSubscriber<string, bool> _aethernetTeleport;
        private readonly ICallGateSubscriber<uint, byte, bool> _teleport;
        private readonly ICallGateSubscriber<string, object> _executeCommand;

        public LifestreamIpc()
        {
            _changeWorld = Service.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
            _isBusy = Service.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            _aethernetTeleport = Service.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
            _teleport = Service.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
            _executeCommand = Service.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        }

        public bool ChangeWorld(string world)
        {
            // 1. The Bulletproof Bypass: Fire Lifestream's chat command directly through Dalamud!
            if (Service.CommandManager.ProcessCommand($"/li {world}"))
            {
                Service.Log.Info($"Successfully triggered '/li {world}' via CommandManager.");
                return true;
            }

            // 2. Try the Lifestream ExecuteCommand IPC
            try 
            { 
                _executeCommand.InvokeAction($"/li {world}");
                return true;
            }
            catch 
            {
                // 3. Try the ChangeWorld IPC
                try 
                { 
                    return _changeWorld.InvokeFunc(world); 
                }
                catch (Exception ex)
                {
                    Service.Log.Error("Lifestream failed to process travel. Is Lifestream installed and enabled in /xlplugins?");
                    return false;
                }
            }
        }

        public bool IsBusy()
        {
            try { return _isBusy.InvokeFunc(); }
            catch 
            { 
                return false; 
            } 
        }
        
        public bool AethernetTeleport(string destination)
        {
            // Bypass for internal city travel
            if (Service.CommandManager.ProcessCommand($"/li {destination}"))
            {
                return true;
            }

            try { return _aethernetTeleport.InvokeFunc(destination); }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Teleport(uint destination, byte subIndex)
        {
            try { return _teleport.InvokeFunc(destination, subIndex); }
            catch (Exception)
            {
                return false;
            }
        }
    }
}