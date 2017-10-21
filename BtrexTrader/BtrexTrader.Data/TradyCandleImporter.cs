using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trady.Core;
using Trady.Core.Infrastructure;
using Trady.Core.Period;

namespace BtrexTrader.Data
{
    public class TradyCandleImporter : IImporter
    {
        public async Task<IReadOnlyList<Candle>> ImportAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null, PeriodOption period = PeriodOption.Daily, CancellationToken token = default(CancellationToken))
        {
            //TODO:COMPLETE IMPORTER
            //Import Candles from Data.Candle5m, get more data from sqlite
            //Assemble/Return IReadOnlyList in correct order.
            if (endTime == null)
                endTime = DateTime.UtcNow;








            return null;
        }




    }
}
