using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Andoromeda.Kyubey.Models;

namespace Andoromeda.Kyubey.Timers
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddConfiguration2(out var config);
            services.AddEosNodeApiInvoker(config["NodeAddress"]);
            services.AddMySqlLogger("kyubey-timers", config["DexBosMySql"]);
            services.AddTimedJob();
            services.AddEntityFrameworkMySql()
                .AddDbContext<KyubeyContext>(x =>
                {
                    x.UseMySql(config["DexBosMySql"]);
                });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.Run(async (context) =>
            {
                await context.Response.WriteAsync("Kyubey Timers are running...");
            });

            using (var serviceScope = app.ApplicationServices.CreateScope())
            {
                serviceScope.ServiceProvider.GetRequiredService<KyubeyContext>().Database.EnsureCreated();
                app.UseTimedJob();
            }
        }
    }
}
