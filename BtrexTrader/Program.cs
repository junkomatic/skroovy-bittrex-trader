using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            //UNCOMMENT 'WHILE' TO RESTART ON FAILURE
            //while (true)
            {
                try
                {
                    RunAsync().Wait();
                }
                catch (Exception e)
                {
                    Console.Write("\r\n\r\n!!!!TOP LVL ERR>> " + e.InnerException.Message);
                    Console.ReadLine();
                    //Thread.Sleep(5000);
                }
            }

            Console.WriteLine("\r\n\r\n-PRESS ENTER 3 TIMES TO EXIT-");
            Console.ReadLine();
            Console.ReadLine();
            Console.ReadLine();
        }


        static async Task RunAsync()
        {
            //UPDATE LOCALLY STORED 5m CANDLES, AND .CSV RECORDS:
            await HistoricalData.UpdateHistData();

            //INITIALIZE DATA, THEN CONNECT WEBSOCKET
            await BtrexData.NewData();
            await BtrexWS.Connect();

            //SUBSCRIBE TO DESIRED MARKETS, THEN START-DATA-UPDATES:
            await BtrexWS.subscribeMarket("BTC-XLM");

            await BtrexData.StartDataUpdates();
            
            //START CALC-STRATEGY WORK:
            //BtrexController.StartWork();
        }
    }


}
