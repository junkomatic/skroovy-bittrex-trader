using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public static TradeMethods TradeMethods = new TradeMethods();

        private static HttpClient client = new HttpClient()
        {
            BaseAddress = new Uri("https://bittrex.com/api/v1.1/"),
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

        public static async Task<HistDataResponse> GetMarketHistoryV2(string delta)
        {
            HttpRequestMessage mesg = new HttpRequestMessage()
            {
                RequestUri = new Uri("https://bittrex.com/Api/v2.0/pub/market/GetTicks?marketName="+ delta +"&tickInterval=fiveMin", UriKind.Absolute),
                Method = HttpMethod.Get
            };

            HistDataResponse history = null;
            HttpResponseMessage response = await client.SendAsync(mesg);
            if (response.IsSuccessStatusCode)
                history = await response.Content.ReadAsAsync<HistDataResponse>();
            else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                do
                {
                    Thread.Sleep(50);
                    response = await client.SendAsync(mesg);
                } while (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable);
            }
            else
                Console.WriteLine("FAIL:  " + response.ReasonPhrase);

            history.MarketDelta = delta.Replace('-', '_');

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
                Console.WriteLine("FAIL:  " + response.ReasonPhrase);

            return Convert.ToDecimal(ticker.data.amount);
        }




    }
}
