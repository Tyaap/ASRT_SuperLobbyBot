using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SLB
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync(Web.message);
                });

                endpoints.MapGet("/{response:minlength(1)}", context => ProcessResponse(context));
            });
        }

        public Task ProcessResponse(HttpContext context)
        {
            if (Web.waitingForResponse)
            {
                Console.WriteLine("Processing web response...");
                Web.response = context.Request.RouteValues["response"].ToString();
                Web.waitHandle.Set(); // Unblock the thread that was asking for user input
                Web.message = "Input recieved! Press F5 to check for new messages.";
                context.Response.Redirect("/");
                Web.waitingForResponse = false;
            }
            return Task.CompletedTask;
        }
    }
}
