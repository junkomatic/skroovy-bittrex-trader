using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System.Threading;
using BtrexTrader.Controller;

namespace BtrexTrader
{
    class Program
    {
        private static HubConnection hubConnection = new HubConnection("https://socket.bittrex.com/");
        private static IHubProxy btrexHubProxy;
        private static BtrexTradeController BtrexRobot = new BtrexTradeController();


        static void Main(string[] args)
        {
            //SHOW DEBUG:
            //hubConnection.TraceLevel = TraceLevels.All;
            //hubConnection.TraceWriter = Console.Out;



            while (true)
            {
                try
                {
                    RunAsync().Wait();
                }
                catch (Exception e)
                {
                    if (e.Message == "!!!!!!!!!!!!!INSUFFICIENT FUNDS")
                    {
                        for (int i = 0; i < 10; i++)
                            Console.Beep(900, 300);
                        Environment.Exit(0);
                    }
                        
                    Console.Write("\r!!!!TOP LVL ERR>>");
                    for (int i = 0; i < 3; i++)
                        Console.Beep(900, 200);
                    Thread.Sleep(5000);
                }
            }
        }

        static async Task RunAsync()
        {
            btrexHubProxy = hubConnection.CreateHubProxy("coreHub");
            btrexHubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexRobot.UpdateEnqueue(update));
            //btrexHubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));

            
            

            await hubConnection.Start();



            await subscribeOB("BTC-ETH");

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
                await SubTriplet("ptoy");
            }






            //await SubTriplet("FCT");
            //await SubTriplet("XRP");
            //await SubTriplet("BCC");
            //await SubTriplet("LTC");
            //await SubTriplet("XMR");
            //await SubTriplet("ADX");
            //await SubTriplet("GNT");
            //await SubTriplet("PAY");
            //await SubTriplet("ZEC");
            //await SubTriplet("STRAT");
            //await SubTriplet("ETC");
            //await SubTriplet("LGD");


            Console.ReadLine();
            Console.ReadLine();
        }

        

        private async static Task subscribeOB(string delta)
        {
            await btrexHubProxy.Invoke("SubscribeToExchangeDeltas", delta);
            MarketQueryResponse marketQuery = btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", delta).Result;
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


}
