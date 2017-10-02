using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BtrexTrader.Data;
using System.Threading;
using System.Diagnostics;
using BtrexTrader.Interface;
using BtrexTrader.TripletStrategy;

namespace BtrexTrader.Control
{
    class BtrexTradeController
    {
        public TripletTrader TripletTrader = new TripletTrader();

        public void StartWork()
        {
            var WorkThread = new Thread(() => ScanMarkets());
            WorkThread.IsBackground = true;
            WorkThread.Name = "Market-Scanning/Work-Thread";
            WorkThread.Start();
        }

        private async void ScanMarkets()
        {
            //THIS DOES SETUP/INIT:
            await TripletTrader.Initialize();

            while (true)
            {
                //Parallel.ForEach<Market>(btrexData.Markets, m => ScanMarket(m));
                Parallel.ForEach<TripletData>(TripletTrader.DeltaTrips, triplet => TripletTrader.CalcTrips(triplet));
                Thread.Sleep(100);
            }
        }

        //TODO: 
        //  CREATE MARKETSCANNER FOLDER AND CLASS
        //  SEE STICKY NOTE 

    }
}
