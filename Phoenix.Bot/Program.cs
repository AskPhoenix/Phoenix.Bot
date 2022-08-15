using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Adapters;
using Phoenix.Bot.Bots;
using Phoenix.Bot.Dialogs;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Dialogs.Auth;
using Phoenix.DataHandle.Senders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container

builder.Services
    .AddHttpClient()
    .AddControllers()
    .AddNewtonsoftJson();

#region DB Contexts

Action<DbContextOptionsBuilder> buildDbContextOptions = o => o
    .UseLazyLoadingProxies()
    .UseSqlServer(builder.Configuration.GetConnectionString("PhoenixConnection"));

builder.Services.AddDbContext<ApplicationContext>(buildDbContextOptions);
builder.Services.AddDbContext<PhoenixContext>(buildDbContextOptions);
builder.Services.AddDbContext<BotDataContext>(buildDbContextOptions);

#endregion

# region Identity

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
    .AddRoles<ApplicationRole>()
    .AddUserStore<ApplicationStore>()
    .AddUserManager<ApplicationUserManager>()
    .AddEntityFrameworkStores<ApplicationContext>()
    .AddDefaultTokenProviders();

#endregion

#region Bot Services

builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

builder.Services.AddTransient<IBot, DialogBot<MainDialog>>();

// Bot State Stores
builder.Services.AddSingleton<IStorage>(new EntityFrameworkStorage(
    builder.Configuration.GetConnectionString("PhoenixConnection")));
builder.Services.AddSingleton(new EntityFrameworkTranscriptStore(
    builder.Configuration.GetConnectionString("PhoenixConnection")));

// Bot State
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
builder.Services.AddScoped<VerificationDialog>();

// Preparation
builder.Services.AddScoped<PreparationDialog>();
builder.Services.AddScoped<AffiliatedUserPreparation>();
builder.Services.AddScoped<CoursePreparation>();
builder.Services.AddScoped<GroupPreparation>();
builder.Services.AddScoped<DatePreparation>();
builder.Services.AddScoped<LecturePreparation>();

// Actions
builder.Services.AddScoped<AssignmentsDialog>();
builder.Services.AddScoped<ScheduleDialog>();
builder.Services.AddScoped<GradesDialog>();
builder.Services.AddScoped<AccessDialog>();
builder.Services.AddScoped<TeacherExtensionDialog>();
builder.Services.AddScoped<BroadcastDialog>();
builder.Services.AddScoped<HelpDialog>();
builder.Services.AddScoped<FeedbackDialog>();

#endregion

# region Miscellaneous Services

builder.Services.AddApplicationInsightsTelemetry(
    o => o.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]);

builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

builder.Services.AddScoped(_ =>
    new SmsSender(builder.Configuration["Vonage:Key"], builder.Configuration["Vonage:Secret"]));

#endregion


var app = builder.Build();

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
