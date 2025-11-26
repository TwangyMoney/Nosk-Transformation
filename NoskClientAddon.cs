#pragma warning disable 1591
using Hkmp.Api.Client;
using HkmpPouch;
using UnityEngine;

namespace Nosk_Transformation.HKMP
{
    public class NoskClientAddon : ClientAddon
    {
        private static IClientApi _clientApi;
        private static PipeClient _pouchClient;

        public override void Initialize(IClientApi clientApi)
        {
            _clientApi = clientApi;

            new NoskClientNet(Logger, this, clientApi);

            _pouchClient = new PipeClient("Nosk_Transformation");
            _pouchClient.ServerCounterPartAvailable(available =>
            {
                //try
                //{
                    //if (available)
                    //{
                        //_clientApi.UiManager.ChatBox.AddMessage("Nosk Transformation network active on this server");
                        //Modding.Logger.Log("[Nosk_Transformation] Server counterpart available (PipeServer present).");
                    //}
                    //else
                    //{
                        //_clientApi.UiManager.ChatBox.AddMessage("Nosk Transformation is not active on this server");
                        //Modding.Logger.Log("[Nosk_Transformation] Server counterpart not available (PipeServer missing or failed to load).");
                    //}
                //}
                //catch { }
            });
        }

        protected override string Name => "Nosk_Transformation";
        protected override string Version => "1.0.0";
        public override bool NeedsNetwork => true;
    }
}
