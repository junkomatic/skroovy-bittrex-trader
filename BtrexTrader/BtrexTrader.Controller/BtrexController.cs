﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BtrexTrader.Data;
using System.Threading;
using System.Diagnostics;

namespace BtrexTrader.Controller
{
    class BtrexTradeController
    {
        public BtrexData btrexData;
        public static BtrexREST btrexRESTClient;
        private static Stopwatch time = new Stopwatch();
        public bool watchOnly = false;

        bool SingleTradeReady = true;
        private int rots = 0;

        public BtrexTradeController()
        {
            btrexData = new BtrexData();
            btrexRESTClient = new BtrexREST();

            var TriCalcThread = new Thread(() => TriCalcTrade());
            TriCalcThread.IsBackground = true;
            TriCalcThread.Name = "Triplet-CalcAndTrade-Thread";
            TriCalcThread.Start();

            
        }

        private async void TriCalcTrade()
        {
            //Before work-loop, set USD value for conversions
            await btrexRESTClient.setUSD();

            var pause = TimeSpan.FromMilliseconds(100);

            while (true)
            {
                Parallel.ForEach<BookTriplet>(btrexData.DeltaTrips, triplet => CalcTrips(triplet));
                Thread.Sleep(pause);
            }
        }

        private async void CalcTrips(BookTriplet triplet)
        {


            GetOrderResponse ord3 = null;
            //TODO: CALC TRIPS AND TRADE
            const decimal wager = 0.012M;
            const decimal BTCreturnThreshhold = 0.00000330M;
            //const decimal BTC2USDrate = 4660.84M;
            TriCalcReturn LeftResult = triplet.CalcLeft(wager);


       

            if (!watchOnly && LeftResult.BTCresult > BTCreturnThreshhold && SingleTradeReady && !triplet.tradingState)
            {
                SingleTradeReady = false;
                triplet.tradingState = true;

                LimitOrderResponse orderResp2 = await btrexRESTClient.PlaceLimitOrder2(triplet.ETHdelta.MarketDelta, "sell", LeftResult.Trades2.Sum(x => x.Value), (LeftResult.Trades2.OrderByDescending(x => x.Key).Last().Key));
                LimitOrderResponse orderResp1 = await btrexRESTClient.PlaceLimitOrder(triplet.BTCdelta.MarketDelta, "buy", LeftResult.Trades1.Sum(x => x.Value), (LeftResult.Trades1.OrderBy(x => x.Key).Last().Key));              
                LimitOrderResponse orderResp3 = await btrexRESTClient.PlaceLimitOrder3(triplet.B2Edelta.MarketDelta, "sell", LeftResult.Trades3.Sum(x => x.Value), (LeftResult.Trades3.OrderByDescending(x => x.Key).Last().Key));

                //watchOnly = true;
                if (!orderResp2.success)
                {
                    Console.WriteLine("\r\n    !!!!ERR PLACE-ORDER2>> " + orderResp2.message);
                    throw new Exception("!!!!!!!!!!!!!INSUFFICIENT FUNDS"); 
                }
                else
                {
                    triplet.IDtrade2 = orderResp2.result.uuid;
                    //Console.WriteLine(triplet.IDtrade2);
                }

                if (!orderResp1.success)
                {
                    Console.WriteLine("\r\n    !!!!ERR PLACE-ORDER1>> " + orderResp1.message);
                    throw new Exception("!!!!!!!!!!!!!INSUFFICIENT FUNDS");
                }
                else
                {
                    triplet.IDtrade1 = orderResp1.result.uuid;
                    //Console.WriteLine(triplet.IDtrade1);
                }

                if (!orderResp3.success)
                {
                    Console.WriteLine("\r\n    !!!!ERR PLACE-ORDER3>> " + orderResp3.message);
                    throw new Exception("!!!!!!!!!!!!!INSUFFICIENT FUNDS");
                }
                else
                {
                    triplet.IDtrade3 = orderResp3.result.uuid;
                    //Console.WriteLine(triplet.IDtrade3);
                }



                

                Console.WriteLine("\r\nTRADES POSTED: [{0}]", triplet.Coin);
                Console.Beep(600, 200);
                //POST TRADE STUFF:
                //Thread.Sleep(400);


                GetOrderResponse ord1 = await btrexRESTClient.GetOrder(triplet.IDtrade1);
                while (!ord1.success)
                {
                    ord1 = await btrexRESTClient.GetOrder(triplet.IDtrade1);
                }
                //Console.WriteLine(ord1.result.OrderUuid + "..." + ord1.result.IsOpen);

                GetOrderResponse ord2 = await btrexRESTClient.GetOrder2(triplet.IDtrade2);
                while (!ord2.success)
                {
                    ord2 = await btrexRESTClient.GetOrder(triplet.IDtrade2);
                }
                //Console.WriteLine(ord2.result.OrderUuid + "..." + ord2.result.IsOpen);

                ord3 = await btrexRESTClient.GetOrder3(triplet.IDtrade3);
                while (!ord3.success)
                {
                    ord3 = await btrexRESTClient.GetOrder(triplet.IDtrade3);
                }
                //Console.WriteLine(ord3.result.OrderUuid + "..." + ord3.result.IsOpen);

                if (ord1.result.IsOpen || ord2.result.IsOpen || ord3.result.IsOpen)
                {
                    Thread.Sleep(1000);

                    OpenOrdersResponse orders = await btrexRESTClient.GetOpenOrders();
                    if (!orders.success)
                        Console.WriteLine("    !!!!ERR OPEN-ORDERS>> " + orders.message);

                    //Console.WriteLine("OPEN ORDERS COUNT: " + orders.result.Count);

                    if (orders.result.Count > 0)
                    {
                        time.Start();
                        while (orders.result.Count > 0)
                        {
                            if (time.Elapsed > TimeSpan.FromMinutes(1))
                            {
                                Console.WriteLine("\r\n!FORCE COMPLETING OPEN ORDERS...");
                                foreach (OpenOrdersResult ord in orders.result)
                                {
                                    if (ord.OrderType == "LIMIT_SELL")
                                    {
                                        bool completed = await MatchBottomAskUntilFilled(ord.OrderUuid, "qty");
                                        if (completed)
                                            Console.WriteLine("\r###FORCE COMPLETED SELL: [{0}]                                           ", ord.Exchange);
                                        else
                                            Console.WriteLine("\r\n    !!!!ERR FORCE-COMPLETE-SELL FAILL>>");
                                    }
                                    else if (ord.OrderType == "LIMIT_BUY")
                                    {
                                        bool completed = await MatchTopBidUntilFilled(ord.OrderUuid, "qty");
                                        if (completed)
                                            Console.WriteLine("\r###FORCE COMPLETED BUY: [{0}]                                            ", ord.Exchange);
                                        else
                                            Console.WriteLine("\r\n    !!!!ERR FORCE-COMPLETE-BUY FAILL>>");
                                    }
                                }
                                break;
                            }

                            if (time.Elapsed > TimeSpan.FromSeconds(25))
                            {
                                Console.Write("\r                                          \r!HANGING ORDERS:");
                                foreach (OpenOrdersResult ord in orders.result)
                                {
                                    Console.Write("  [{0}]", ord.Exchange);
                                }
                            }

                            Thread.Sleep(500);
                            orders = await btrexRESTClient.GetOpenOrders();
                            if (!orders.success)
                                Console.WriteLine("\r\n    !!!!ERR OPEN-ORDERS>> " + orders.message);
                        }
                        time.Reset();
                    }
                }
                rots++;
                Console.WriteLine("\r*#{0} Rotations COMPLETE* - {1}", rots, DateTime.Now.ToShortTimeString());

                //TODO: REPORTING...
                //Console.WriteLine(triplet.IDtrade1);
                //Console.WriteLine(triplet.IDtrade2);
                //Console.WriteLine(triplet.IDtrade3);

                Console.Beep();



                //bool balsChecked = false;
                //while (!balsChecked)
                //{
                //    balsChecked = true;
                //    GetBalancesResponse bals = await btrexRESTClient.GetBalances();                    
                //    foreach (BalancesResult bal in bals.result)
                //    {
                //        if (bal.Available < bal.Balance)
                //        {
                //            balsChecked = false;
                //            Console.WriteLine("Waiting on Balance: {0}", bal.Currency);
                //            Thread.Sleep(200);
                //        }
                //    }
                //}

                //GetOrderResponse ord1 = await btrexRESTClient.GetOrder(triplet.IDtrade1);
                //if (!ord1.success)
                //    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord1.message);
                //GetOrderResponse ord2 = await btrexRESTClient.GetOrder(triplet.IDtrade2);
                //if (!ord2.success)
                //    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord2.message);
                ord3 = await btrexRESTClient.GetOrder(triplet.IDtrade3);
                if (!ord3.success)
                    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord3.message);

                //Console.WriteLine("Trade1:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord1.result.Quantity, ord1.result.PricePerUnit, triplet.IDtrade1);
                //Console.WriteLine("Trade2:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord2.result.Quantity, ord2.result.PricePerUnit, triplet.IDtrade2);
                //Console.WriteLine("Trade3:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord3.result.Quantity, ord3.result.PricePerUnit, triplet.IDtrade3);

                decimal profit = (ord3.result.Quantity * Convert.ToDecimal(ord3.result.PricePerUnit) - ord3.result.CommissionPaid) - 0.012M;
                Console.WriteLine("PROFIT: {0:0.0#######}BTC(${1:0.00##})", profit, profit * btrexRESTClient.USDrate);


                triplet.Ready();
                SingleTradeReady = true;
                //watchOnly = false;
            }





