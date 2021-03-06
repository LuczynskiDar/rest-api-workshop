using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Songify.Simple.DAL;
using Songify.Simple.Helpers;

namespace Songify.Simple
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // identity server, obecnie darmowy ale bedzie platny
            // jest tam implementacja: open id connect, 
            // uwiezytelnienie jest token jwt, w zewnetrznym serwerze
            
            // aplikacja na uaktualnianie schema
            // fluentmigrator i dbup do rozdzielenia migracji od startu aplikacji
            
            services.AddSingleton<InMemoryRepository>();
            services.AddAutoMapper(typeof(Startup));
            //scrutor
            services.Scan(scan => scan.FromAssemblyOf<Startup>()
                .AddClasses(@class => @class.AssignableTo<IRepository>())
                .AsImplementedInterfaces());
            services.AddDbContext<SongifyDbContext>(options =>
            {
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"), 
                    conf =>
                    {
                        // konfigurujemy gdzie przechowujemy migracji
                        conf.MigrationsAssembly(typeof(Startup).Assembly.FullName);
                    });
            });
            
            // services.AddControllers();
            services.AddControllers(options =>
            {
                options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));
            })
                .AddNewtonsoftJson(x =>
                {
                    x.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    x.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    x.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    x.SerializerSettings.Converters.Add(new StringEnumConverter());               
                }
            )
                .ConfigureApiBehaviorOptions(setupAction =>
                {
                    setupAction.InvalidModelStateResponseFactory = context =>
                    {
                        var problemDetailsFactory = context.HttpContext.RequestServices
                            .GetRequiredService<ProblemDetailsFactory>();
                        var problemDetails = problemDetailsFactory
                            .CreateValidationProblemDetails(context.HttpContext, context.ModelState);
                        problemDetails.Detail = "Se the errors field for details";
                        problemDetails.Instance = context.HttpContext.Request.Path;
                        var actionExecutingContext = context as ActionExecutingContext;
                        if (
                            (context.ModelState.ErrorCount > 0) &&
                            (actionExecutingContext?.ActionArguments.Count() == context.ActionDescriptor.Parameters.Count)
                        )
                        {
                            // https://datatracker.ietf.org/doc/html/rfc7807
                            problemDetails.Type = "https://songify.com/modelvalidationproblem";
                            problemDetails.Status = StatusCodes.Status422UnprocessableEntity;
                            problemDetails.Title = "One or more validation occured.";

                            return new UnprocessableEntityObjectResult(problemDetails)
                            {
                                ContentTypes = {"application/problem+json"}
                            };
                        }

                        problemDetails.Status = StatusCodes.Status400BadRequest;
                        problemDetails.Title = "One or more errors on input occured.";
                        return new BadRequestObjectResult(problemDetails)
                        {
                            ContentTypes = {"application/problem+json"}
                        };
                    };
                });
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    // Title = "Songify.Simple", 
                    // Version = "v1"
                    Title = "Songify.Api", 
                    Version = "v1",
                    Description = "Dokumentacja api Songify",
                    TermsOfService = new Uri("https://songify.com/termsofservice.pdf", UriKind.Absolute),
                    Contact = new OpenApiContact
                    {
                        Name = "Songify Support",
                        Email = "rayzki@gmail.com",
                        Url = new Uri("https://example.com", UriKind.Absolute)
                    }
                });
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Songify.Simple v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
