using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using Trady.Core;
using Trady.Core.Infrastructure;
using Trady.Core.Period;
using Quartz;
using Quartz.Impl;
using BtrexTrader.Interface;
using BtrexTrader.Data.MarketData;

namespace BtrexTrader.Data
{
    public static class BtrexData
    {
        public static List<Market> Markets { get; private set; }
        public static ConcurrentQueue<MarketDataUpdate> UpdateQueue { get; private set; }
        public static decimal USDrate { get; private set; }

        private static ISchedulerFactory schedFact = new StdSchedulerFactory();
        private static IScheduler sched = schedFact.GetScheduler();

        public static void NewData()
        {
            Markets = new List<Market>();
            UpdateQueue = new ConcurrentQueue<MarketDataUpdate>();
        }

        public static async Task StartDataUpdates()
        {
            //set USD value for conversions
            USDrate = await BtrexREST.getUSD();

            //Start crontriggered jobs:
            sched.Start();
            sched.ScheduleJob(buildCandles, candleTrigger);

            //Begin Dequeue Thread:
            var DequeueThread = new Thread(() => ProcessQueue());
            DequeueThread.IsBackground = true;
            DequeueThread.Name = "Update-Dequeue-Thread";
            DequeueThread.Start();
        }



        public static void ProcessQueue()
        {
            var pause = TimeSpan.FromMilliseconds(100);
            while (true)
            {
                if (UpdateQueue.IsEmpty)
                {
                    // no pending updates available pause
                    Thread.Sleep(pause);
                    continue;
                }

                bool tryDQ = false;
                while (!tryDQ)
                {
                    MarketDataUpdate mdUpdate = new MarketDataUpdate();

                    tryDQ = UpdateQueue.TryDequeue(out mdUpdate);

                    if (tryDQ)
                    {
                        //foreach (OrderBook book in Books)
                        foreach (Market market in Markets)
                        {
                            if (mdUpdate.MarketName != market.MarketDelta)
                                continue;
                            else if (mdUpdate.Nounce <= market.Nounce)
                                break;
                            else if (mdUpdate.Nounce > (market.Nounce + 1))
                            {
                                //IF NOUNCE IS DE-SYNCED, WIPE BOOK AND RE-SNAP
                                Console.WriteLine("    !!!!ERR>>  NOUNCE OUT OF ORDER! " + mdUpdate.MarketName + " BOOK-DSYNC.  {0}, {1}", market.Nounce, mdUpdate.Nounce);
                                foreach (Market mk in Markets)
                                {
                                    if (mk.MarketDelta == mdUpdate.MarketName)
                                    {
                                        Markets.Remove(mk);
                                        break;
                                    }
                                }

                                //Request MarketQuery from websocket, and OpenBook() with new snapshot
                                MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", mdUpdate.MarketName).Result;
                                marketQuery.MarketName = mdUpdate.MarketName;
                                OpenMarket(marketQuery).Wait();
                                Console.WriteLine("    [BOOK RE-SYNCED]");
                                break;
                            }
                            else
                                market.SetUpdate(mdUpdate);
                        }
                    }
                }
            }
        }

        public static async Task OpenMarket(MarketQueryResponse snapshot)
        {
            Market market = new Market(snapshot);
            await market.TradeHistory.Resolve5mCandles();
            Markets.Add(market);

        }


        private static void BuildAll5mCandles()
        {
            foreach (Market market in Markets)
            {
                DateTime NextCandleStart = market.TradeHistory.LastStoredCandle.AddMinutes(5);

                //If there is no trade data after the last candle period, or if next candle period is unfinished, do nothing:
                if ((market.TradeHistory.RecentFills.Last().TimeStamp < NextCandleStart) || (NextCandleStart >= DateTime.UtcNow))
                    return;

                //If we have trade data, but there are periods where no trades took place (skip those candles):
                while (market.TradeHistory.RecentFills.First().TimeStamp >= NextCandleStart.AddMinutes(5))
                    NextCandleStart = NextCandleStart.AddMinutes(5);
                
                //Build next candle from RecentFill data
                market.TradeHistory.BuildCandleFromRecentFills(NextCandleStart);
            }
        
        }


        private static void DumpDataToSQLite()
        {
            //DUMP CANDLE DATA INTO SQLite & CULL RecentFills TO CONSERVE MEMORY (TRIGGERED EVERY 3 HOURS)
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + HistoricalData.dbName + ";Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(conn))
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        foreach (Market market in Markets)
                        {
                            market.TradeHistory.SavePurgeCandlesSQLite(cmd);
                            market.TradeHistory.CullRecentFills();
                        }

                        tx.Commit();
                    }
                }

                conn.Close();
            }
        }

        
        //CREATE CRON-JOBS
        static IJobDetail buildCandles = JobBuilder.Create<Build5mCandles>()
            .WithIdentity("candlesJob", "group1")
            .Build();

        static IJobDetail DataCleanup = JobBuilder.Create<CandleDataCleanUp>()
            .WithIdentity("cleanUpJob", "group1")
            .Build();

        //CREATE CRON-TRIGGERS
        static ITrigger candleTrigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithCronSchedule("6 0/5 * ? * * *")
            .Build();

        static ITrigger cleanUpTrigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithCronSchedule("0 32 0/3 * * ? *")
            .Build();

        //DEFINE CRON-JOBS
        public class Build5mCandles : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                BuildAll5mCandles();
            }
        }

        public class CandleDataCleanUp : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                DumpDataToSQLite();
            }
        }

        
    }


    public class CandleImporter : IImporter
    {
        public async Task<IReadOnlyList<Candle>> ImportAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null, PeriodOption period = PeriodOption.Daily, CancellationToken token = default(CancellationToken))
        {
            //TODO:COMPLETE IMPORTER - PeriodOption missing 5m, does this matter?

            return null;
        }
    }

    
}
