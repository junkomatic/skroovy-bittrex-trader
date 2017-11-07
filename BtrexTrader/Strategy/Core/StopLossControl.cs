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
        //Stopwatch



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
                foreach (StopLoss stop in SL_Book)
                {
                    //Check to move up (trailing) more frequently than check to exe
                    
                    









                }



                Thread.Sleep(WatchFrequency);
            }
        }


        public static void RegisterStopLoss()
        {

        }


    }



    public class StopLoss
    {
        Action Callback;
    }


}
