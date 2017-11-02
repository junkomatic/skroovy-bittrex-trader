using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BtrexTrader.Data;
using System.Threading;
using System.Diagnostics;
using BtrexTrader.Interface;
using BtrexTrader.Strategy.TripletStrategy;
using BtrexTrader.Strategy.Demo;
using BtrexTrader.Strategy.EMAofRSI1;

namespace BtrexTrader.Control
{
    class BtrexTradeController
    {
        private EofR1control eofR1Control = new EofR1control();
        //private NewStratControl NewStrat = new NewStratControl();
        //private TripletTrader TripletTrader = new TripletTrader();

        public async Task InitializeMarkets()
        {
            await eofR1Control.Initialize();
            //await NewStrat.Initialize();
            //await TripletTrader.Initialize();
        }

        public void StartWork()
        {
            
            
            
            
            //DEMO STRAT:
            //NewStrat.StartMarketsDemo().Wait();


            //TRIPLET STRAT:
            //var WorkThread = new Thread(() => ScanMarkets());
            //WorkThread.IsBackground = true;
            //WorkThread.Name = "Market-Scanning/Work-Thread";
            //WorkThread.Start();
        }

        private async void ScanMarkets()
        {
            while (true)
            {
                //Parallel.ForEach<TripletData>(TripletTrader.DeltaTrips, triplet => TripletTrader.CalcTrips(triplet));
                Thread.Sleep(100);
            }
        }

    }
}
