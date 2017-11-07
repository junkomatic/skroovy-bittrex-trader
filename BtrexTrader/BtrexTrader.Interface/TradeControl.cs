using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BtrexTrader.Interface
{
    public class TradeControl
    {
        //TODO: HIGHER TRADE FUNCTIONS



        public async Task<bool> MatchBottomAskUntilFilled(string orderID, string qtyORamt)
        {
            GetOrderResponse order = await BtrexREST.GetOrder(orderID);
            if (!order.success)
            {
                Console.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }

            while (order.result.IsOpen)
            {
                TickerResponse tick = await BtrexREST.GetTicker(order.result.Exchange);
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
                    LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Console.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Console.Write("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    string coin = order.result.Exchange.Split('-')[1];
                    GetBalanceResponse bal = await BtrexREST.GetBalance(coin);
                    if (!bal.success)
                        Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < newQty)
                    {
                        Thread.Sleep(150);
                        bal = await BtrexREST.GetBalance(coin);
                        if (!bal.success)
                            Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await BtrexREST.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Console.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await BtrexREST.PlaceLimitOrder(order.result.Exchange, "sell", newQty, tick.result.Ask);
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
                order = await BtrexREST.GetOrder(orderID);
                if (!order.success)
                {
                    Console.WriteLine("    !!!!ERR GET-ORDER2: " + order.message);
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
                Console.WriteLine("    !!!!ERR GET-ORDER: " + order.message);
                return false;
            }


            while (order.result.IsOpen)
            {
                TickerResponse tick = await BtrexREST.GetTicker(order.result.Exchange);
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
                        newQty = ((order.result.Limit * 1.0025M) * order.result.QuantityRemaining) / (tick.result.Bid * 0.9975M);
                    else if (qtyORamt == "qty")
                        newQty = order.result.QuantityRemaining;

                    //CHECK THAT NEXT ORDER WILL BE GREATER THAN MINIMUM ORDER PLACEMENT:
                    if (newQty * (tick.result.Bid * 1.0025M) < 0.0005M)
                    {
                        Console.WriteLine("***TRAILING-ORDER: " + order.result.OrderUuid);
                        return true;
                    }

                    //CANCEL EXISTING ORDER:
                    LimitOrderResponse cancel = await BtrexREST.CancelLimitOrder(order.result.OrderUuid);
                    if (!cancel.success)
                    {
                        Console.WriteLine("    !!!!ERR CANCEL-MOVE>> " + cancel.message);
                        return false;
                    }
                    //else
                    //    Console.WriteLine("CANCELED");

                    //Kill time after cancel by checking bal to make sure its available:
                    GetBalanceResponse bal = await BtrexREST.GetBalance("btc");
                    if (!bal.success)
                        Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    while (bal.result.Available < (newQty * (tick.result.Bid * 1.0025M)))
                    {
                        Console.WriteLine("###BAL");
                        Thread.Sleep(150);
                        bal = await BtrexREST.GetBalance("btc");
                        if (!bal.success)
                            Console.WriteLine("    !!!!ERR GET-BALANCE>> " + bal.message);
                    }

                    //Get recent tick again
                    tick = await BtrexREST.GetTicker(order.result.Exchange);
                    if (!tick.success)
                    {
                        Console.WriteLine("    !!!!ERR TICKER3>> " + tick.message);
                        return false;
                    }

                    //REPLACE ORDER AT NEW RATE/QTY:
                    LimitOrderResponse newOrd = await BtrexREST.PlaceLimitOrder(order.result.Exchange, "buy", newQty, tick.result.Bid);
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

                order = await BtrexREST.GetOrder(orderID);
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
