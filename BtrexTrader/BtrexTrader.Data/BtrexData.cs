using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BtrexTrader.Interface;

namespace BtrexTrader.Data
{
    public static class BtrexData
    {
        public static List<OrderBook> Books { get; private set; }  
        public static ConcurrentQueue<MarketDataUpdate> UpdateQueue { get; private set; }

        public static decimal USDrate;

        public static async void StartData()
        {
            Books = new List<OrderBook>();
            UpdateQueue = new ConcurrentQueue<MarketDataUpdate>();

            //set USD value for conversions
            USDrate = await BtrexREST.getUSD();

            var DequeueThread = new Thread(() => ProcessQueue());
            DequeueThread.IsBackground = true;
            DequeueThread.Name = "Update-Dequeue-Thread";
            DequeueThread.Start();
        }


        public static async Task UpdateHistData()
        {
            //await new HistoricalData().UpdateHistData();
            new HistoricalData().UpdateOrCreateCSVs();
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
                        foreach (OrderBook book in Books)
                        {
                            if (mdUpdate.MarketName != book.MarketDelta)
                                continue;
                            else if (mdUpdate.Nounce <= book.Nounce)
                                break;
                            else if (mdUpdate.Nounce > (book.Nounce + 1))
                            {
                                //IF NOUNCE IS DE-SYNCED, WIPE BOOK AND RE-SNAP
                                Console.WriteLine("    !!!!ERR>>  NOUNCE OUT OF ORDER! " + mdUpdate.MarketName + " BOOK-DSYNC.");
                                foreach (OrderBook bk in Books)
                                {
                                    if (bk.MarketDelta == mdUpdate.MarketName)
                                    {
                                        Books.Remove(bk);
                                        break;
                                    }
                                }

                                //Request MarketQuery from websocket, and OpenBook() with new snapshot
                                MarketQueryResponse marketQuery = BtrexWS.btrexHubProxy.Invoke<MarketQueryResponse>("QueryExchangeState", mdUpdate.MarketName).Result;
                                OpenBook(marketQuery);
                                Console.WriteLine("    [BOOK RE-SYNCED]");
                                break;
                            }
                            else
                                book.SetUpdate(mdUpdate);
                        }
                    }
                }
            }
        }

        public static void OpenBook(MarketQueryResponse snapshot)
        {
            OrderBook book = new OrderBook(snapshot);
            Books.Add(book);
        }

       
    }


}
