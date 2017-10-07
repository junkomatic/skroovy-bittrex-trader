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
                    Console.ReadLine();
                    //Thread.Sleep(5000);
                }
            }
        }

        static async Task RunAsync()
        {
            await BtrexData.UpdateHistData();
            


            //BtrexData.StartData();
            //await BtrexWS.Connect();
            //BtrexController.StartWork();

            Console.WriteLine("\r\n\r\n-PRESS ENTER 3 TIMES TO EXIT-");
            Console.ReadLine();
            Console.ReadLine();
            Console.ReadLine();
        }
    }


}
