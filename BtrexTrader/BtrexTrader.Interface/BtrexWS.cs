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

        public static async Task Connect()
        {
            //CREATE PROXY, REGISTER CALLBACKS, CONNECT TO HUB:
            BtrexWS.btrexHubProxy = BtrexWS.hubConnection.CreateHubProxy("coreHub");
            BtrexWS.btrexHubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexData.UpdateQueue.Enqueue(update));
            //btrexHubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));
            await hubConnection.Start();
        }
    }

}
