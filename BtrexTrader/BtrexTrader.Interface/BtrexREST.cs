﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Configuration;

namespace BtrexTrader.Interface
{
    public static class BtrexREST
    {
        private readonly static string API_KEY = ConfigurationManager.AppSettings["API_KEY"];
        private readonly static string SECRET_KEY = ConfigurationManager.AppSettings["SECRET_KEY"];

        private readonly static string API_KEY2 = ConfigurationManager.AppSettings["API_KEY2"];
        private readonly static string SECRET_KEY2 = ConfigurationManager.AppSettings["SECRET_KEY2"];

        private readonly static string API_KEY3 = ConfigurationManager.AppSettings["API_KEY3"];
        private readonly static string SECRET_KEY3 = ConfigurationManager.AppSettings["SECRET_KEY3"];

        public static TradeControl TradeController = new TradeControl();

        private static HttpClient client = new HttpClient()
        {
            BaseAddress = new Uri("https://bittrex.com/api/v1.1/"),
            Timeout = TimeSpan.FromMinutes(3)
        };

        public static async Task<decimal> getUSD()
        {
            decimal rate = await GetCBsellPrice();
            return rate;
        }

        public static async Task<TickerResponse> GetTicker(string delta)
        {
            TickerResponse ticker = null;
            HttpResponseMessage response = await client.GetAsync("public/getticker?market=" + delta);
            if (response.IsSuccessStatusCode)
                ticker = await response.Content.ReadAsAsync<TickerResponse>();
            return ticker;
        }

        public static async Task<GetMarketsResponse> GetMarkets()
        {
            GetMarketsResponse marketsResponse = null;
            HttpResponseMessage response = await client.GetAsync("public/getmarkets");
            if (response.IsSuccessStatusCode)
                marketsResponse = await response.Content.ReadAsAsync<GetMarketsResponse>();
            return marketsResponse;
        }

        public static async Task<MarketSummary> GetMarketSummary(string delta = null)
        {
            string uri;
            if (delta == null)
                uri = "public/getmarketsummaries";
            else
                uri = "public/getmarketsummary?market=" + delta;

            MarketSummary summary = null;
            HttpResponseMessage response = await client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
            {
                summary = await response.Content.ReadAsAsync<MarketSummary>();
            }
            return summary;
        }

        //public static async Task<MarketHistoryResponse> GetMarketHistory(string delta)
        //{
        //    MarketHistoryResponse marketHistory = null;
        //    HttpResponseMessage response = await client.GetAsync("");
        //    if (response.IsSuccessStatusCode)
        //        marketHistory = await response.Content.ReadAsAsync<MarketHistoryResponse>();

        //    return marketHistory;
        //}

        public static async Task<HistDataResponse> GetMarketHistoryV2(string delta, string period)
        {                        
            HistDataResponse history = new HistDataResponse();
            while (true)
            {
                try
                {
                    var mesg = new HttpRequestMessage()
                    {
                        RequestUri = new Uri("https://bittrex.com/Api/v2.0/pub/market/GetTicks?marketName=" + delta + "&tickInterval=" + period + "", UriKind.Absolute),
                        Method = HttpMethod.Get
                    };
                    HttpResponseMessage response = await client.SendAsync(mesg);

                    if (response.IsSuccessStatusCode)
                        history = await response.Content.ReadAsAsync<HistDataResponse>();
                    else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || history.result == null)
                    {
                        do
                        {

                            Thread.Sleep(500);
                            HttpRequestMessage mesgClone = new HttpRequestMessage()
                            {
                                RequestUri = new Uri("https://bittrex.com/Api/v2.0/pub/market/GetTicks?marketName=" + delta + "&tickInterval=" + period + "", UriKind.Absolute),
                                Method = HttpMethod.Get
                            };

                            response = await client.SendAsync(mesgClone);
                            history = await response.Content.ReadAsAsync<HistDataResponse>();

                        } while (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || history.result == null);

                    }
                    else
                        Trace.WriteLine("FAIL:  " + response.ReasonPhrase);


                    history.MarketDelta = string.Empty;

                    if (history.result == null || history.MarketDelta == null || delta.Replace('-', '_') == null)
                        Trace.WriteLine("\r\nHIST NULL " + delta + " RESPONSE CODE: " + response.StatusCode + "\r\n\r\n");


                    history.MarketDelta = delta.Replace('-', '_');
                    if (period.ToUpper() != "ONEMIN" && history.result.Count > 0)
                        history.result.Remove(history.result.Last());
                    break;

                }
                catch (Exception e)
                {
                    //Trace.WriteLine("\r\n222HIST NULL " + delta + " RESPONSE CODE: " + response.StatusCode + "\r\n\r\n");

                }
            }
            return history;
        }
        

