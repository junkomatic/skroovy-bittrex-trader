using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BtrexTrader.Interface;
using BtrexTrader.Data.MarketData;

namespace BtrexTrader.Data
{
    public static class BtrexData
    {
        public static List<Market> Markets { get; private set; }
        public static ConcurrentQueue<MarketDataUpdate> UpdateQueue { get; private set; }
        public static decimal USDrate { get; private set; }

        public static async Task StartData()
        {
            Markets = new List<Market>();
            UpdateQueue = new ConcurrentQueue<MarketDataUpdate>();

            //set USD value for conversions
            USDrate = await BtrexREST.getUSD();

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
                                Console.WriteLine("    !!!!ERR>>  NOUNCE OUT OF ORDER! " + mdUpdate.MarketName + " BOOK-DSYNC.");
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
            //Console.WriteLine("***");
            await market.TradeHistory.Resolve5mCandles();
            Markets.Add(market);
        }
        
    }


}
