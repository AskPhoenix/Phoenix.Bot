using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phoenix.Bot.Adapters;
using Phoenix.Bot.Bots;
using Phoenix.Bot.Dialogs;
using Phoenix.Bot.Dialogs.Authentication;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using System.Globalization;
using Phoenix.Bot.Dialogs.Actions.Preparation;

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
            services.AddScoped<IntroductionDialog>();
            services.AddScoped<HomeDialog>();
            
            services.AddScoped<AuthDialog>();
            services.AddScoped<CredentialsDialog>();
            services.AddScoped<VerificationDialog>();

            services.AddScoped<PreparationDialog>();
            services.AddScoped<AffiliatedUserPreparationComponent>();
            services.AddScoped<CoursePreparationComponent>();
            services.AddScoped<GroupPreparationComponent>();
            services.AddScoped<DatePreparationComponent>();
            services.AddScoped<LecturePreparationComponent>();

            services.AddScoped<AssignmentsDialog>();
            services.AddScoped<ScheduleDialog>();
            services.AddScoped<GradesDialog>();
            services.AddScoped<AccessDialog>();

            services.AddScoped<HelpDialog>();
            services.AddScoped<FeedbackDialog>();
            services.AddScoped<ExerciseDialog>();
            services.AddScoped<ExamDialog>();

            services.AddTransient<IBot, DialogBot<MainDialog>>();

            services.AddApplicationInsightsTelemetry();
            services.AddControllers();
            services.AddHttpsRedirection(options => options.HttpsPort = 443);

            services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
                options.User.AllowedUserNameCharacters = null;
            }).AddUserStore<ApplicationStore>().AddUserManager<UserManager<ApplicationUser>>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(Configuration.GetConnectionString("PhoenixConnection")));
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
