using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Pomelo.AspNetCore.TimedJob;
using Andoromeda.Kyubey.Models;

namespace Andoromeda.Kyubey.Timers.Jobs
{
    public class NewDexPriceJob : Job
    {
        private class NewDexPrice
        { 
            public NewDexPriceSymbolInfo symbolInfo { get; set; }
        }

        private class NewDexPriceSymbolInfo
        {
            public double bidPrice { get; set; }

            public double askPrice { get; set; }
        }

        [Invoke(Begin = "2018-06-01 0:01", Interval = 5000 * 60, SkipWhileExecuting = true)]
        public void PullNewDexPrice(KyubeyContext db)
        {
            var tokens = db.Tokens
                .Where(x => !string.IsNullOrEmpty(x.NewDexId))
                .ToList();

            foreach(var x in tokens)
            {
                try
                {
                    var ret = GetNewDexPriceAsync(x.NewDexId).Result;
                    x.NewDexAsk = ret.ask;
                    x.NewDexBid = ret.bid;
                    db.SaveChanges();
                }
                catch
                {
                    // TODO: Log failures
                }
            }
        }

        private async Task<(double ask, double bid)> GetNewDexPriceAsync(string newdexId)
        {
            using (var client = new HttpClient { BaseAddress = new Uri("https://newdex.io") })
            using (var response = await client.PostAsync("/api/symbol/getSymbolInfo", new FormUrlEncodedContent(new Dictionary<string, string> {
                { "symbol", newdexId }
            })))
            {
                var result = await response.Content.ReadAsAsync<NewDexPrice>();
                return (result.symbolInfo.askPrice, result.symbolInfo.bidPrice);
            }
        }
    }
}
