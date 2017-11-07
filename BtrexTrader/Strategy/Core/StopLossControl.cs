using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BtrexTrader.Strategy.Core
{
    public static class StopLossController
    {
        private static List<StopLoss> SL_Book = new List<StopLoss>();
        private static Thread StopLossThread;
        private static bool isStarted = false;

        private static readonly TimeSpan WatchFrequency = TimeSpan.FromMilliseconds(500);
        //Stopwatch for differing TimeSpans up/down

            
        public static void StartWatching()
        {
            if (!isStarted)
            {
                StopLossThread = new Thread(() => WatchMarkets());
                StopLossThread.IsBackground = true;
                StopLossThread.Name = "StopLoss-WatchMarkets-Thread";
                StopLossThread.Start();
                isStarted = true;
            }

        }


        public static void WatchMarkets()
        {
            while (true)
            {
                if (SL_Book.Count == 0)
                {
                    Thread.Sleep(500);
                    continue;
                }

                foreach (StopLoss stop in SL_Book)
                {
                    //Check to move up (trailing) more frequently than check to execute
                    
                    









                }



                Thread.Sleep(WatchFrequency);
            }
        }


        public static void RegisterStopLoss(string delta, decimal rate, decimal qty, Action<string> callBack = null)
        {
            StopLoss sl = new StopLoss();
            sl.MarketDelta = delta;
            sl.StopRate = rate;
            sl.Quantity = qty;
            
            if (callBack != null)
                sl.Callback = callBack;

            SL_Book.Add(sl);
        }


    }



    public class StopLoss
    {
        public string MarketDelta { get; set; }
        public decimal StopRate { get; set; }
        public decimal Quantity { get; set; }
        
        //Callback to Strategy, containing CandlePeriod parameter(optional):
        public Action<string> Callback { get; set; }
        
    }


}