            //REPORTING ONLY
            if (watchOnly)
            {
                TriCalcReturn RightResult = triplet.CalcRight(wager);
                decimal LeftReturnUSD = LeftResult.BTCresult * btrexRESTClient.USDrate;
                decimal RightReturnUSD = RightResult.BTCresult * btrexRESTClient.USDrate;
                if (LeftReturnUSD > 0.02M)
                {
                    if (triplet.tradingState == true)
                        return;
                    string ALT = triplet.BTCdelta.MarketDelta.Substring(4);
                    Console.WriteLine("{0} :: [{1}]  ===  ${2:0.00###} -L-", DateTime.Now, ALT, LeftReturnUSD);
                }
                if (RightReturnUSD > 0.02M)
                {
                    if (triplet.tradingState == true)
                        return;
                    string ALT = triplet.BTCdelta.MarketDelta.Substring(4);
                    Console.WriteLine("{0} :: [{1}]  ===  ${2:0.00###} -R-", DateTime.Now, ALT, RightReturnUSD);
                } 
            }
        }


        public void UpdateEnqueue(MarketDataUpdate update)
        {
            btrexData.UpdateQueue.Enqueue(update);
        }

        public void OpenBook(MarketQueryResponse snap)
        {
            btrexData.OpenBook(snap);
        }

