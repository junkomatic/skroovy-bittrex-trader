using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using BtrexTrader.Strategy.Core;
using BtrexTrader.Data;


namespace BtrexTrader.Interface
{
    public class TradeControl
    {
        public const decimal minimumTradeSatoshis = 0.00109M;
        private readonly TimeSpan LoopFrequency = TimeSpan.FromSeconds(1);

        private static ConcurrentDictionary<string, OpenOrder> OpenOrders = new ConcurrentDictionary<string, OpenOrder>();
        private static Thread TradeControllerThread;


        public void StartTrading()
        {
            TradeControllerThread = new Thread(async () => await ProcessOrders());
            TradeControllerThread.IsBackground = true;
            TradeControllerThread.Name = "TradeControl-Thread";
            TradeControllerThread.Start();
        }

        private async Task ProcessOrders()
        {
                while (true)
                {
                    if (OpenOrders.Count == 0)
                    {
                        Thread.Sleep(LoopFrequency);
                        continue;
                    }

                    foreach (var order in OpenOrders)
                    {
                        //TODO: MAKE DECISIONS BASED ON THE STATE OF EACH OpenOrder obj:


                    }



                }
            

            Trace.WriteLine("\r\n\r\n    @@@ TradeControl-Thread STOPPED @@@\r\n\r\n");
        }


        public void RegisterNewOrder(NewOrder newOrd, string uniqueIdentifier)
        {
            //TODO: CONVERT NewOrder obj TO OpenOrder obj
            var OpenOrd = new OpenOrder();

            bool added;
            do
            {
                added = OpenOrders.TryAdd(uniqueIdentifier, OpenOrd);
            } while (!added);

        }

        public void RemoveNewOrder(string uniqueIdentifier)
        {
            bool removed;
            do
            {
                removed = OpenOrders.TryRemove(uniqueIdentifier, out var s);
            } while (!removed);
        }


       

  






        //*****************************************************************




        public async Task ExecuteStopLoss(StopLoss stopLoss)
        {
            if (!stopLoss.virtualSL)
            {
                var lowestRate = minimumTradeSatoshis / stopLoss.Quantity;
                LimitOrderResponse orderResp = await BtrexREST.PlaceLimitOrder(stopLoss.MarketDelta, "sell", stopLoss.Quantity, lowestRate);
                if (!orderResp.success)
                {
                    Trace.WriteLine(string.Format("    !!!!ERR ExecuteStopLoss-PLACE-ORDER1>> " + orderResp.message));
                    Trace.WriteLine(string.Format("        QTY: {1} ... STOP-LOSS RATE: {2}", stopLoss.Quantity, stopLoss.StopRate));

                    //REDUNTANT (FAILSAFE) CALL ON INITIAL CALL FAILURE 
                    orderResp = await BtrexREST.PlaceLimitOrder(stopLoss.MarketDelta, "sell", stopLoss.Quantity, lowestRate);
                    if (!orderResp.success)
                    {
                        Trace.WriteLine("    !!!!ERR ExecuteStopLoss-PLACE-ORDER1.2ndTry>> " + orderResp.message);
                        Trace.WriteLine(string.Format("        QTY: {1} ... STOP-LOSS RATE: {2}", stopLoss.Quantity, stopLoss.StopRate));
                        return;
                    }
                }

                Thread.Sleep(1500);

                var order = await BtrexREST.GetOrder(orderResp.result.uuid);
                if (!order.success)
                {
                    Trace.WriteLine("    !!!!ERR ExecuteStopLoss-GET-ORDER: " + order.message);
                }
                
                stopLoss.ExecutionCallback(order.result, stopLoss.CandlePeriod);
            }
            else
            {
                var vSell = new GetOrderResult();
                vSell.Exchange = stopLoss.MarketDelta;
                vSell.Quantity = stopLoss.Quantity;
                vSell.PricePerUnit = BtrexData.Markets[stopLoss.MarketDelta].TradeHistory.RecentFills.Last().Rate * 0.9975M;

                stopLoss.ExecutionCallback(vSell, stopLoss.CandlePeriod);
            }
            
        }

        
        public async Task ExecuteNewOrderList(List<NewOrder> NewOrderList, bool virtualTrading = false)
        {
            if (!virtualTrading)
            {
                var placeOrders = NewOrderList.Select(ExecuteNewOrder).ToArray();
                await Task.WhenAll(placeOrders);
            }
            else if (virtualTrading)
            {
                var placeVirtualOrders = NewOrderList.Select(VirtualExecuteNewOrder).ToArray();
                await Task.WhenAll(placeVirtualOrders);
            }
        }