        public static async Task<LimitOrderResponse> PlaceLimitOrder(string delta, string buyORsell, decimal qty, decimal rate)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            string uri = string.Format("market/{0}limit?apikey={1}&nonce={2}&market={3}&quantity={4}&rate={5}", buyORsell, API_KEY, timeUnix, delta, qty, rate);
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            LimitOrderResponse limitOrder = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                limitOrder = await response.Content.ReadAsAsync<LimitOrderResponse>();
            }

            return limitOrder;
        }

        public static async Task<LimitOrderResponse> PlaceLimitOrder2(string delta, string buyORsell, decimal qty, decimal rate)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            string uri = string.Format("market/{0}limit?apikey={1}&nonce={2}&market={3}&quantity={4}&rate={5}", buyORsell, API_KEY2, timeUnix, delta, qty, rate);
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            string sign = GetSignature2(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            LimitOrderResponse limitOrder = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                limitOrder = await response.Content.ReadAsAsync<LimitOrderResponse>();
            }

            return limitOrder;
        }

        public static async Task<LimitOrderResponse> PlaceLimitOrder3(string delta, string buyORsell, decimal qty, decimal rate)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            string uri = string.Format("market/{0}limit?apikey={1}&nonce={2}&market={3}&quantity={4}&rate={5}", buyORsell, API_KEY3, timeUnix, delta, qty, rate);
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            string sign = GetSignature3(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            LimitOrderResponse limitOrder = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                limitOrder = await response.Content.ReadAsAsync<LimitOrderResponse>();
            }

            return limitOrder;
        }

        public static async Task<LimitOrderResponse> CancelLimitOrder(string orderID)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            string uri = string.Format("market/cancel?apikey={0}&nonce={1}&uuid={2}", API_KEY, timeUnix, orderID);
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            LimitOrderResponse limitOrder = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                limitOrder = await response.Content.ReadAsAsync<LimitOrderResponse>();
            }

            return limitOrder;
        }

        public static async Task<GetBalanceResponse> GetBalance(string curr)
        {
            //Get nonce:
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            //Form request Uri:
            string uri = string.Format("account/getbalance?apikey={0}&nonce={1}&currency={2}", API_KEY, timeUnix, curr);
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            GetBalanceResponse balanceResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                balanceResponse = await response.Content.ReadAsAsync<GetBalanceResponse>();
            }

            return balanceResponse;
        }

        public static async Task<GetBalancesResponse> GetBalances()
        {
            //Get nonce:
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            //Form request Uri:
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("account/getbalances?apikey=" + API_KEY + "&nonce=" + timeUnix, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            GetBalancesResponse balancesResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                balancesResponse = await response.Content.ReadAsAsync<GetBalancesResponse>();
            }

            return balancesResponse;
        }

        public static async Task<OpenOrdersResponse> GetOpenOrders(string delta = null)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            string uri;
            if (delta == null)
                uri = string.Format("market/getopenorders?apikey={0}&nonce={1}", API_KEY, timeUnix);
            else
                uri = string.Format("market/getopenorders?apikey={0}&nonce={1}&market={2}", API_KEY, timeUnix, delta);

            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            OpenOrdersResponse ordersResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                ordersResponse = await response.Content.ReadAsAsync<OpenOrdersResponse>();
            }

            return ordersResponse;
        }

        public static async Task<GetOrderResponse> GetOrder(string orderID)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("account/getorder?apikey=" + API_KEY + "&nonce=" + timeUnix + "&uuid=" + orderID, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            GetOrderResponse orderResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                orderResponse = await response.Content.ReadAsAsync<GetOrderResponse>();
            }

            return orderResponse;
        }


        public static async Task<GetOrderResponse> GetOrder2(string orderID)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("account/getorder?apikey=" + API_KEY2 + "&nonce=" + timeUnix + "&uuid=" + orderID, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature2(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            GetOrderResponse orderResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                orderResponse = await response.Content.ReadAsAsync<GetOrderResponse>();
            }

            return orderResponse;
        }


        public static async Task<GetOrderResponse> GetOrder3(string orderID)
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double timeUnix = t.TotalSeconds;

            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("account/getorder?apikey=" + API_KEY3 + "&nonce=" + timeUnix + "&uuid=" + orderID, UriKind.Relative),
                Method = HttpMethod.Get
            };

            //Compute Signature and add to request header: 
            string sign = GetSignature3(new Uri(client.BaseAddress, mesg.RequestUri));
            mesg.Headers.Add("apisign", sign);

            //SendAsync request and await response object:
            GetOrderResponse orderResponse = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
            {
                orderResponse = await response.Content.ReadAsAsync<GetOrderResponse>();
            }

            return orderResponse;
        }


        private static string GetSignature(Uri uri)
        {
            Encoding encoding = Encoding.UTF8;
            using (HMACSHA512 hmac = new HMACSHA512(encoding.GetBytes(SECRET_KEY)))
            {
                var msg = encoding.GetBytes(uri.ToString());
                var hash = hmac.ComputeHash(msg);
                return BitConverter.ToString(hash).ToLower().Replace("-", string.Empty);
            }
        }

        private static string GetSignature2(Uri uri)
        {
            Encoding encoding = Encoding.UTF8;
            using (HMACSHA512 hmac = new HMACSHA512(encoding.GetBytes(SECRET_KEY2)))
            {
                var msg = encoding.GetBytes(uri.ToString());
                var hash = hmac.ComputeHash(msg);
                return BitConverter.ToString(hash).ToLower().Replace("-", string.Empty);
            }
        }

        private static string GetSignature3(Uri uri)
        {
            Encoding encoding = Encoding.UTF8;
            using (HMACSHA512 hmac = new HMACSHA512(encoding.GetBytes(SECRET_KEY3)))
            {
                var msg = encoding.GetBytes(uri.ToString());
                var hash = hmac.ComputeHash(msg);
                return BitConverter.ToString(hash).ToLower().Replace("-", string.Empty);
            }
        }


        public static async Task<decimal> GetCBsellPrice()
        {
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("https://api.coinbase.com/v2/prices/BTC-USD/sell", UriKind.Absolute),
                Method = HttpMethod.Get
            };

            mesg.Headers.Add("CB-VERSION", "2017-08-25");

            CBsellPriceResponse ticker = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
                ticker = await response.Content.ReadAsAsync<CBsellPriceResponse>();
            else
                Trace.WriteLine("FAIL:  " + response.ReasonPhrase);

            return Convert.ToDecimal(ticker.data.amount);
        }


        public static async Task<List<string>> GetTopMarketsByBVwithETHdelta(int n)
        {
            MarketSummary markets = await BtrexREST.GetMarketSummary();
            Dictionary<string, decimal> topMarketsBTC = new Dictionary<string, decimal>();
            List<string> topMarketsETH = new List<string>();
            foreach (SummaryResult market in markets.result)
            {
                string mkbase = market.MarketName.Split('-')[0];
                if (mkbase == "BTC")
                {
                    topMarketsBTC.Add(market.MarketName, market.BaseVolume);
                }
                else if (mkbase == "ETH")
                {
                    topMarketsETH.Add(market.MarketName.Split('-')[1]);
                }
            }

            List<string> mks = new List<string>();
            foreach (KeyValuePair<string, decimal> mk in topMarketsBTC.OrderByDescending(x => x.Value).Take(n))
            {
                string coin = mk.Key.Split('-')[1];
                if (topMarketsETH.Contains(coin))
                    mks.Add(coin);
            }

            Trace.WriteLine(string.Format("Markets: {0}", mks.Count));
            return mks;
        }

        public static async Task<List<string>> GetTopMarketsByBVbtcOnly(int n)
        {
            MarketSummary markets = await BtrexREST.GetMarketSummary();
            Dictionary<string, decimal> topMarketsBTC = new Dictionary<string, decimal>();
            foreach (SummaryResult market in markets.result)
            {
                string mkbase = market.MarketName.Split('-')[0];
                if (mkbase == "BTC")
                {
                    topMarketsBTC.Add(market.MarketName, market.BaseVolume);
                }
            }

            List<string> mks = new List<string>();
            foreach (KeyValuePair<string, decimal> mk in topMarketsBTC.OrderByDescending(x => x.Value).Take(n))
            {
                mks.Add(mk.Key.Split('-')[1]);
            }

            return mks;
        }

    }
}
