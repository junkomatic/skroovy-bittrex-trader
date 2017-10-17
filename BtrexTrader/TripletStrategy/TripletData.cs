using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BtrexTrader.Data;
using BtrexTrader.Data.MarketData;

namespace BtrexTrader.TripletStrategy
{
    public class TripletData
    {

        public bool tradingState { get; set; }
        public OrderBook BTCdelta { get; set; }
        public OrderBook ETHdelta { get; set; }
        public OrderBook B2Edelta { get; set; }
        private const decimal feeRate = 0.025M;
        public string IDtrade1 { get; set; }
        public string IDtrade2 { get; set; }
        public string IDtrade3 { get; set; }
        public string Coin { get; set; }


        public TripletData(OrderBook BTC, OrderBook ETH, OrderBook B2E)
        {
            tradingState = false;
            BTCdelta = BTC;
            ETHdelta = ETH;
            B2Edelta = B2E;
            Coin = BTC.MarketDelta.Split('-')[1];
        }

        public void Ready()
        {
            tradingState = false;
            IDtrade1 = string.Empty;
            IDtrade2 = string.Empty;
            IDtrade3 = string.Empty;
        }

        public TriCalcReturn CalcLeft(decimal initial)
        {
            decimal wager = initial;
            TriCalcReturn trades = new TriCalcReturn();
            const decimal tax = 0.0025M;

            decimal ALTamt = 0;
            foreach (KeyValuePair<decimal, decimal> ask in BTCdelta.Asks.ToArray().OrderBy(k => k.Key).Take(10))
            {
                decimal rate = ask.Key * (1 + tax);
                decimal askTotal = rate * ask.Value;
                if (wager <= askTotal)
                {
                    decimal purchaseAmt = wager / rate;
                    ALTamt += purchaseAmt;
                    wager = 0;
                    trades.Trades1.Add(ask.Key, purchaseAmt);
                    break;
                }

                ALTamt += ask.Value;
                wager -= askTotal;
                trades.Trades1.Add(ask.Key, ask.Value);
            }


            decimal ETHamt = 0;
            foreach (KeyValuePair<decimal, decimal> bid in ETHdelta.Bids.ToArray().OrderByDescending(k => k.Key).Take(10))
            {
                if (ALTamt <= bid.Value)
                {
                    ETHamt += (ALTamt * bid.Key) * (1 - tax);
                    trades.Trades2.Add(bid.Key, ALTamt);
                    ALTamt = 0;
                    break;
                }

                ETHamt += (bid.Value * bid.Key) * (1 - tax);
                ALTamt -= (bid.Value);
                trades.Trades2.Add(bid.Key, bid.Value);
            }

            decimal BTCresult = 0;
            foreach (KeyValuePair<decimal, decimal> bid in B2Edelta.Bids.ToArray().OrderByDescending(k => k.Key).Take(10))
            {
                if (ETHamt <= bid.Value)
                {
                    BTCresult += (ETHamt * bid.Key) * (1 - tax);
                    trades.Trades3.Add(bid.Key, ETHamt);
                    ETHamt = 0;
                    break;
                }

                BTCresult += (bid.Value * bid.Key) * (1 - tax);
                ETHamt -= (bid.Value);
                trades.Trades3.Add(bid.Key, bid.Value);
            }

            trades.BTCresult = BTCresult - initial;
            return trades;
        }

        public TriCalcReturn CalcRight(decimal initial)
        {
            decimal wager = initial;
            TriCalcReturn trades = new TriCalcReturn();
            decimal tax = 0.00025M;

            decimal ETHamt = 0;
            foreach (KeyValuePair<decimal, decimal> ask in B2Edelta.Asks.ToArray().OrderBy(k => k.Key).Take(10))
            {
                decimal rate = ask.Key * (1 + tax);
                decimal askTotal = rate * ask.Value;
                if (wager <= askTotal)
                {
                    decimal purchaseAmount = wager / rate;
                    ETHamt += purchaseAmount;
                    wager = 0;
                    trades.Trades1.Add(ask.Key, purchaseAmount);
                    break;
                }
                ETHamt += ask.Value;
                wager -= askTotal;
                trades.Trades1.Add(ask.Key, ask.Value);
            }

            decimal ALTamt = 0;
            foreach (KeyValuePair<decimal, decimal> ask in ETHdelta.Asks.ToArray().OrderBy(k => k.Key).Take(10))
            {
                decimal rate = ask.Key * (1 + tax);
                decimal askTotal = rate * ask.Value;
                if (ETHamt <= askTotal)
                {
                    decimal purchaseAmount = wager / rate;
                    ALTamt += purchaseAmount;
                    ETHamt = 0;
                    trades.Trades2.Add(ask.Key, purchaseAmount);
                    break;
                }
                ALTamt += ask.Value;
                ETHamt -= askTotal;
                trades.Trades2.Add(ask.Key, ask.Value);
            }

            decimal BTCresult = 0;
            foreach (KeyValuePair<decimal, decimal> bid in BTCdelta.Bids.ToArray().OrderByDescending(k => k.Key).Take(10))
            {
                if (ALTamt <= bid.Value)
                {
                    BTCresult += (ALTamt * bid.Key) * (1 - tax);
                    trades.Trades3.Add(bid.Key, ALTamt);
                    ALTamt = 0;
                    break;
                }
                BTCresult += (bid.Value * bid.Key) * (1 - tax);
                ALTamt -= (bid.Value);
                trades.Trades3.Add(bid.Key, bid.Value);
            }

            trades.BTCresult = BTCresult - initial;
            return trades;
        }
    }

    public class TriCalcReturn
    {
        public decimal BTCresult { get; set; }
        public Dictionary<decimal, decimal> Trades1 { get; set; }
        public Dictionary<decimal, decimal> Trades2 { get; set; }
        public Dictionary<decimal, decimal> Trades3 { get; set; }

        public TriCalcReturn()
        {
            Trades1 = new Dictionary<decimal, decimal>();
            Trades2 = new Dictionary<decimal, decimal>();
            Trades3 = new Dictionary<decimal, decimal>();
        }
    }

}