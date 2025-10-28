#pragma warning disable 1591
using Hkmp.Api.Server;

namespace Nosk_Transformation.HKMP
{
    public class NoskServerAddon : ServerAddon
    {
        public override void Initialize(IServerApi serverApi)
        {
            new NoskServerNet(Logger, this, serverApi.NetServer);
        }

        protected override string Name => "Nosk_Transformation";
        protected override string Version => "1.0.0";
        public override bool NeedsNetwork => true;
    }
}