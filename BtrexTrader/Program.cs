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
        }


        static async Task RunAsync()
        {
            await HistoricalData.UpdateHistData();

            await BtrexData.StartData();
            await BtrexWS.Connect();

            //init markets here, before starting work thread




            //BtrexController.StartWork();


            await BtrexWS.subscribeMarket("BTC-XLM");
            await BtrexData.RectifyCandles("BTC-XLM");


            Console.WriteLine("\r\n\r\n-PRESS ENTER 3 TIMES TO EXIT-");
            Console.ReadLine();
            Console.ReadLine();
            Console.ReadLine();
        }
    }


}
