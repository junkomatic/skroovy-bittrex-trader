using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BtrexTrader.TripletStrat;

namespace BtrexTrader.Data
{
    class BtrexData
    {
        public List<OrderBook> Books { get; private set; }
        public List<TripletData> DeltaTrips { get; private set; }       
        public ConcurrentQueue<MarketDataUpdate> UpdateQueue { get; private set; }


        public BtrexData()
        {
            Books = new List<OrderBook>();
            UpdateQueue = new ConcurrentQueue<MarketDataUpdate>();
            DeltaTrips = new List<TripletData>();

            var DequeueThread = new Thread(() => ProcessQueue());
            DequeueThread.IsBackground = true;
            DequeueThread.Name = "Update-Dequeue-Thread";
            DequeueThread.Start();
        }



        public void ProcessQueue()
        {
            var pause = TimeSpan.FromMilliseconds(35);
            while (true)
            {
                if (UpdateQueue.IsEmpty)
                {
                    // no pending actions available. pause
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
                                //Console.WriteLine("ERR>>  NOUNCE OUT OF ORDER! " + mdUpdate.MarketName + " BOOK-DSYNC.");
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
                                break;
                            }                              
                            else
                                book.SetUpdate(mdUpdate);
                        }
                    }
                }
            }  
        }

        public void OpenBook(MarketQueryResponse snapshot)
        {
            OrderBook book = new OrderBook(snapshot);
            Books.Add(book);
        }

        public void AddTripletDeltas(string BTCdelta, string ETHdelta)
        {
            OrderBook BTCbk = null;
            OrderBook ETHbk = null;
            OrderBook B2Ebk = (OrderBook)Books[0].Clone();

            foreach (OrderBook book in Books)
            {
                if (BTCbk == null && book.MarketDelta == BTCdelta)
                {
                    BTCbk = (OrderBook)book.Clone();
                }
                else if (ETHbk == null && book.MarketDelta == ETHdelta)
                {
                    ETHbk = (OrderBook)book.Clone();
                }
                if (BTCbk != null && ETHbk != null)
                {
                    DeltaTrips.Add(new TripletData(BTCbk, ETHbk, B2Ebk));
                    Console.WriteLine("###TRIPLET ADDED: [{0}]", BTCdelta.Substring(4));
                    break;
                }
            }
            if (BTCbk == null || ETHbk == null)
            {
                Console.WriteLine("ERR>> DOUBLET-CLONE NOT SET!!!!!!!!!!!!!");
            }
        }
    }

    
}
