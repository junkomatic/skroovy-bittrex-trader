using System;
using System.Collections.Concurrent;
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
        private static ConcurrentDictionary<string, StopLoss> SL_Book = new ConcurrentDictionary<string, StopLoss>();
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


        private static void WatchMarkets()
        {
            while (true)
            {
                if (SL_Book.Count == 0)
                {
                    Thread.Sleep(500);
                    continue;
                }

                foreach (var stop in SL_Book)
                {
                    //TODO: Check to move up (trailing) more frequently than check to execute
                    //After execution, call callback









                }



                Thread.Sleep(WatchFrequency);
            }
        }


        public static void RegisterStopLoss(StopLoss sl, string uniqueIdentifier)
        {
            bool added;
            do
            {
                added = SL_Book.TryAdd(uniqueIdentifier, sl);
            } while (!added);
            
        }

        public static void CancelStopLoss(string uniqueIdentifier)
        {
            bool removed;
            do
            {
                removed = SL_Book.TryRemove(uniqueIdentifier, out var s);
            } while (!removed);
        }
    }



    public class StopLoss
    {
        public string MarketDelta { get; set; }
        public decimal StopRate { get; set; }
        public decimal Quantity { get; set; }
        //Callbacks to Strategy, containing CandlePeriod parameter(optional):
        public Action<string, decimal, string> MovedCallback { get; set; }
        public Action<GetOrderResponse, string> ExecutionCallback { get; set; }
        public string CandlePeriod { get; set; }

        public StopLoss(string mDelta, decimal rate, decimal qty, Action<string, decimal, string> MOVEDcBack = null, Action<GetOrderResponse, string> EXEcBack = null, string period = null)
        {
            MarketDelta = mDelta;
            StopRate = rate;
            Quantity = qty;

            if (MOVEDcBack != null)
                MovedCallback = MOVEDcBack;

            if (EXEcBack != null)
                ExecutionCallback = EXEcBack;

            if (period != null)
                CandlePeriod = period;
        }
    }


}
