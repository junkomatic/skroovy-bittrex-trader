using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using BtrexTrader.Data;

namespace BtrexTrader.Interface
{
    public static class BtrexWS
    {
        public readonly static HubConnection hubConnection = new HubConnection("https://socket.bittrex.com/");
        public static IHubProxy btrexHubProxy;

        public static async Task subscribeOB(string delta)
        {
            await BtrexWS.btrexHubProxy.Invoke("SubscribeToExchangeDeltas", delta);
            MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", delta).Result;
            marketQuery.MarketName = delta;
            BtrexData.OpenBook(marketQuery);
        }
    }

}
