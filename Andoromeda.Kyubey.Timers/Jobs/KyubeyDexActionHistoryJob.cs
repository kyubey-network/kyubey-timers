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
    public class KyubeyDexActionHistoryJob : Job
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
                        logger.LogInfo($"Handling action log pos={act.account_action_seq}, act={act.action_trace.act.name}");

                        switch (act.action_trace.act.name)
                        {
                            case "addfav":
                                await HandleAddFavAsync(db, act.action_trace.act.data.symbol, act.action_trace.act.authorization.First().actor, act.block_time, logger);
                                break;
                            case "removefav":
                                await HandleRemoveFavAsync(db, act.action_trace.act.data.symbol, act.action_trace.act.authorization.First().actor, act.block_time, logger);
                                break;
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
                            case "clean":
                                await HandleCleanAsync(db, act.action_trace.act.data, act.block_time, logger);
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

        private async Task HandleAddFavAsync(KyubeyContext db, string symbol, string account, DateTime time, ILogger logger)
        {
            try
            {
                if (await db.Favorites.SingleOrDefaultAsync(x => x.Account == account && x.TokenId == symbol) == null)
                {
                    db.Favorites.Add(new Favorite
                    {
                        Account = account,
                        TokenId = symbol
                    });
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleRemoveFavAsync(KyubeyContext db, string symbol, string account, DateTime time, ILogger logger)
        {
            try
            {
                var fav = await db.Favorites.SingleOrDefaultAsync(x => x.Account == account && x.TokenId == symbol);
                if (fav != null)
                {
                    db.Favorites.Remove(fav);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleCleanAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                string symbol = Convert.ToString(data.symbol);
                db.Remove(db.DexBuyOrders.Where(x => x.TokenId == symbol));
                await db.SaveChangesAsync();
                db.Remove(db.DexSellOrders.Where(x => x.TokenId == symbol));
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleCancelSellAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.id);
                string symbol = Convert.ToString(data.symbol);
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

        private async Task HandleCancelBuyAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.id);
                string symbol = Convert.ToString(data.symbol);
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

        private async Task HandleSellReceiptAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.t.id);
                string token = Convert.ToString(data.t.bid).Split(' ')[1];
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    db.DexSellOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexSellOrder
                {
                    Id = data.t.id,
                    Account = data.t.account,
                    Ask = Convert.ToDouble(Convert.ToString(data.t.ask).Split(' ')[0]),
                    Bid = Convert.ToDouble(Convert.ToString(data.t.bid).Split(' ')[0]),
                    UnitPrice = Convert.ToInt64(data.t.unit_price) / 100000000.0,
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

        private async Task HandleBuyReceiptAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.o.id);
                string token = Convert.ToString(data.o.ask).Split(' ')[1];
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    db.DexBuyOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexBuyOrder
                {
                    Id = data.o.id,
                    Account = data.o.account,
                    Ask = Convert.ToDouble(Convert.ToString(data.o.ask).Split(' ')[0]),
                    Bid = Convert.ToDouble(Convert.ToString(data.o.bid).Split(' ')[0]),
                    UnitPrice = Convert.ToInt64(data.o.unit_price) / 100000000.0,
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

        private async Task HandleSellMatchAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.t.id);
                string token = Convert.ToString(data.t.bid).Split(' ')[1];
                var bid = Convert.ToDouble(Convert.ToString(data.t.bid).Split(' ')[0]);
                var ask = Convert.ToDouble(Convert.ToString(data.t.ask).Split(' ')[0]);
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= ask;
                    order.Ask -= bid;
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
                    Asker = data.t.asker,
                    Bidder = data.t.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = Convert.ToInt64(data.t.unit_price) / 100000000.0,
                    IsSellMatch = true
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                throw;
            }
        }

        private async Task HandleBuyMatchAsync(KyubeyContext db, dynamic data, DateTime time, ILogger logger)
        {
            try
            {
                long orderId = Convert.ToInt64(data.t.id);
                string token = Convert.ToString(data.t.ask).Split(' ')[1];
                var bid = Convert.ToDouble(Convert.ToString(data.t.bid).Split(' ')[0]);
                var ask = Convert.ToDouble(Convert.ToString(data.t.ask).Split(' ')[0]);
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == orderId && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= ask;
                    order.Ask -= bid;
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
                    Asker = data.t.asker,
                    Bidder = data.t.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = Convert.ToInt64(data.t.unit_price) / 100000000.0,
                    IsSellMatch = false
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
                logger.LogInfo("Current kyubeydex.bp account seq is " + position);

                var ret = await nodeApiInvoker.GetActionsAsync("kyubeydex.bp", position);
                if (ret.actions.Count() == 0)
                {
                    logger.LogInfo("No new action in kyubeydex.bp");
                    return new GetActionsResponseAction[] { };
                }

                position += ret.actions.Count();
                row.Value = position.ToString();
                await db.SaveChangesAsync();
                logger.LogInfo($"{ret.actions.Count()} new actions found in kyubeydex.bp, position moved to " + position);

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
