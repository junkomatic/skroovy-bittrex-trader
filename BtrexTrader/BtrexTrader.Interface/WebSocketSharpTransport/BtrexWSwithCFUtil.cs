using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Transports;
using BtrexTrader.Data;

namespace BtrexTrader.Interface.WebSocketSharpTransport
{
    internal static class BittrexConstants
    {
        public const string HubName = "CoreHub";
        public const string __cfduidCookieName = "__cfduid";
        public const string cf_clearanceCookieName = "cf_clearance";
        public const string accessTokenCookieName = ".AspNet.ApplicationCookie";
        public const string UpdateSummaryStateEventName = "updateSummaryState";
        public const string UpdateExchangeStateEventName = "updateExchangeState";
        public const string UpdateOrderStateEventName = "updateOrderState";
    }

    public sealed class ConnectionConfiguration
    {
        public CookieContainer CookieContainer { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }

    public sealed class BittrexFeedConnectionConfiguration
    {
        public string AccessToken { get; set; }
        public ConnectionConfiguration Connection { get; set; }

        public static BittrexFeedConnectionConfiguration Default
        {
            get { return new BittrexFeedConnectionConfiguration(); }
        }
    }


    public class BtrexWSwithCFUtil
    {
        private readonly HubConnection _connection;
        private readonly IHubProxy _hubProxy;
        private readonly Uri _feedUri;

        public BtrexWSwithCFUtil(Uri feedUri)
        {
            if (feedUri == null)
                throw new ArgumentNullException(nameof(feedUri));

            _feedUri = feedUri;

            _connection = new HubConnection(feedUri.OriginalString);
            _connection.CookieContainer = new CookieContainer();            
            _connection.Closed += Connection_Closed;
            _connection.ConnectionSlow += Connection_ConnectionSlow;
            _connection.Error += Connection_Error;
            _connection.Reconnected += Connection_Reconnected;
            _connection.Reconnecting += Connection_Reconnecting;
            _connection.StateChanged += Connection_StateChanged;
            //_connection.Received += Connection_Received;
            //_connection.TraceLevel = TraceLevels.Events;
            //_connection.TraceWriter = Console.Out;

            _hubProxy = _connection.CreateHubProxy(BittrexConstants.HubName);
            _hubProxy.On<MarketDataUpdate>("updateExchangeState", update => BtrexData.UpdateQueue.Enqueue(update));
            //_hubProxy.On<SummariesUpdate>("updateSummaryState", update => Console.WriteLine("FULL SUMMARY: "));
            //_hubProxy.On<dynamic>(BittrexConstants.UpdateOrderStateEventName, this.OnUpdateOrderState);

            
        }

        public HubConnection Connection
        {
            get { return this._connection; }
        }

        public IHubProxy HubProxy
        {
            get { return this._hubProxy; }
        }

        public async Task Connect(BittrexFeedConnectionConfiguration configuration)
        {
            DefaultHttpClientEx httpClient = new DefaultHttpClientEx();
            AutoTransport autoTransport = null;

            if (configuration != null)
            {
                if (configuration.Connection != null)
                {
                    var transports = new IClientTransport[]
                    {
                        new WebSocketSharpTransport(httpClient),
                        new LongPollingTransport(httpClient)
                    };

                    autoTransport = new AutoTransport(httpClient, transports);

                    _connection.CookieContainer = configuration.Connection.CookieContainer;

                    if (configuration.Connection.Headers != null)
                    {
                        foreach (var header in configuration.Connection.Headers)
                            _connection.Headers[header.Key] = header.Value;
                    }

                    _connection.TransportConnectTimeout = new TimeSpan(0, 0, 10);
                }

                if (!string.IsNullOrEmpty(configuration.AccessToken))
                {
                    var aspNetApplicationCookie = new Cookie(BittrexConstants.accessTokenCookieName, configuration.AccessToken, "/", ".bittrex.com");
                    _connection.CookieContainer.Add(_feedUri, aspNetApplicationCookie);
                }
            }

            if (autoTransport == null)
                autoTransport = new AutoTransport(httpClient);

            await _connection.Start(autoTransport);
        }

        public void Disconnect()
        {
            Task.Run(() => _connection.Stop());
        }

        //private void OnUpdateOrderState(dynamic obj)
        //{
        //    Console.WriteLine("OnUpdateOrderState");
        //}

        //private void OnUpdateExchangeState(dynamic obj)
        //{
        //    Console.WriteLine("OnUpdateExchangeState");
        //}

        //private void OnUpdateSummaryState(dynamic obj)
        //{
        //    Console.WriteLine("OnUpdateSummaryState");
        //}

        #region event handlers
        private void Connection_StateChanged(StateChange obj)
        {
            Console.WriteLine($"State changed {obj.OldState} -> {obj.NewState}.");
        }

        private void Connection_Reconnecting()
        {
            Console.WriteLine($"Reconnecting.");
        }

        private void Connection_Reconnected()
        {
            Console.WriteLine($"Reconnected.");
        }

        private void Connection_Received(string obj)
        {
            Console.WriteLine($"Received {obj.Length} bytes of data.");
        }

        private void Connection_Error(Exception obj)
        {
            Console.WriteLine($"Error: {obj.Message}.");
        }

        private void Connection_ConnectionSlow()
        {
            Console.WriteLine($"Connection slow.");
        }

        private void Connection_Closed()
        {
            Console.WriteLine($"Connection closed.");
        }
        #endregion


    }


    internal static class HttpMessageHandlerExtensions
    {
        public static HttpMessageHandler GetMostInnerHandler(this HttpMessageHandler self)
        {
            var delegatingHandler = self as DelegatingHandler;
            return delegatingHandler == null ? self : delegatingHandler.InnerHandler.GetMostInnerHandler();
        }
    }

    internal static class CookieCollectionExtensions
    {
        public static Cookie GetCFIdCookie(this CookieCollection self)
        {
            return self[BittrexConstants.__cfduidCookieName];
        }

        public static Cookie GetCFClearanceCookie(this CookieCollection self)
        {
            return self[BittrexConstants.cf_clearanceCookieName];
        }
    }

    internal static class CookieExtensions
    {
        public static string ToHeaderValue(this Cookie cookie)
        {
            return $"{cookie.Name}={cookie.Value}";
        }

        public static IEnumerable<Cookie> GetCookiesByName(this CookieContainer container, Uri uri, params string[] names)
        {
            return container.GetCookies(uri).Cast<Cookie>().Where(c => names.Contains(c.Name)).ToList();
        }
    }
}
