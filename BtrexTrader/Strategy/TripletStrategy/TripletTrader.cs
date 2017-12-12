using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BtrexTrader.Interface;
using BtrexTrader.Data;
using BtrexTrader.Data.Market;

namespace BtrexTrader.Strategy.TripletStrategy
{


    public class TripletTrader
    {
        public bool watchOnly = true;
        private static Stopwatch time = new Stopwatch();
        private bool SingleTradeReady = true;
        private int rots = 0;

        public static List<TripletData> DeltaTrips = new List<TripletData>();

        public async Task Initialize()
        {
            //SUBSCRIBE TO MARKET DELTAS TO TRACK ORDERBOOKS & TRADE EVENTS
            await BtrexWS.SubscribeMarket("BTC-ETH");

            //DO TRIPLETS STUFF
            if (watchOnly)
            {
                //FOR "watchOnly", TAKE TOP 20 MARKETS, 
                //SUBTRACT MARKETS THAT DONT HAVE ETH DELTAS,
                //and CalcTrips() will report only.
                Console.WriteLine("*WATCH ONLY*");
                List<string> topMarkets = await BtrexREST.GetTopMarketsByBVwithETHdelta(20);
                foreach (string mk in topMarkets)
                {
                    await SubTriplet(mk);
                }
            }
            else
            {
                //OTHERWISE, SELECT SPECIFIC MARKETS TO LIVE TRADE ON:
                await SubTriplet("NEO");
                await SubTriplet("QTUM");
                await SubTriplet("OMG");
                await SubTriplet("ADX");
                await SubTriplet("DGB");
            }
        }

        public async void CalcTrips(TripletData triplet)
        {
            GetOrderResponse ord3 = null;
            const decimal wager = 0.012M;
            const decimal BTCreturnThreshhold = 0.00000330M;
            TriCalcReturn LeftResult = triplet.CalcLeft(wager);




            if (!watchOnly && LeftResult.BTCresult > BTCreturnThreshhold && SingleTradeReady && !triplet.tradingState)
            {
                SingleTradeReady = false;
                triplet.tradingState = true;

                LimitOrderResponse orderResp2 = await BtrexREST.PlaceLimitOrder2(triplet.ETHdelta.MarketDelta, "sell", LeftResult.Trades2.Sum(x => x.Value), (LeftResult.Trades2.OrderByDescending(x => x.Key).Last().Key));
                LimitOrderResponse orderResp1 = await BtrexREST.PlaceLimitOrder(triplet.BTCdelta.MarketDelta, "buy", LeftResult.Trades1.Sum(x => x.Value), (LeftResult.Trades1.OrderBy(x => x.Key).Last().Key));
                LimitOrderResponse orderResp3 = await BtrexREST.PlaceLimitOrder3(triplet.B2Edelta.MarketDelta, "sell", LeftResult.Trades3.Sum(x => x.Value), (LeftResult.Trades3.OrderByDescending(x => x.Key).Last().Key));

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


                GetOrderResponse ord1 = await BtrexREST.GetOrder(triplet.IDtrade1);
                while (!ord1.success)
                {
                    ord1 = await BtrexREST.GetOrder(triplet.IDtrade1);
                }
                //Console.WriteLine(ord1.result.OrderUuid + "..." + ord1.result.IsOpen);

                GetOrderResponse ord2 = await BtrexREST.GetOrder2(triplet.IDtrade2);
                while (!ord2.success)
                {
                    ord2 = await BtrexREST.GetOrder(triplet.IDtrade2);
                }
                //Console.WriteLine(ord2.result.OrderUuid + "..." + ord2.result.IsOpen);

                ord3 = await BtrexREST.GetOrder3(triplet.IDtrade3);
                while (!ord3.success)
                {
                    ord3 = await BtrexREST.GetOrder(triplet.IDtrade3);
                }
                //Console.WriteLine(ord3.result.OrderUuid + "..." + ord3.result.IsOpen);

                if (ord1.result.IsOpen || ord2.result.IsOpen || ord3.result.IsOpen)
                {
                    Thread.Sleep(1000);

                    OpenOrdersResponse orders = await BtrexREST.GetOpenOrders();
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
                                        bool completed = await BtrexREST.TradeController.MatchBottomAskUntilFilled(ord.OrderUuid, "qty");
                                        if (completed)
                                            Console.WriteLine("\r###FORCE COMPLETED SELL: [{0}]                                           ", ord.Exchange);
                                        else
                                            Console.WriteLine("\r\n    !!!!ERR FORCE-COMPLETE-SELL FAILL>>");
                                    }
                                    else if (ord.OrderType == "LIMIT_BUY")
                                    {
                                        bool completed = await BtrexREST.TradeController.MatchTopBidUntilFilled(ord.OrderUuid, "qty");
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
                            orders = await BtrexREST.GetOpenOrders();
                            if (!orders.success)
                                Console.WriteLine("\r\n    !!!!ERR OPEN-ORDERS>> " + orders.message);
                        }
                        time.Reset();
                    }
                }
                rots++;
                Console.WriteLine("\r*#{0} Rotations COMPLETE* - {1}", rots, DateTime.Now.ToShortTimeString());

