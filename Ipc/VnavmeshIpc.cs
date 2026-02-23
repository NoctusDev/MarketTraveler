using Dalamud.Plugin.Ipc;
using System;
using System.Numerics;

namespace MarketTraveler.Ipc
{
    public class VnavmeshIpc
    {
        private readonly ICallGateSubscriber<bool> _navIsReady;
        private readonly ICallGateSubscriber<Vector3, bool, bool> _simpleMove;
        private readonly ICallGateSubscriber<bool> _pathIsRunning;
        private readonly ICallGateSubscriber<object> _pathStop;

        public VnavmeshIpc()
        {
            _navIsReady = Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
            _simpleMove = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
            _pathIsRunning = Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
            _pathStop = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        }

        public bool IsReady()
        {
            try { return _navIsReady.InvokeFunc(); }
            catch { return false; }
        }

        public bool PathfindAndMoveTo(Vector3 dest, bool fly)
        {
            try { return _simpleMove.InvokeFunc(dest, fly); }
            catch (Exception ex)
            {
                Service.Log.Error(ex, "vnavmesh PathfindAndMoveTo failed");
                return false;
            }
        }

        public bool IsRunning()
        {
             try { return _pathIsRunning.InvokeFunc(); }
             catch { return false; }
        }

        public void Stop()
        {
             try { _pathStop.InvokeAction(); }
             catch { }
        }
    }
}