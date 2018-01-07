using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
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
            TradeControllerThread = new Thread(() => RunAsync());
            TradeControllerThread.IsBackground = true;
            TradeControllerThread.Name = "TradeControl-Thread";
            TradeControllerThread.Start();
        }

        private void RunAsync()
        {
            ProcessOrders().Wait();
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
                    if (DateTime.UtcNow - order.Value.Opened > NewOrderWaitTime)
                    {
                        if (order.Value.Type == "LIMIT_BUY" && BtrexData.Markets[order.Value.Exchange].OrderBook.Bids.OrderByDescending(a => a.Key).First().Key > order.Value.Limit)
                        {
                            //CANCEL ORDER, GET DATA:                            
                            var canceledOrderData = await CancelOrderGetData(order.Key, order.Value.OrderUuid);
                            if (canceledOrderData == null)
                                continue;

                            //QTY MUST BE ADJUSTED SO THAT 'TotalReserved' BTC AMT REAMINS EQUAL
                            var satoshisRemaining = order.Value.Reserved - order.Value.Price;
                            var newRate = BtrexData.Markets[canceledOrderData.Exchange].OrderBook.Bids.OrderByDescending(a => a.Key).First().Key;
                            var newQty = satoshisRemaining / (newRate * 1.0025M);

                            //MAKE SURE REMAINING AMOUNT IS STILL ABOVE MIN SATOSHIS
                            if (newQty * newRate < minimumTradeSatoshis)
                            {
                                //ORDER IS COMPLETE, CALL EXE CALLBACK
                                RemoveNewOrder(order.Key);
                                order.Value.CompleteOrder(canceledOrderData);
                                continue;
                            }

                            //REPLACE ORDER
                            var order2 = await BtrexREST.PlaceLimitOrder2(canceledOrderData.Exchange, "BUY", newQty, newRate);
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
                                order.Value.CompleteOrder(canceledOrderData);
                                continue;
                            }

                            //UPDATE DATA BY COMBINING CANCELED-ORDDATA AND ORDER2DATA
                            OpenOrders[order.Key].ReplaceOrder(canceledOrderData, order2data.result);


                        }
                        else if (order.Value.Type == "LIMIT_SELL" && BtrexData.Markets[order.Value.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key < order.Value.Limit)
                        {
                            //CANCEL ORDER, GET DATA:                            
                            var canceledOrderData = await CancelOrderGetData(order.Key, order.Value.OrderUuid);
                            if (canceledOrderData == null)
                                continue;

                            //MAKE SURE REMAINING AMOUNT IS STILL ABOVE MIN SATOSHIS
                            var highestAsk = BtrexData.Markets[canceledOrderData.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key;
                            var satoshis = canceledOrderData.QuantityRemaining * (highestAsk * 0.9975M);
                            if (satoshis < minimumTradeSatoshis)
                            {
                                //ORDER IS COMPLETE, CALL EXE CALLBACK
                                RemoveNewOrder(order.Key);
                                order.Value.CompleteOrder(canceledOrderData);
                                continue;
                            }

                            //REPLACE ORDER
                            var order2 = await BtrexREST.PlaceLimitOrder2(canceledOrderData.Exchange, "SELL", canceledOrderData.QuantityRemaining, BtrexData.Markets[canceledOrderData.Exchange].OrderBook.Asks.OrderBy(a => a.Key).First().Key);
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
                                order.Value.CompleteOrder(canceledOrderData);
                                continue;
                            }

                            //UPDATE DATA BY COMBINING CANCELED-ORDDATA AND ORDER2DATA
                            OpenOrders[order.Key].ReplaceOrder(canceledOrderData, order2data.result);

                        }
                    }

                }

                Thread.Sleep(LoopFrequency);

            }

            Trace.WriteLine("\r\n\r\n    @@@ TradeControl-Thread STOPPED @@@\r\n\r\n");
        }


        



        //*****************************************************************


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

        private async Task<GetOrderResult> CancelOrderGetData(string UniqueID, string OrderID)
        {
            //CANCEL ORDER:
            var cancelRequest = await BtrexREST.CancelLimitOrder(OrderID);
            if (!cancelRequest.success)
            {
                Trace.WriteLine("    !!!!ERR CANCEL-ORDER>>> " + UniqueID + ": " + cancelRequest.message);
                return null;
            }

            //Get data from cancelled order:
            var canceledOrderData = new GetOrderResponse();
            do
            {
                canceledOrderData = await BtrexREST.GetOrder(OrderID);
                if (!canceledOrderData.success)
                {
                    Trace.WriteLine("    !!!!ERR GET-CANCELED-ORDDATA>>> " + UniqueID + ": " + canceledOrderData.message);
                }
            }
            while (canceledOrderData.result == null || canceledOrderData.result.IsOpen == false);

            return canceledOrderData.result;
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
            //CREATE ORDER-DATA FROM ord
            var type = "";
            var TotRes = 0M;
            var openTime = DateTime.UtcNow;
            
            if (ord.BUYorSELL.ToUpper() == "BUY")
            {
                type = "LIMIT_BUY";
                TotRes = ord.Qty * ord.Rate * 1.0025M;
            }
            else if (ord.BUYorSELL.ToUpper() == "SELL")
            {
                type = "LIMIT_SELL";
                TotRes = ord.Qty * ord.Rate * 1.0025M;
            }

            var orderData = new OpenOrder("VIRTUAL-NO-ID", ord.MarketDelta, type, ord.Qty, TotRes, ord.Qty, 0M, ord.Rate, 0M, 0M, 0M, 0M, TotRes, 0M, openTime, ord.DataUpdateCallback, ord.ExecutionCompleteCallback, ord.CandlePeriod);
            var orderResult = new GetOrderResult()
            {
                CancelInitiated = false,
                Opened = openTime,
                Closed = DateTime.UtcNow,
                Exchange = ord.MarketDelta,
                IsOpen = false,
                Limit = ord.Rate,
                Price = TotRes,
                Quantity = ord.Qty,
                Reserved = TotRes,
                Type = type,
                OrderUuid = orderData.OrderUuid
            };
                 
            //CALL EXE-CALLBACK WITH MOCK DATA:
            orderData.CompleteOrder(orderResult);
        }


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
                var vSell = new GetOrderResult()
                {
                    Exchange = stopLoss.MarketDelta,
                    Quantity = stopLoss.Quantity,
                    PricePerUnit = BtrexData.Markets[stopLoss.MarketDelta].TradeHistory.RecentFills.Last().Rate * 0.9975M
                };
                
                stopLoss.ExecutionCallback(vSell, stopLoss.CandlePeriod);
            }

        }

    }
    

}
