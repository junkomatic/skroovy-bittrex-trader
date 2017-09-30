using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System.Threading;
using BtrexTrader.Control;

namespace BtrexTrader
{
    class Program
    {
        
        private static BtrexTradeController BtrexRobot = new BtrexTradeController();


        static void Main(string[] args)
        {
            //UNCOMMENT TO SHOW SIGNALR-WEBSOCKET DEBUG:
            //hubConnection.TraceLevel = TraceLevels.All;
            //hubConnection.TraceWriter = Console.Out;


            //while (true)
            {
                try
                {
                    RunAsync().Wait();
                }
                catch (Exception e)
                {                                     
                    Console.Write("\r\n\r\n!!!!TOP LVL ERR>> " + e.InnerException.Message);
                    //Thread.Sleep(5000);
                }
            }
        }

        static async Task RunAsync()
        {
            //CREATE hubProxy, REGISTER CALLBACKS, CONNECT TO HUB:
            BtrexWS.btrexHubProxy = BtrexWS.hubConnection.CreateHubProxy("coreHub");
            BtrexWS.btrexHubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexRobot.UpdateEnqueue(update));
            //btrexHubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));
            await BtrexWS.hubConnection.Start();


            //SUBSCRIBE TO MARKET DELTAS TO TRACK ORDERBOOKS & TRADE EVENTS
            await subscribeOB("BTC-ETH");


            //TRIPLETS STUFF
            if (BtrexRobot.watchOnly)
            {
                List<string> topMarkets = await BtrexRobot.GetTopMarkets();
                foreach (string mk in topMarkets)
                {
                    await SubTriplet(mk);
                }
            }
            else
            {
                await SubTriplet("NEO");
                await SubTriplet("QTUM");
                await SubTriplet("OMG");
                await SubTriplet("PAY");
                await SubTriplet("PTOY");
            }

            

            Console.ReadLine();
            Console.ReadLine();
        }

        

        private async static Task subscribeOB(string delta)
        {
            await BtrexWS.btrexHubProxy.Invoke("SubscribeToExchangeDeltas", delta);
            MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", delta).Result;
            marketQuery.MarketName = delta;
            BtrexRobot.OpenBook(marketQuery);
        }

        private async static Task SubTriplet(string COIN)
        {
            string BTCdelta = "BTC-" + COIN;
            string ETHdelta = "ETH-" + COIN;
            await Task.WhenAll(subscribeOB(BTCdelta), subscribeOB(ETHdelta));
            BtrexRobot.AddDoublet(BTCdelta, ETHdelta);
        }

    }

    public static class BtrexWS
    {
        public readonly static HubConnection hubConnection = new HubConnection("https://socket.bittrex.com/");
        public static IHubProxy btrexHubProxy;
    }


}