                //REPORTING...
                //Console.WriteLine(triplet.IDtrade1);
                //Console.WriteLine(triplet.IDtrade2);
                //Console.WriteLine(triplet.IDtrade3);

                Console.Beep();



                //bool balsChecked = false;
                //while (!balsChecked)
                //{
                //    balsChecked = true;
                //    GetBalancesResponse bals = await BtrexREST.GetBalances();                    
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

                //GetOrderResponse ord1 = await BtrexREST.GetOrder(triplet.IDtrade1);
                //if (!ord1.success)
                //    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord1.message);
                //GetOrderResponse ord2 = await BtrexREST.GetOrder(triplet.IDtrade2);
                //if (!ord2.success)
                //    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord2.message);

                ord3 = await BtrexREST.GetOrder(triplet.IDtrade3);
                if (!ord3.success)
                    Console.WriteLine("    !!!!ERR GET-ORDER>> " + ord3.message);

                //Console.WriteLine("Trade1:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord1.result.Quantity, ord1.result.PricePerUnit, triplet.IDtrade1);
                //Console.WriteLine("Trade2:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord2.result.Quantity, ord2.result.PricePerUnit, triplet.IDtrade2);
                //Console.WriteLine("Trade3:\r\n\tQTY: {0}\r\n\tPPU: {1}\r\n\tID: {2}", ord3.result.Quantity, ord3.result.PricePerUnit, triplet.IDtrade3);

                decimal profit = (ord3.result.Quantity * Convert.ToDecimal(ord3.result.PricePerUnit) - ord3.result.CommissionPaid) - 0.012M;
                Console.WriteLine("PROFIT: {0:0.0#######}BTC(${1:0.00##})", profit, profit * BtrexData.USDrate);


                triplet.Ready();
                SingleTradeReady = true;
                //watchOnly = false;
            }


            //    //REPORTING ONLY
            if (watchOnly)
            {
                //TriCalcReturn RightResult = triplet.CalcRight(wager);
                decimal LeftReturnUSD = LeftResult.BTCresult * BtrexData.USDrate;
                //decimal RightReturnUSD = RightResult.BTCresult * BtrexREST.USDrate;
                if (LeftReturnUSD > 0.00M)
                {
                    //if (triplet.tradingState == true)
                    //    return;
                    string ALT = triplet.BTCdelta.MarketDelta.Substring(4);
                    Console.WriteLine("{0} :: [{1}]  ===  ${2:0.00###} -L-", DateTime.Now, ALT, LeftReturnUSD);
                }
                //if (RightReturnUSD > 0.00M)
                //{
                //    if (triplet.tradingState == true)
                //        return;
                //    string ALT = triplet.BTCdelta.MarketDelta.Substring(4);
                //    Console.WriteLine("{0} :: [{1}]  ===  ${2:0.00###} -R-", DateTime.Now, ALT, RightReturnUSD);
                //}
            }
        }


        private async static Task SubTriplet(string COIN)
        {
            string BTCdelta = "BTC-" + COIN;
            string ETHdelta = "ETH-" + COIN;
            await Task.WhenAll(BtrexWS.SubscribeMarket(BTCdelta), BtrexWS.SubscribeMarket(ETHdelta));
            AddTripletDeltas(BTCdelta, ETHdelta);
        }


        public static void AddTripletDeltas(string BTCdelta, string ETHdelta)
        {
            OrderBook BTCbk = null;
            OrderBook ETHbk = null;
            OrderBook B2Ebk = (OrderBook)BtrexData.Markets["BTC-ETH"].OrderBook.Clone();

            foreach (Market market in BtrexData.Markets.Values)
            {
                if (BTCbk == null && market.OrderBook.MarketDelta == BTCdelta)
                {
                    BTCbk = (OrderBook)market.OrderBook.Clone();
                }
                else if (ETHbk == null && market.OrderBook.MarketDelta == ETHdelta)
                {
                    ETHbk = (OrderBook)market.OrderBook.Clone();
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
                Console.WriteLine("    !!!!ERR>> DOUBLET-CLONES NOT SET!");
            }
        }

    }


}

