using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

        public static async Task subscribeMarket(string delta)
        {
            await BtrexWS.btrexHubProxy.Invoke("SubscribeToExchangeDeltas", delta);
            MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", delta).Result;

            marketQuery.MarketName = delta;
            await BtrexData.OpenMarket(marketQuery);
        }

        public static async Task Connect()
        {
            //UNCOMMENT TO SHOW SIGNALR-WEBSOCKET DEBUG:
            //hubConnection.TraceLevel = TraceLevels.All;
            //hubConnection.TraceWriter = Console.Out;

            //CREATE PROXY, REGISTER CALLBACKS, CONNECT TO HUB:
            btrexHubProxy = BtrexWS.hubConnection.CreateHubProxy("coreHub");
            btrexHubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexData.UpdateQueue.Enqueue(update));
            //btrexHubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));
            Console.Write("Connecting Websocket...");
            await hubConnection.Start();
            Console.WriteLine("\rWebsocket Connected.      ");
        }
    }

}