        public async Task ExecuteNewOrder(NewOrder ord)
        {
            var orderReturnData = new NewOrder(ord.MarketDelta, ord.BUYorSELL, 0, 0, null, null, ord.CandlePeriod);            
            bool orderComplete = false;
            decimal percentRemaining = 1;
            decimal newQty = ord.Qty;
            var orderRate = ord.Rate;
            var BTCbuyWager = ord.Qty * ord.Rate * 1.0025M;

                        Trace.WriteLine(string.Format("%%%%PLACING ORDER: {0} {1} ... QTY: {2} ... RATE: {3} ", ord.BUYorSELL, ord.MarketDelta, ord.Qty, ord.Rate));

            //PLACE INITIAL ORDER:
            var orderResp = await BtrexREST.PlaceLimitOrder(ord.MarketDelta, ord.BUYorSELL, ord.Qty, ord.Rate);
            if (!orderResp.success)
            {
                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-PLACE-ORDER1>> " + orderResp.message);
                Trace.WriteLine(string.Format("{0} {3} QTY: {1} ...  RATE: {2}", ord.MarketDelta, ord.Qty, ord.Rate, ord.BUYorSELL));
                return;
            }
            
            string OrderID = orderResp.result.uuid;

            var stopwatch = new Stopwatch();
            stopwatch.Start();                       

            //ORDER EXECUTION LOOP:
            do
            {
                Thread.Sleep(1000);
                var getOrder1 = await BtrexREST.GetOrder(OrderID);
                if (!getOrder1.success)
                {
                    Trace.WriteLine("    !!!!ERR ExecuteNewOrder-GET-ORDER1: " + getOrder1.message);
                }                              

                orderComplete = !getOrder1.result.IsOpen;

                if (orderComplete)
                {
                    //ADD DATA TO RETURN OBJECT
                    //avgRate = TOTAL BTC amt SPENT divided by TOTAL QTY RECIEVED)
                    orderReturnData.Rate = ((orderReturnData.Qty * orderReturnData.Rate) + (getOrder1.result.Quantity * getOrder1.result.Price)) / (orderReturnData.Qty + getOrder1.result.Quantity);
                    orderReturnData.Qty += getOrder1.result.Quantity;
                    Trace.WriteLine(ord.MarketDelta + "ORDER COMPLETE1&&&&&&&");
                }
                else if (!orderComplete)
                {

                    Trace.WriteLine(ord.MarketDelta + "else&&&&&&");
                    //IF THE REMAINING AMT IS UNDER DUST ORDER, JUST WAIT FOR COMPLETION
                    if (getOrder1.result.QuantityRemaining * getOrder1.result.Price < minimumTradeSatoshis)
                        continue;
                    //ELSE, IF THE ORDER REMAINS AFTER (30) SECONDS, CANCEL & REPLACE AT NEW RATE:
                    else if (stopwatch.Elapsed > TimeSpan.FromSeconds(12))
                    {                    
                        //RECALC RATE and (BUY)QTY AND REPOST ORDER AT DATA PRICE
                        if (ord.BUYorSELL == "BUY" && BtrexData.Markets[ord.MarketDelta].OrderBook.Bids.ToList().OrderByDescending(k => k.Key).First().Key > orderRate)
                        {
                            //CANCEL TRADE ORDER IF NOT IMMEDIATE EXE
                            LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(OrderID);
                            if (!cancel.success)
                            {
                                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-CANCEL-MOVE>> " + cancel.message);
                                return;
                            }

                            //GET ORDER ONCE AGAIN TO CHECK PARTIALLY COMPLETED
                            var getOrder2 = await BtrexREST.GetOrder(OrderID);
                            if (!getOrder2.success)
                            {
                                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-GET-ORDER2: " + getOrder2.message);
                            }

                            //Add completed amount to percentComplete:
                            var completedQty = getOrder2.result.Quantity - getOrder2.result.QuantityRemaining;
                            var currentCompletion = completedQty / getOrder2.result.Quantity;
                            percentRemaining -= percentRemaining * currentCompletion;
                            var wagerRemains = percentRemaining * BTCbuyWager;

                            if (currentCompletion > 0M)
                            {
                                //ADD DATA TO RETURN OBJECT
                                orderReturnData.Rate = ((orderReturnData.Qty * orderReturnData.Rate) + (completedQty * getOrder2.result.Price)) / (orderReturnData.Qty + completedQty);
                                orderReturnData.Qty += completedQty;
                            }

                            //FINAL CHECK FOR ORDER COMPLETE:
                            if (getOrder2.result.QuantityRemaining == 0M || wagerRemains > minimumTradeSatoshis)
                            {
                                orderComplete = true;
                                break;
                            }
                        
                            //REPLACE CANCELED ORDER AT NEW RATE WITH NEW QTY:
                            orderRate = BtrexData.Markets[ord.MarketDelta].OrderBook.Bids.ToList().OrderByDescending(k => k.Key).First().Key;
                            newQty = wagerRemains / (orderRate * 1.0025M);

                            var orderResp2 = await BtrexREST.PlaceLimitOrder(ord.MarketDelta, "BUY", newQty, orderRate);
                            if (!orderResp2.success)
                            {
                                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-PLACE(BUY)-ORDER2>> " + orderResp2.message);
                                Trace.WriteLine(string.Format(" QTY: {1} ...  RATE: {2}", newQty, orderRate));
                            }

                            OrderID = orderResp2.result.uuid;
                        

                        }
                        //REPLACE "SELL" ORDER TO SELL REAMINING QTY AT NEW RATE:
                        else if (ord.BUYorSELL == "SELL" && BtrexData.Markets[ord.MarketDelta].OrderBook.Asks.ToList().OrderBy(k => k.Key).First().Key < orderRate)
                        {
                            //CANCEL TRADE ORDER IF NOT IMMEDIATE EXE
                            LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(OrderID);
                            if (!cancel.success)
                            {
                                Trace.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                                return;
                            }

                            //GET ORDER ONCE AGAIN TO CHECK PARTIALLY COMPLETED
                            var getOrder2 = await BtrexREST.GetOrder(OrderID);
                            if (!getOrder2.success)
                            {
                                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-GET-ORDER2: " + getOrder2.message);
                            }

                            var completedQty = getOrder2.result.Quantity - getOrder2.result.QuantityRemaining;

                            if (completedQty > 0)
                            {
                                //ADD DATA TO RETURN OBJECT
                                orderReturnData.Rate = ((orderReturnData.Qty * orderReturnData.Rate) + (completedQty * getOrder2.result.Price)) / (orderReturnData.Qty + completedQty);
                                orderReturnData.Qty += completedQty;
                            }

                            //CHECK NEW ORDER RATE:
                            orderRate = BtrexData.Markets[ord.MarketDelta].OrderBook.Asks.ToList().OrderBy(k => k.Key).First().Key;

                            //FINAL CHECK FOR ORDER COMPLETE, OR REMAINING AMOUNT == LESS THAN DUST:
                            if (getOrder2.result.QuantityRemaining == 0 || getOrder2.result.QuantityRemaining * orderRate < minimumTradeSatoshis)  
                            {
                                orderComplete = true;
                                break;
                            }

                            //REPLACE ORDER TO SELL REMAINING QTY AT NEW RATE:
                            var orderResp2 = await BtrexREST.PlaceLimitOrder(ord.MarketDelta, "SELL", getOrder2.result.QuantityRemaining, orderRate);
                            if (!orderResp2.success)
                            {
                                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-PLACE(SELL)-ORDER2>> " + orderResp2.message);
                                Trace.WriteLine(string.Format(" QTY: {1} ...  RATE: {2}", newQty, orderRate));
                            }

                            OrderID = orderResp2.result.uuid;

                        }

                    }
                }                                

            } while (!orderComplete);

            stopwatch.Stop();

            //ADJUST FINAL AVG RATE DATA FOR EXCHANGE FEE: 
            if (orderReturnData.BUYorSELL == "BUY")
                orderReturnData.Rate = BtrexData.Markets[ord.MarketDelta].TradeHistory.RecentFills.Last().Rate; //orderReturnData.Rate = orderReturnData.Rate * 1.0025M;
            else if (orderReturnData.BUYorSELL == "SELL")
                orderReturnData.Rate = orderReturnData.Rate * 0.9975M;

            //CALL NewOrder obj CALLBACK FUNCTION:
            ord.ExecutionCompleteCallback(orderReturnData);
        }


