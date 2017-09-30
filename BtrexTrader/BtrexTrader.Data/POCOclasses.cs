using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BtrexTrader
{

    public class MarketDataUpdate
    {
        public string MarketName { get; set; }
        public int Nounce { get; set; }
        public List<mdBuy> Buys { get; set; }
        public List<mdSell> Sells { get; set; }
        public List<mdFill> Fills { get; set; }
    }
    public class mdBuy
    {
        public int Type { get; set; }
        public decimal Rate { get; set; }
        public decimal Quantity { get; set; }
    }
    public class mdSell
    {
        public int Type { get; set; }
        public decimal Rate { get; set; }
        public decimal Quantity { get; set; }
    }
    public class mdFill
    {
        public string OrderType { get; set; }
        public decimal Rate { get; set; }
        public decimal Quantity { get; set; }
        public string TimeStamp { get; set; }
    }


    //Its possible the [A]: is root here...
    //public class SummariesUpdate
    //{
    //    public string H { get; set; }
    //    public string M { get; set; }
    //    public List<A> A { get; set; }
    //}
    public class SummariesUpdate
    {
        public int Nounce { get; set; }
        public List<DeltaSummUp> Deltas { get; set; }
    }
    public class DeltaSummUp
    {
        public string MarketName { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Volume { get; set; }
        public decimal Last { get; set; }
        public decimal BaseVolume { get; set; }
        public string TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public int OpenBuyOrders { get; set; }
        public int OpenSellOrders { get; set; }
        public decimal PrevDay { get; set; }
        public string Created { get; set; }
    }


    public class MarketQueryResponse
    {
        public string MarketName { get; set; }
        public int Nounce { get; set; }
        public List<Buy> Buys { get; set; }
        public List<Sell> Sells { get; set; }
        public List<Fill> Fills { get; set; }
    }
    public class Buy
    {
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
    }
    public class Sell
    {
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
    }
    public class Fill
    {
        public int Id { get; set; }
        public string TimeStamp { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        public string FillType { get; set; }
        public string OrderType { get; set; }
    }


    public class TickerResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public TickerResult result { get; set; }
    }
    public class TickerResult
    {
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Last { get; set; }
    }


    public class MarketSummary
    {
        public bool success { get; set; }
        public string message { get; set; }
        public List<SummaryResult> result { get; set; }
    }
    public class SummaryResult
    {
        public string MarketName { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Volume { get; set; }
        public decimal Last { get; set; }
        public decimal BaseVolume { get; set; }
        public string TimeStamp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public int OpenBuyOrders { get; set; }
        public int OpenSellOrders { get; set; }
        public decimal PrevDay { get; set; }
        public string Created { get; set; }
        public object DisplayMarketName { get; set; }
    }


    public class MarketHistoryResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public List<MarketHistoryResult> result { get; set; }
    }
    public class MarketHistoryResult
    {
        public int Id { get; set; }
        public string TimeStamp { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        public string FillType { get; set; }
        public string OrderType { get; set; }
    }


    public class GetBalancesResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public List<BalancesResult> result { get; set; }
    }
    public class BalancesResult
    {
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public decimal Available { get; set; }
        public decimal Pending { get; set; }
        public string CryptoAddress { get; set; }
        public bool Requested { get; set; }
        public object Uuid { get; set; }
    }
    

    public class GetBalanceResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public BalanceResult result { get; set; }
    }
    public class BalanceResult
    {
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public decimal Available { get; set; }
        public decimal Pending { get; set; }
        public string CryptoAddress { get; set; }
        public bool Requested { get; set; }
        public object Uuid { get; set; }
    }


    public class LimitOrderResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public LimitOrderResult result { get; set; }
    }
    public class LimitOrderResult
    {
        public string uuid { get; set; }
    }


    public class OpenOrdersResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public List<OpenOrdersResult> result { get; set; }
    }
    public class OpenOrdersResult
    {
        public object Uuid { get; set; }
        public string OrderUuid { get; set; }
        public string Exchange { get; set; }
        public string OrderType { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityRemaining { get; set; }
        public decimal Limit { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public object PricePerUnit { get; set; }
        public string Opened { get; set; }
        public object Closed { get; set; }
        public bool CancelInitiated { get; set; }
        public bool ImmediateOrCancel { get; set; }
        public bool IsConditional { get; set; }
        public object Condition { get; set; }
        public object ConditionTarget { get; set; }
    }

    public class GetOrderResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public GetOrderResult result { get; set; }
    }
    public class GetOrderResult
    {
        public object AccountId { get; set; }
        public string OrderUuid { get; set; }
        public string Exchange { get; set; }
        public string Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityRemaining { get; set; }
        public decimal Limit { get; set; }
        public decimal Reserved { get; set; }
        public decimal ReserveRemaining { get; set; }
        public decimal CommissionReserved { get; set; }
        public decimal CommissionReserveRemaining { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal Price { get; set; }
        public object PricePerUnit { get; set; }
        public string Opened { get; set; }
        public object Closed { get; set; }
        public bool IsOpen { get; set; }
        public string Sentinel { get; set; }
        public bool CancelInitiated { get; set; }
        public bool ImmediateOrCancel { get; set; }
        public bool IsConditional { get; set; }
        public string Condition { get; set; }
        public object ConditionTarget { get; set; }
    }


    public class GetMarketsResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
        public List<GetMarketsResult> result { get; set; }
    }
    public class GetMarketsResult
    {
        public string MarketCurrency { get; set; }
        public string BaseCurrency { get; set; }
        public string MarketCurrencyLong { get; set; }
        public string BaseCurrencyLong { get; set; }
        public decimal MinTradeSize { get; set; }
        public string MarketName { get; set; }
        public bool IsActive { get; set; }
        public string Created { get; set; }
    }


    public class CBsellPriceResponse
    {
        public CBsellPriceData data { get; set; }
    }
    public class CBsellPriceData
    {
        public string amount { get; set; }
        public string currency { get; set; }
    }



}
