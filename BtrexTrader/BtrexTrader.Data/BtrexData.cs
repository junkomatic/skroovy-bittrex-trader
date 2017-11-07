using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.SQLite;
using Quartz;
using Quartz.Impl;
using BtrexTrader.Interface;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Data
{
    public static class BtrexData
    {
        public static ConcurrentDictionary<string, Market.Market> Markets { get; private set; }
        public static ConcurrentQueue<MarketDataUpdate> UpdateQueue { get; private set; }
        public static decimal USDrate { get; private set; }

        private static ISchedulerFactory schedFact = new StdSchedulerFactory();
        private static IScheduler sched = schedFact.GetScheduler();

        public static void NewData()
        {
            Markets = new ConcurrentDictionary<string, Market.Market>();
            UpdateQueue = new ConcurrentQueue<MarketDataUpdate>();
        }

        public static async Task StartDataUpdates()
        {
            //set USD value for conversions
            USDrate = await BtrexREST.getUSD();

            //Start crontriggered jobs:
            sched.Start();
            sched.ScheduleJob(BuildCandles, candleTrigger);
            //sched.ScheduleJob(DataCleanup, cleanUpTrigger);

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
                do
                {
                    MarketDataUpdate mdUpdate = new MarketDataUpdate();
                    tryDQ = UpdateQueue.TryDequeue(out mdUpdate);
                    if (tryDQ)
                    {
                        if (mdUpdate.Nounce <= Markets[mdUpdate.MarketName].Nounce || Markets[mdUpdate.MarketName] == null)
                            break;
                        else if (mdUpdate.Nounce > Markets[mdUpdate.MarketName].Nounce + 1)
                        {
                            //IF NOUNCE IS DE-SYNCED, WIPE BOOK AND RE-SNAP
                            Console.WriteLine("    !!!!ERR>>  NOUNCE OUT OF ORDER! " + mdUpdate.MarketName + " BOOK-DSYNC.  {0}, {1}", Markets[mdUpdate.MarketName].Nounce, mdUpdate.Nounce);
                            bool removed;
                            do
                            {
                                Market.Market m;
                                removed = Markets.TryRemove(mdUpdate.MarketName, out m);
                            } while (!removed);
                            
                            //Request MarketQuery from websocket, and OpenBook() with new snapshot
                            MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", mdUpdate.MarketName).Result;
                            marketQuery.MarketName = mdUpdate.MarketName;
                            OpenMarket(marketQuery).Wait();
                            Console.WriteLine("    [BOOK RE-SYNCED]");
                            break;

                        }
                        else
                            Markets[mdUpdate.MarketName].SetUpdate(mdUpdate);
                        
                    }
                } while (!tryDQ);
            }
        }

        public static async Task OpenMarket(MarketQueryResponse snapshot)
        {
            Market.Market market = new Market.Market(snapshot);
            await market.TradeHistory.Resolve5mCandles();
            bool added;
            do
            {
                added = Markets.TryAdd(market.MarketDelta, market);
            } while (!added);
        }

        
        private static void BuildAll5mCandles()
        {
            foreach (Market.Market market in Markets.Values)
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
                        foreach (Market.Market market in Markets.Values)
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
        static IJobDetail BuildCandles = JobBuilder.Create<Build5mCandles>()
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


}