        public async Task VirtualExecuteNewOrder(NewOrder ord)
        {
            ord.ExecutionCompleteCallback(ord);
        }













        //***LEGACY METHODS (TRIPLET TRADER):


        public async Task<bool> MatchBottomAskUntilFilled(string orderID, string qtyORamt)
        {
            GetOrderResponse order = await BtrexREST.GetOrder(orderID);
            if (!order.success)
            {
                Trace.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }

            while (order.result.IsOpen)
            {
                TickerResponse tick = await BtrexREST.GetTicker(order.result.Exchange);
                if (!tick.success)
                {
                    Trace.WriteLine("    !!!!ERR TICKER2>> " + tick.message);
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
                        Trace.WriteLine("***TRAILING-ORDER: " + order.result.OrderUuid);
                        return true;
                    }

                    //CANCEL EXISTING ORDER:
                    LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Trace.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Trace.Write("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    string coin = order.result.Exchange.Split('-')[1];
                    GetBalanceResponse bal = await BtrexREST.GetBalance(coin);
                    if (!bal.success)
                        Trace.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < newQty)
                    {
                        Thread.Sleep(150);
                        bal = await BtrexREST.GetBalance(coin);
                        if (!bal.success)
                            Trace.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await BtrexREST.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Trace.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await BtrexREST.PlaceLimitOrder(order.result.Exchange, "sell", newQty, tick.result.Ask);
                    if (!newOrd.success)
                    {
                        Trace.WriteLine("    !!!!ERR SELL-REPLACE-MOVE>> " + newOrd.message);
                        Trace.WriteLine(string.Format(" QTY: {1} ... RATE: {2}", newQty, tick.result.Ask));
                        return false;
                    }
                    else
                    {
                        Trace.Write(string.Format("\r                                                         \r###SELL ORDER MOVED=[{0}]    QTY: {1} ... RATE: {2}", order.result.Exchange, newQty, tick.result.Ask));
                        orderID = newOrd.result.uuid;
                    }
                }

                Thread.Sleep(1000);
                order = await BtrexREST.GetOrder(orderID);
                if (!order.success)
                {
                    Trace.WriteLine("    !!!!ERR GET-ORDER2: " + order.message);
                    return false;
                }
            }
            return true;
        }

