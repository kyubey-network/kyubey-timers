using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Andoromeda.Kyubey.Models;
using Pomelo.AspNetCore.TimedJob;

namespace Andoromeda.Kyubey.Timers.Jobs
{
    public class WhaleExPriceJob
    {
        private class WhaleExPriceItem
        { 
            public string baseCurrency { get; set; }

            public double lastPrice { get; set; }
        }

        [Invoke(Begin = "2018-06-01", Interval = 1000 * 60, SkipWhileExecuting = true)]
        public void PullNewDexPrice(KyubeyContext db)
        {
            var tokens = db.Tokens
                .Where(x => !string.IsNullOrEmpty(x.NewDexId))
                .ToList();

            var results = GetWhaleExPriceAsync().Result;

            foreach (var x in tokens)
            {
                if (!results.Any(y => y.baseCurrency == x.Id))
                {
                    continue;
                }

                x.WhaleExPrice = results.First(y => y.baseCurrency == x.Id).lastPrice;
            }
        }

        private async Task<IEnumerable<WhaleExPriceItem>> GetWhaleExPriceAsync()
        {
            using (var client = new HttpClient { BaseAddress = new Uri("https://www.whaleex.com") })
            using (var response = await client.GetAsync("/BUSINESS/api/public/symbol"))
            {
                return await response.Content.ReadAsAsync<IEnumerable<WhaleExPriceItem>>();
            }
        }
    }
}
