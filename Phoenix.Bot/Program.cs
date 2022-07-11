using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phoenix.Bot.Bots;
using Phoenix.Bot.Dialogs;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Dialogs.Authentication;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Sms;
using System;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Configure Web Host Defaults
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Add services to the container

builder.Services
    .AddHttpClient()
    .AddControllers();

#region DB Contexts

Action<DbContextOptionsBuilder> buildDbContextOptions = o => o
    .UseLazyLoadingProxies()
    .UseSqlServer(builder.Configuration.GetConnectionString("PhoenixConnection"));

builder.Services.AddDbContext<ApplicationContext>(buildDbContextOptions);
builder.Services.AddDbContext<PhoenixContext>(buildDbContextOptions);
builder.Services.AddDbContext<BotDataContext>(buildDbContextOptions);

#endregion

# region Identity

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
{
    o.User.AllowedUserNameCharacters = null;
    o.Password.RequireDigit = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = false;
    o.Password.RequiredLength = 6;
})
    .AddRoles<ApplicationRole>()
    .AddUserStore<ApplicationStore>()
    .AddUserManager<ApplicationUserManager>()
    .AddEntityFrameworkStores<ApplicationContext>()
    .AddDefaultTokenProviders();

#endregion

#region Bot State Services

builder.Services.AddSingleton<UserState>();
builder.Services.AddSingleton<ConversationState>();

#endregion

#region Bot Dialog Services

// Root
builder.Services.AddScoped<MainDialog>();
builder.Services.AddScoped<IntroductionDialog>();
builder.Services.AddScoped<HomeDialog>();

// Authentication
builder.Services.AddScoped<AuthenticationDialog>();
builder.Services.AddScoped<CredentialsDialog>();
builder.Services.AddScoped<VerificationDialog>();

// Preparation
builder.Services.AddScoped<PreparationDialog>();
builder.Services.AddScoped<AffiliatedUserPreparationComponent>();
builder.Services.AddScoped<CoursePreparationComponent>();
builder.Services.AddScoped<GroupPreparationComponent>();
builder.Services.AddScoped<DatePreparationComponent>();
builder.Services.AddScoped<LecturePreparationComponent>();

// Actions
builder.Services.AddScoped<AssignmentsDialog>();
builder.Services.AddScoped<ScheduleDialog>();
builder.Services.AddScoped<GradesDialog>();
builder.Services.AddScoped<AccessDialog>();
builder.Services.AddScoped<AssignmentsManagementDialog>();
builder.Services.AddScoped<BroadcastDialog>();
builder.Services.AddScoped<HelpDialog>();
builder.Services.AddScoped<FeedbackDialog>();

#endregion

#region Bot Services

builder.Services.AddTransient<IBot, DialogBot<MainDialog>>();

#endregion

# region Miscellaneous Services

builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

builder.Services.AddApplicationInsightsTelemetry(builder.Configuration["ApplicationInsights:ConnectionString"]);

builder.Services.AddScoped<ISmsService>(_ =>
    new SmsService(builder.Configuration["NexmoSMS:ApiKey"], builder.Configuration["NexmoSMS:ApiSecret"]));

#endregion


var app = builder.Build();

// TODO: Load culture from School Settings
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("el-GR");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseHsts();

app.UseDefaultFiles()
    .UseStaticFiles()
    .UseWebSockets()
    .UseRouting()
    .UseAuthorization()
    .UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
    })
    .UseHttpsRedirection();

app.Run();
