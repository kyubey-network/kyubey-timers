using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Pomelo.AspNetCore.TimedJob;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Andoromeda.Framework.EosNode;
using Andoromeda.Framework.Logger;
using Andoromeda.Kyubey.Models;
using Andoromeda.Kyubey.Timers.Models;

namespace Andoromeda.Kyubey.Timers.Jobs
{
    public class ActionHistoryJob : Job
    {
        [Invoke(Begin = "2018-06-01", Interval = 1000 * 5, SkipWhileExecuting = true)]
        public void PollDexActions(IConfiguration config, KyubeyContext db, ILogger logger, NodeApiInvoker nodeApiInvoker)
        {
            TryHandleDexActionAsync(config, db, logger, nodeApiInvoker).Wait();
        }

        private async Task TryHandleDexActionAsync(IConfiguration config, KyubeyContext db, ILogger logger, NodeApiInvoker nodeApiInvoker)
        {
            while (true)
            {
                var actions = await LookupDexActionAsync(config, db, logger, nodeApiInvoker);
                if (actions != null)
                {
                    foreach (var act in actions)
                    {
                        Console.WriteLine($"Handling action log {act.account_action_seq} {act.action_trace.act.name}");

                        switch (act.action_trace.act.name)
                        {
                            case "sellmatch":
                                await HandleSellMatchAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            case "buymatch":
                                await HandleBuyMatchAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            case "sellreceipt":
                                await HandleSellReceiptAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            case "buyreceipt":
                                await HandleBuyReceiptAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            case "cancelbuy":
                                await HandleCancelBuyAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            case "cancelsell":
                                await HandleCancelSellAsync(db, act.action_trace.act.data, act.block_time, logger);
                                break;
                            default:
                                continue;
                        }
                    }
                }
                if (actions == null || actions.Count() < 100)
                {
                    break;
                }
            }
        }

        private async Task HandleCancelSellAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string symbol = Convert.ToString(data.data.symbol);
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == symbol);
                if (order != null)
                {
                    db.DexSellOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleCancelBuyAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string symbol = Convert.ToString(data.data.symbol);
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == symbol);
                if (order != null)
                {
                    db.DexBuyOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleSellReceiptAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string token = data.data.bid.Split(' ')[1];
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    db.DexSellOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexSellOrder
                {
                    Id = data.data.id,
                    Account = data.data.account,
                    Ask = Convert.ToDouble(data.data.ask.Split(' ')[0]),
                    Bid = Convert.ToDouble(data.data.bid.Split(' ')[0]),
                    UnitPrice = data.data.unit_price / 100000000.0,
                    Time = time,
                    TokenId = token
                };
                db.DexSellOrders.Add(order);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleBuyReceiptAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string token = data.data.ask.Split(' ')[1];
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    db.DexBuyOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexBuyOrder
                {
                    Id = data.data.id,
                    Account = data.data.account,
                    Ask = Convert.ToDouble(data.data.ask.Split(' ')[0]),
                    Bid = Convert.ToDouble(data.data.bid.Split(' ')[0]),
                    UnitPrice = data.data.unit_price / 100000000.0,
                    Time = time,
                    TokenId = token
                };
                db.DexBuyOrders.Add(order);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleSellMatchAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string token = data.data.bid.Split(' ')[1];
                var bid = Convert.ToDouble(data.data.bid.Split(' ')[0]);
                var ask = Convert.ToDouble(data.data.ask.Split(' ')[0]);
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= bid;
                    order.Ask -= ask;
                    if (order.Ask <= 0 || order.Bid <= 0)
                    {
                        db.DexBuyOrders.Remove(order);
                    }
                    await db.SaveChangesAsync();
                }
                db.MatchReceipts.Add(new MatchReceipt
                {
                    Ask = ask,
                    Bid = bid,
                    Asker = data.data.asker,
                    Bidder = data.data.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = data.data.unit_price / 100000000.0
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleBuyMatchAsync(KyubeyContext db, GetActionsResponseActionTraceAct data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.data.id);
                string token = data.data.ask.Split(' ')[1];
                var bid = Convert.ToDouble(data.data.bid.Split(' ')[0]);
                var ask = Convert.ToDouble(data.data.ask.Split(' ')[0]);
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= bid;
                    order.Ask -= ask;
                    if (order.Ask <= 0 || order.Bid <= 0)
                    {
                        db.DexSellOrders.Remove(order);
                    }
                    await db.SaveChangesAsync();
                }
                db.MatchReceipts.Add(new MatchReceipt
                {
                    Ask = ask,
                    Bid = bid,
                    Asker = data.data.asker,
                    Bidder = data.data.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = data.data.unit_price / 100000000.0
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task<IEnumerable<GetActionsResponseAction>> LookupDexActionAsync(IConfiguration config, KyubeyContext db, ILogger logger, NodeApiInvoker nodeApiInvoker)
        {
            try
            {
                var row = await db.Constants.SingleAsync(x => x.Id == "action_pos");
                var position = Convert.ToInt32(row.Value);

                var ret = await nodeApiInvoker.GetActionsAsync("kyubeydex.bp", position);
                return ret.actions;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                return null;
            }
        }

        private static HttpClient _client = new HttpClient { BaseAddress = new Uri("https://kyubey.net") };

        private async Task<GetTokenResultContract> GetTokenContractsAsync(string symbol)
        {
            using (var response = await _client.GetAsync("/api/v1/lang/en/token/" + symbol))
            {
                var ret = await response.Content.ReadAsAsync<ApiResult<GetTokenResult>>();
                return ret.Data.Contract;
            }
        }
    }
}
