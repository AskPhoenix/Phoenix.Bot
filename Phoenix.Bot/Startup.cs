using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phoenix.Bot.Adapters;
using Phoenix.Bot.Bots;
using Phoenix.Bot.Dialogs;
using Phoenix.Bot.Dialogs.Student;
using Phoenix.Bot.Dialogs.Teacher;
using Phoenix.DataHandle.Main.Models;
using System.Globalization;

namespace Phoenix.Bot
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            services.AddSingleton<IStorage>(new EntityFrameworkStorage(Configuration.GetConnectionString("PhoenixConnection")));
            services.AddSingleton(new EntityFrameworkTranscriptStore(Configuration.GetConnectionString("PhoenixConnection")));

            services.AddSingleton<UserState>();
            services.AddSingleton<ConversationState>();

            services.AddScoped<MainDialog>();
            services.AddScoped<AuthDialog>();
            services.AddScoped<WelcomeDialog>();
            services.AddScoped<FeedbackDialog>();

            services.AddScoped<StudentDialog>();
            services.AddScoped<ExerciseDialog>();
            services.AddScoped<ExamDialog>();
            services.AddScoped<ScheduleDialog>();

            services.AddScoped<TeacherDialog>();

            services.AddTransient<IBot, DialogBot<MainDialog>>();

            services.AddApplicationInsightsTelemetry();
            services.AddControllers();
            services.AddHttpsRedirection(options => options.HttpsPort = 443);

            services.AddDbContext<PhoenixContext>(options => options.UseLazyLoadingProxies().UseSqlServer(Configuration.GetConnectionString("PhoenixConnection")));
        }

        public static void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
                app.UseHsts();

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseWebSockets();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("el-GR");
        }
    }
}
