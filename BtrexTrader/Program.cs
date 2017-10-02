using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System.Threading;
using BtrexTrader.Control;
using BtrexTrader.Interface;
using BtrexTrader.Data;

namespace BtrexTrader
{
    class Program
    {
        private static BtrexTradeController BtrexController = new BtrexTradeController();

        static void Main(string[] args)
        {
            //UNCOMMENT TO SHOW SIGNALR-WEBSOCKET DEBUG:
            //BtrexWS.hubConnection.TraceLevel = TraceLevels.All;
            //BtrexWS.hubConnection.TraceWriter = Console.Out;


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
            //START DATA-UPDATE THREAD
            BtrexData.StartData();

            //CREATE PROXY, REGISTER CALLBACKS, CONNECT TO HUB:
            BtrexWS.btrexHubProxy = BtrexWS.hubConnection.CreateHubProxy("coreHub");
            BtrexWS.btrexHubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexData.UpdateQueue.Enqueue(update));
            //btrexHubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));
            await BtrexWS.hubConnection.Start();

            BtrexController.StartWork();


            Console.ReadLine();
            Console.ReadLine();
            Console.ReadLine();
        }
    }

}
