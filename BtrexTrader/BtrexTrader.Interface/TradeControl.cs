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
        private readonly TimeSpan NewOrderWaitTime = TimeSpan.FromSeconds(45);
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

                foreach (var order in OpenOrders.ToList())
                {
                    //Call Order Data:
                    var getOrder2 = await BtrexREST.GetOrder(order.Value.OrderUuid);
                    if (!getOrder2.success)
                    {
                        Trace.WriteLine("    !!!!ERR ExecuteNewOrder-GET-ORDER2: " + getOrder2.message);
                    }

                    
                    //CHECK IF COMPLETED OR UPDATED, CALL CALLBACKS:
                    if (!getOrder2.result.IsOpen)
                    {
                        //Remove from Dictionary and call ExeCallback:
                        RemoveNewOrder(order.Key);
                        order.Value.CompleteOrder(getOrder2.result);

                        continue;
                    }
                    else if (getOrder2.result.QuantityRemaining != order.Value.QuantityRemaining)
                    {
                        //Update OpenOrder in Dict and Call DataCallback
                        OpenOrders[order.Key].UpdateOrder(getOrder2.result);
                    }


                    //BUY AND SELL LOGIC FOR STILL-OPEN ORDERS: 
                    //If the initial order time has elapsed, and the remaining amount is above min order satoshis:
                    

                    if (DateTime.UtcNow - order.Value.Opened > NewOrderWaitTime)
                    {
                        if (order.Value.Type == "LIMIT_BUY" && BtrexData.Markets[order.Value.Exchange].OrderBook.Bids.OrderByDescending(a => a.Key).First().Key > order.Value.Limit)
                        {
                            //Cancel and Replace at top bid:
                            //QTY MUST BE ADJUSTED SO THAT 'TotalReserved' BTC AMT REAMINS EQUAL






                        }
                        else if (order.Value.Type == "LIMIT_SELL" && BtrexData.Markets[order.Value.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key < order.Value.Limit)
                        {
                            //IF ORDER IS NOT AT TOP OF ASKS, CANCEL AND REPLACE TO MAKE IT SO:
                            //Use QuantityRemains
                            var cancelRequest = await BtrexREST.CancelLimitOrder(order.Value.OrderUuid);
                            if (!cancelRequest.success)
                            {
                                Trace.WriteLine("    !!!!ERR CANCEL-ORDER>>> " + order.Key + ": " + cancelRequest.message);
                                continue;
                            }

                            //Get data from cancelled order:
                            var canceledOrderData = new GetOrderResponse();
                            do
                            {
                                canceledOrderData = await BtrexREST.GetOrder(order.Value.OrderUuid);
                                if (!canceledOrderData.success)
                                {
                                    Trace.WriteLine("    !!!!ERR GET-CANCELED-ORDDATA>>> " + order.Key + ": " + canceledOrderData.message);
                                }
                            }
                            while (canceledOrderData.result == null || canceledOrderData.result.IsOpen == false);


                            //MAKE SURE REMAINING AMOUNT IS STILL ABOVE MIN SATOSHIS
                            var highestBid = BtrexData.Markets[canceledOrderData.result.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key;
                            var satoshis = highestBid * canceledOrderData.result.QuantityRemaining;
                            if (satoshis < minimumTradeSatoshis)
                            {
                                //ORDER IS COMPLETE, CALL EXE CALLBACK
                                RemoveNewOrder(order.Key);
                                order.Value.CompleteOrder(canceledOrderData.result);
                                continue;
                            }
                            
                            //REPLACE ORDER
                            var order2 = await BtrexREST.PlaceLimitOrder2(canceledOrderData.result.Exchange, "SELL", canceledOrderData.result.QuantityRemaining, BtrexData.Markets[canceledOrderData.result.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key);
                            if (!order2.success)
                            {
                                Trace.WriteLine("    !!!!ERR PLACE-ORDER2>>> " + order.Key + ": " + order2.message);
                                continue;
                            }

                            //GET REPLACED ORDER DATA
                            var order2data = new GetOrderResponse();
                            do
                            {
                                order2data = await BtrexREST.GetOrder(order2.result.uuid);
                                if (!order2data.success)
                                {
                                    Trace.WriteLine("    !!!!ERR GET-ORD2DATA>>> " + order.Key + ": " + order2data.message);
                                }
                            }
                            while (order2data.result == null || order2data.result.IsOpen == false);

                            //CHECK IsOpen, IF false THEN EXE CALLBACK
                            if (!order2data.result.IsOpen)
                            {
                                //ORDER IS COMPLETE, CALL EXE CALLBACK
                                RemoveNewOrder(order.Key);
                                order.Value.CompleteOrder(canceledOrderData.result);
                                continue;
                            }

                            //UPDATE DATA BY COMBINING CANCELED-ORDDATA AND ORDER2DATA
                            OpenOrders[order.Key].ReplaceOrder(canceledOrderData.result, order2data.result);                          
                            
                        }
                    }

                }

                Thread.Sleep(LoopFrequency);

            }
            
            Trace.WriteLine("\r\n\r\n    @@@ TradeControl-Thread STOPPED @@@\r\n\r\n");
        }


        public void RegisterOpenOrder(OpenOrder openOrd, string uniqueIdentifier)
        {
            bool added;
            do
            {
                added = OpenOrders.TryAdd(uniqueIdentifier, openOrd);
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
            Trace.WriteLine(string.Format("%%%%PLACING ORDER1: {0} {1} ... QTY: {2} ... RATE: {3} ", ord.BUYorSELL, ord.MarketDelta, ord.Qty, ord.Rate));

            //PLACE INITIAL ORDER:
            var orderResp = await BtrexREST.PlaceLimitOrder(ord.MarketDelta, ord.BUYorSELL, ord.Qty, ord.Rate);
            if (!orderResp.success)
            {
                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-PLACE-ORDER1>> " + orderResp.message);
                Trace.WriteLine(string.Format("{0} {3} QTY: {1} ...  RATE: {2}", ord.MarketDelta, ord.Qty, ord.Rate, ord.BUYorSELL));
                return;
            }
            
            //Wait for server processing time and then call order data:
            Thread.Sleep(1000);
            var getOrder1 = await BtrexREST.GetOrder(orderResp.result.uuid);
            if (!getOrder1.success)
            {
                Trace.WriteLine("    !!!!ERR ExecuteNewOrder-GET-ORDER1: " + getOrder1.message);
            }

            var totalQty = 0M;
            var totalReserved = 0M;

            if (getOrder1.result.Type.ToUpper() == "LIMIT_SELL")
            {
                totalQty = getOrder1.result.Quantity;
                totalReserved = getOrder1.result.Price;
            }
            else if (getOrder1.result.Type.ToUpper() == "LIMIT_BUY")
            {
                totalReserved = getOrder1.result.Reserved;
                totalQty = getOrder1.result.Quantity - getOrder1.result.QuantityRemaining;
            }
                        
            //Enter Order Data into Dictionary
            var order = new OpenOrder(getOrder1.result, totalQty, totalReserved, ord.DataUpdateCallback, ord.ExecutionCompleteCallback, ord.CandlePeriod);
            RegisterOpenOrder(order, ord.UniqueID);
            
        }


        public async Task VirtualExecuteNewOrder(NewOrder ord)
        {

            //TODO: CREATE ORDER-DATA FROM ord



            
            //ord.ExecutionCompleteCallback(orderData);
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
        public string UniqueID;
        public string MarketDelta { get; set; }
        public string BUYorSELL { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public string CandlePeriod { get; set; }
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<OpenOrder> ExecutionCompleteCallback { get; set; }

        public NewOrder(string ID, string mDelta, string BUYSELL, decimal quantity, decimal price, Action<OpenOrder> DUpdateCback = null, Action<OpenOrder> ExeCback = null, string cPeriod = null)
        {
            UniqueID = ID;
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
        public decimal TotalQuantity { get; set; }
        public decimal TotalReserved { get; set; }
        public decimal CommissionReserved { get; set; }
        public decimal CommissionReserveRemaining { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public decimal PricePerUnit { get; set; }
        public DateTime Opened { get; set; }
        public string CandlePeriod { get; set; }
        public Action<OpenOrder> DataUpdateCallback { get; set; }
        public Action<OpenOrder> ExecutionCompleteCallback { get; set; }


        public OpenOrder(GetOrderResult ord, decimal totalQty, decimal totalRes, Action<OpenOrder> cBack_Data, Action<OpenOrder> cBack_Exe, string periodName = null)
        {
            OrderUuid = ord.OrderUuid;
            Exchange = ord.Exchange;
            Type = ord.Type;
            TotalQuantity = totalQty;
            TotalReserved = totalRes;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Limit = ord.Limit;
            Reserved = ord.Reserved;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;
            Price = ord.Price;
            PricePerUnit = 0M;
            Opened = ord.Opened;            
            DataUpdateCallback = cBack_Data;
            ExecutionCompleteCallback = cBack_Exe;

            if (periodName != null)
                CandlePeriod = periodName;
            
        }


        public OpenOrder(string ID, string delta, string orderType, decimal totalQty, decimal totalRes, decimal qty, decimal qtyRemains, decimal limit_price, decimal BTCreserved, decimal feeReserved, decimal feeRemains, decimal feePaid, decimal BTCprice, decimal PPU, DateTime TimeOpened, Action<OpenOrder> cBack_Data, Action<OpenOrder> cBack_Exe, string periodName = null)
        {
            OrderUuid = ID;
            Exchange = delta;
            Type = orderType;
            TotalQuantity = totalQty;
            TotalReserved = totalRes;
            Quantity = qty;
            QuantityRemaining = qtyRemains;
            Limit = limit_price;
            Reserved = BTCreserved;           
            CommissionReserved = feeReserved;
            CommissionReserveRemaining = feeRemains;
            CommissionPaid = feePaid;
            Price = BTCprice;
            PricePerUnit = PPU;
            Opened = TimeOpened;
            DataUpdateCallback = cBack_Data;
            ExecutionCompleteCallback = cBack_Exe;

            if (periodName != null)
                CandlePeriod = periodName;
                        
        }


        public void UpdateOrder(GetOrderResult ord)
        {
            //UPDATE TOTALS:
            if (ord.Type.ToUpper() == "LIMIT_SELL")
                TotalReserved += ord.Price - Price;
            else if (ord.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += QuantityRemaining - ord.QuantityRemaining;

            Limit = ord.Limit;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Reserved = ord.Reserved;
            Price = ord.Price;             
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;  

            //CALL DataUpdateCallback:
            DataUpdateCallback(this);
        }

        
        public void CompleteOrder(GetOrderResult ord)
        {
            //UPDATE TOTALS:
            if (ord.Type.ToUpper() == "LIMIT_SELL")
                TotalReserved += ord.Price - Price;
            else if (ord.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += QuantityRemaining - ord.QuantityRemaining;

            Limit = ord.Limit;
            Quantity = ord.Quantity;
            QuantityRemaining = ord.QuantityRemaining;
            Reserved = ord.Reserved;
            Price = ord.Price;
            CommissionReserved = ord.CommissionReserved;
            CommissionReserveRemaining = ord.CommissionReserveRemaining;
            CommissionPaid = ord.CommissionPaid;

            //CALL ExecutionCallback:
            ExecutionCompleteCallback(this);
        }
        
        public void ReplaceOrder(GetOrderResult oldOrder, GetOrderResult newOrder)
        {
            OrderUuid = newOrder.OrderUuid;

            if (newOrder.Type.ToUpper() == "LIMIT_SELL")            
                TotalReserved += (oldOrder.Price - Price) + newOrder.Price;            
            else if (newOrder.Type.ToUpper() == "LIMIT_BUY")
                TotalQuantity += (QuantityRemaining - oldOrder.QuantityRemaining) + (newOrder.Quantity - newOrder.QuantityRemaining);

            Limit = newOrder.Limit;
            Quantity = newOrder.Quantity;
            QuantityRemaining = newOrder.QuantityRemaining;
            Reserved = newOrder.Reserved;
            Price = newOrder.Price;
            CommissionReserved = newOrder.CommissionReserved;
            CommissionReserveRemaining = newOrder.CommissionReserveRemaining;
            CommissionPaid = newOrder.CommissionPaid;

            //CALL DataUpdateCallback:
            DataUpdateCallback(this);

        }

    }


}