        public void AddDoublet(string BTCdelta, string ETHdelta)
        {
            btrexData.AddTripletDeltas(BTCdelta, ETHdelta);
        }




        public async Task<List<string>> GetTopMarkets()
        {
            MarketSummary markets = await btrexRESTClient.GetMarketSummary();
            Dictionary<string, decimal> topMarketsBTC = new Dictionary<string, decimal>();
            List<string> topMarketsETH = new List<string>();
            foreach (SummaryResult market in markets.result)
            {
                string mkbase = market.MarketName.Split('-')[0];
                if (mkbase =="BTC")
                {
                    topMarketsBTC.Add(market.MarketName, market.BaseVolume);
                }
                else if (mkbase == "ETH")
                {
                    topMarketsETH.Add(market.MarketName.Split('-')[1]);
                }
            }
            
            List<string> mks = new List<string>();
            foreach (KeyValuePair<string, decimal> mk in topMarketsBTC.OrderByDescending(x => x.Value).Take(20))
            {
                string coin = mk.Key.Split('-')[1];
                if (topMarketsETH.Contains(coin))
                    mks.Add(coin);
            }

            Console.WriteLine("Markets: {0}", mks.Count);
            return mks;
        }


        private static async Task<bool> MatchBottomAskUntilFilled(string orderID, string qtyORamt)
        {
            GetOrderResponse order = await btrexRESTClient.GetOrder(orderID);
            if (!order.success)
            {
                Console.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }

            while (order.result.IsOpen)
            {
                TickerResponse tick = await btrexRESTClient.GetTicker(order.result.Exchange);
                if (!tick.success)
                {
                    Console.WriteLine("    !!!!ERR TICKER2>> " + tick.message);
                    return false;
                }

                if (tick.result.Ask < order.result.Limit)
                {
                    //CALC NEW QTY AT NEW RATE:
                    decimal newQty = 0;
                    if (qtyORamt == "amt")
                    {
                        decimal amtDesired = (order.result.Limit * order.result.Quantity) * 0.9975M;
                        decimal alreadyMade = ((order.result.Quantity - order.result.QuantityRemaining) * order.result.Limit) * 0.9975M;
                        newQty = (amtDesired - alreadyMade) / tick.result.Ask;
                    }
                    else if (qtyORamt == "qty")
                        newQty = order.result.QuantityRemaining;

                    //CHECK THAT NEXT ORDER WILL BE GREATER THAN MINIMUM ORDER PLACEMENT:
                    if ((newQty * tick.result.Ask) * 0.9975M < 0.0005M)
                    {
                        Console.WriteLine("***TRAILING-ORDER: " + order.result.OrderUuid);
                        return true;
                    }

                    //CANCEL EXISTING ORDER:
                    LimitOrderResponse cancel = await btrexRESTClient.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Console.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Console.Write("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    string coin = order.result.Exchange.Split('-')[1];
                    GetBalanceResponse bal = await btrexRESTClient.GetBalance(coin);
                    if (!bal.success)
                        Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < newQty)
                    {
                        Thread.Sleep(150);
                        bal = await btrexRESTClient.GetBalance(coin);
                        if (!bal.success)
                            Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await btrexRESTClient.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Console.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await btrexRESTClient.PlaceLimitOrder(order.result.Exchange, "sell", newQty, tick.result.Ask);
                    if (!newOrd.success)
                    {
                        Console.WriteLine("    !!!!ERR SELL-REPLACE-MOVE>> " + newOrd.message);
                        Console.WriteLine(" QTY: {1} ... RATE: {2}", newQty, tick.result.Ask);
                        return false;
                    }
                    else
                    {
                        Console.Write("\r                                                         \r###SELL ORDER MOVED=[{0}]    QTY: {1} ... RATE: {2}", order.result.Exchange, newQty, tick.result.Ask);
                        orderID = newOrd.result.uuid;
                    }
                }

                Thread.Sleep(1000);
                order = await btrexRESTClient.GetOrder(orderID);
                if (!order.success)
                {
                    Console.WriteLine("    !!!!ERR GET-ORDER2: " + order.message);
                    return false;
                }
            }
            return true;
        }

        private static async Task<bool> MatchTopBidUntilFilled(string orderID, string qtyORamt)
        {
            GetOrderResponse order = await btrexRESTClient.GetOrder(orderID);
            if (!order.success)
            {
                Console.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }
            while (order.result.IsOpen)
            {
                TickerResponse tick = await btrexRESTClient.GetTicker(order.result.Exchange);
                if (!tick.success)
                {
                    Console.WriteLine("    !!!!ERR TICKER2>> " + tick.message);
                    return false;
                }

                if (tick.result.Bid > order.result.Limit)
                {
                    //CALC NEW QTY AT NEW RATE:
                    decimal newQty = 0;
                    if (qtyORamt == "amt")
                        newQty = ((order.result.Limit * 1.0025M) * order.result.QuantityRemaining) / tick.result.Bid;
                    else if (qtyORamt == "qty")
                        newQty = order.result.QuantityRemaining;

                    //CHECK THAT NEXT ORDER WILL BE GREATER THAN MINIMUM ORDER PLACEMENT:
                    if (newQty * (tick.result.Bid * 1.0025M) < 0.0005M)
                    {
                        Console.WriteLine("***TRAILING-ORDER: " + order.result.OrderUuid);
                        return true;
                    }

                    //CANCEL EXISTING ORDER:
                    LimitOrderResponse cancel = await btrexRESTClient.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Console.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Console.WriteLine("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    GetBalanceResponse bal = await btrexRESTClient.GetBalance("btc");
                    if (!bal.success)
                        Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < (newQty * (tick.result.Bid * 1.0025M)))
                    {
                        Console.WriteLine("###BAL");
                        Thread.Sleep(150);
                        bal = await btrexRESTClient.GetBalance("btc");
                        if (!bal.success)
                            Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await btrexRESTClient.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Console.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await btrexRESTClient.PlaceLimitOrder(order.result.Exchange, "buy", newQty, tick.result.Bid);
                    if (!newOrd.success)
                    {
                        Console.WriteLine("    !!!!ERR REPLACE-MOVE>> " + newOrd.message);
                        return false;
                    }
                    else
                    {
                        Console.Write("\r                                                            \r###BUY ORDER MOVED=[{0}]    QTY: {1} ... RATE: {2}", order.result.Exchange, newQty, tick.result.Bid);
                        orderID = newOrd.result.uuid;
                    }
                }

                Thread.Sleep(1000);
                order = await btrexRESTClient.GetOrder(orderID);
                if (!order.success)
                {
                    Console.WriteLine("    !!!!ERR GET-ORDER2: " + order.message);
                    return false;
                }
            }
            return true;
        }
    }
}