        public async Task<bool> MatchTopBidUntilFilled(string orderID, string qtyORamt)
        {
            GetOrderResponse order = await BtrexREST.GetOrder(orderID);
            if (!order.success)
            {
                Trace.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }


            while (order.result.IsOpen)
            {
                TickerResponse tick = await BtrexREST.GetTicker(order.result.Exchange);
                if (!tick.success)
                {
                    Trace.WriteLine("    !!!!ERR TICKER2>> " + tick.message);
                    return false;
                }

                if (tick.result.Bid > order.result.Limit)
                {
                    //CALC NEW QTY AT NEW RATE:
                    decimal newQty = 0;
                    if (qtyORamt == "amt")
                        newQty = ((order.result.Limit * 1.0025M) * order.result.QuantityRemaining) / (tick.result.Bid * 0.9975M);
                    else if (qtyORamt == "qty")
                        newQty = order.result.QuantityRemaining;

                    //CHECK THAT NEXT ORDER WILL BE GREATER THAN MINIMUM ORDER PLACEMENT:
                    if (newQty * (tick.result.Bid * 1.0025M) < 0.0005M)
                    {
                        Trace.WriteLine("***TRAILING-ORDER: " + order.result.OrderUuid);
                        return true;
                    }

                    //CANCEL EXISTING ORDER:
                    LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Trace.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Trace.WriteLine("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    GetBalanceResponse bal = await BtrexREST.GetBalance("btc");
                    if (!bal.success)
                        Trace.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < (newQty * (tick.result.Bid * 1.0025M)))
                    {
                        Trace.WriteLine("###BAL");
                        Thread.Sleep(150);
                        bal = await BtrexREST.GetBalance("btc");
                        if (!bal.success)
                            Trace.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await BtrexREST.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Trace.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await BtrexREST.PlaceLimitOrder(order.result.Exchange, "buy", newQty, tick.result.Bid);
                    if (!newOrd.success)
                    {
                        Trace.WriteLine("    !!!!ERR REPLACE-MOVE>> " + newOrd.message);
                        return false;
                    }
                    else
                    {
                        Trace.Write(string.Format("\r                                                            \r###BUY ORDER MOVED=[{0}]    QTY: {1} ... RATE: {2}", order.result.Exchange, newQty, tick.result.Bid));
                        orderID = newOrd.result.uuid;
                    }
                }

                Thread.Sleep(1000);

                order = await BtrexREST.GetOrder(orderID);
                if (!order.success)
                {
                    Trace.WriteLine("    !!!!ERR GET-ORDER2: " + order.message);
                    return false;
                }
            }
            return true;
        }



    }



