using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using BtrexTrader.Data;
using BtrexTrader.Interface;

namespace BtrexTrader.Strategy.Core
{
    public static class StopLossController
    {
        private static ConcurrentDictionary<string, StopLoss> SL_Book = new ConcurrentDictionary<string, StopLoss>();
        private static Thread StopLossThread;
        private static bool isStarted = false;

        private static readonly TimeSpan WatchFrequency = TimeSpan.FromSeconds(2);
        private static readonly int CheckExeEveryCycles = 4; 

            
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
            bool checkExe = false;
            int cycles = 0;

            while (true)
            {                
                if (SL_Book.Count == 0)
                {
                    Thread.Sleep(500);
                    continue;
                }

                cycles++;
                if (cycles >= CheckExeEveryCycles)
                    checkExe = true;

                foreach (var stop in SL_Book)
                {
                    if (checkExe)
                    {
                        if (BtrexData.Markets[stop.Value.MarketDelta].TradeHistory.RecentFills.Last().Rate <= stop.Value.StopRate)
                        {
                            //EXECUTE STOPLOSS, CALL CALLBACK
                            BtrexREST.TradeController.ExecuteStopLoss(stop.Value);
                            CancelStoploss(stop.Key);
                            continue;
                        }                        
                    }
                    
                    //CHECK TO RAISE SL USING CALLBACK FOR NEW RATE CALC:
                    stop.Value.ReCalcCallback(stop.Value.MarketDelta, stop.Value.StopRate, stop.Value.CandlePeriod);
                    
                }


                if (checkExe)
                {
                    cycles = 0;
                    checkExe = false;
                }
                
                Thread.Sleep(WatchFrequency);
            }
        }


        public static void RegisterStoploss(StopLoss sl, string uniqueIdentifier)
        {
            bool added;
            do
            {
                added = SL_Book.TryAdd(uniqueIdentifier, sl);
            } while (!added);
            
        }

        public static void CancelStoploss(string uniqueIdentifier)
        {
            bool removed;
            do
            {
                removed = SL_Book.TryRemove(uniqueIdentifier, out var s);
            } while (!removed);
        }

        public static void RaiseStoploss(string uniqueID, decimal newRate)
        {
            SL_Book[uniqueID].StopRate = newRate;
        }

        public static void Stop()
        {
            if (isStarted)
                StopLossThread.Abort();

            isStarted = false;
        }
    }



    public class StopLoss
    {
        public string MarketDelta { get; set; }
        public decimal StopRate { get; set; }
        public decimal Quantity { get; set; }
        //Callbacks to Strategy, containing CandlePeriod parameter(optional):
        public Action<string, decimal, string> ReCalcCallback { get; set; }
        public Action<GetOrderResult, string> ExecutionCallback { get; set; }
        public string CandlePeriod { get; set; }
        public bool virtualSL { get; set; }

        public StopLoss(string mDelta, decimal rate, decimal qty, Action<string, decimal, string> RECALCcBack = null, Action<GetOrderResult, string> EXEcBack = null, string period = null, bool isVirtual = false)
        {
            MarketDelta = mDelta;
            StopRate = rate;
            Quantity = qty;

            if (RECALCcBack != null)
                ReCalcCallback = RECALCcBack;

            if (EXEcBack != null)
                ExecutionCallback = EXEcBack;

            if (period != null)
                CandlePeriod = period;

            virtualSL = isVirtual; 
        }
    }


}
