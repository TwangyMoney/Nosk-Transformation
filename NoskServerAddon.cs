#pragma warning disable 1591
using Hkmp.Api.Server;
using HkmpPouch;

namespace Nosk_Transformation.HKMP
{
    public class NoskServerAddon : ServerAddon
    {
        private PipeServer _pouchServer;

        public override void Initialize(IServerApi serverApi)
        {
            _pouchServer = new PipeServer("Nosk_Transformation");
            new NoskServerNet(Logger, this, serverApi.NetServer);
        }

        protected override string Name => "Nosk_Transformation";
        protected override string Version => "1.0.0";
        public override bool NeedsNetwork => true;
    }
}