    public class NewOrder
    {
        public string MarketDelta { get; set; }
        public string BUYorSELL { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public string CandlePeriod { get; set; }
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<NewOrder> ExecutionCompleteCallback { get; set; }

        public NewOrder(string mDelta, string BUYSELL, decimal quantity, decimal price, Action<OpenOrder> DUpdateCback = null, Action<NewOrder> ExeCback = null, string cPeriod = null)
        {
            MarketDelta = mDelta;
            BUYorSELL = BUYSELL.ToUpper();
            Qty = quantity;            
            Rate = price;

            if (DUpdateCback != null)
                DataUpdateCallback = DUpdateCback;

            if (ExeCback != null)
                ExecutionCompleteCallback = ExeCback;

            if (cPeriod != null)
                CandlePeriod = cPeriod;
        }

    }

    
    public class OpenOrder
    {
        public string OrderUuid { get; set; }
        public string Exchange { get; set; }
        public string Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityRemaining { get; set; }
        public decimal Limit { get; set; }
        public decimal Reserved { get; set; }
        public decimal CommissionReserved { get; set; }
        public decimal CommissionReserveRemaining { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public decimal PricePerUnit { get; set; }
        public DateTime Opened { get; set; }     
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<GetOrderResult> ExecutionCompleteCallback { get; set; }


        public OpenOrder(GetOrderResult ord, Action<OpenOrder> cBack_Data = null, Action<GetOrderResult> cBack_Exe = null)
        {
            OrderUuid = ord.OrderUuid;
            Exchange = ord.Exchange;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Limit = ord.Limit;
            Reserved = ord.Reserved;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;
            Price = ord.Price;
            PricePerUnit = ord.PricePerUnit;
            Opened = ord.Opened;

            if (cBack_Data != null)
                DataUpdateCallback = cBack_Data;

            if (cBack_Exe != null)
                ExecutionCompleteCallback = cBack_Exe;

        }

        public void UpdateOpenOrder(GetOrderResult ord)
        {
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Limit = ord.Limit;
            Reserved = ord.Reserved;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;
            Price = ord.Price;
            PricePerUnit = ord.PricePerUnit;
            Opened = ord.Opened;

            //CALL DataUpdateCallback:
            DataUpdateCallback(this);
        }

    }


}
