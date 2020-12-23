using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Dialogs.Student;
using System;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.Bot.Dialogs.Teacher;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Phoenix.DataHandle.Main;
using Microsoft.EntityFrameworkCore;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Repositories;
using Phoenix.Bot.Dialogs.Common.Authentication;
using Phoenix.Bot.Utilities.State.Dialogs;

namespace Phoenix.Bot.Dialogs.Common
{
    public class MainDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly PhoenixContext phoenixContext;

        private readonly AspNetUserRepository userRepository;
        private readonly Repository<AspNetRoles> roleReposotory;

        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;
        private readonly IStatePropertyAccessor<ConversationsOptions> convOptionsAccesor;

        public MainDialog(
            IConfiguration configuration,
            ConversationState conversationState,
            UserState userState,
            PhoenixContext phoenixContext,

            CredentialsDialog authDialog,
            WelcomeDialog welcomeDialog,
            FeedbackDialog feedbackDialog,
            StudentDialog studentDialog,
            TeacherDialog teacherDialog) 
            : base(nameof(MainDialog))
        {
            this.configuration = configuration;
            this.phoenixContext = phoenixContext;

            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.roleReposotory = new Repository<AspNetRoles>(phoenixContext);

            this.userOptionsAccesor = userState.CreateProperty<UserOptions>("Options");
            this.convOptionsAccesor = conversationState.CreateProperty<ConversationsOptions>("Options");

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(authDialog);
            AddDialog(welcomeDialog);
            AddDialog(feedbackDialog);
            AddDialog(studentDialog);
            AddDialog(teacherDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Top,
                new WaterfallStep[]
                {
                    FirstTimeStepAsync,
                    UserRegisterStepAsync,
                    CommandHandleStepAsync,
                    GreetingStepAsync,
                    MultiRoleStepAsync,
                    MultiRoleSelectStepAsync,
                    ForwardStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Main.Top;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            options ??= new MainOptions();

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Main Waterfall Dialog

        private async Task<DialogTurnResult> FirstTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);

            if (userOptions.IsAuthenticated)
            {
                LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
                string providerKey = stepContext.Context.Activity.From.Id;

                if (!userRepository.AnyLogin(provider, providerKey))
                {
                    userOptions.IsAuthenticated = false;
                    await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
                }
            }

            if (!userOptions.IsAuthenticated)
                return await stepContext.BeginDialogAsync(nameof(CredentialsDialog), null, cancellationToken);

            return await stepContext.NextAsync(true, cancellationToken);
        }

        private async Task<DialogTurnResult> UserRegisterStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!(stepContext.Result is bool) || !(bool)stepContext.Result)
                return await stepContext.CancelAllDialogsAsync(cancellationToken);

            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            if (userOptions.IsAuthenticated)
                return await stepContext.NextAsync(null, cancellationToken);
            if (!userOptions.HasAcceptedTerms)
                return await stepContext.CancelAllDialogsAsync(cancellationToken);
            userOptions.IsAuthenticated = true;

            var convOptions = await convOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            string phone = convOptions.Authentication?.PhoneNumber;
            string code = convOptions.Authentication?.OTC;
            convOptions.Authentication = null;

            //TODO: Needs revision
            //TODO: Remove OneTimeCode related columns from DB
            //This is for the students and their parents who have registered with the same phone number
            string schoolFbId = stepContext.Context.Activity.Recipient.Id;
            var user = phoenixContext.UserSchool.
                Include(us => us.AspNetUser).
                Include(us => us.AspNetUser.AspNetUserLogins).
                SingleOrDefault(us => us.AspNetUser.PhoneNumber == phone && us.School.FacebookPageId == schoolFbId && us.AspNetUser.AspNetUserLogins.Any(l => l.OneTimeCode == code && l.UserId == us.AspNetUserId)).
                AspNetUser;

            user.User.TermsAccepted = true;
            await phoenixContext.SaveChangesAsync();

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;
            if (!userRepository.AnyLogin(provider, providerKey))
            {
                userRepository.LinkLogin(new AspNetUserLogins()
                {
                    LoginProvider = provider.GetProviderName(),
                    ProviderKey = providerKey,
                    UserId = user.Id
                });
            }

            await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
            await convOptionsAccesor.SetAsync(stepContext.Context, convOptions, cancellationToken);

            //await conversationState.SaveChangesAsync(stepContext.Context);
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> CommandHandleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!Persistent.TryGetCommand(stepContext.Context.Activity.Text, out Persistent.Command cmd))
                return await stepContext.NextAsync(null, cancellationToken);

            switch (cmd)
            {
                case Persistent.Command.GetStarted:
                case Persistent.Command.Tutorial:
                    (stepContext.Options as MainOptions).UserWelcomed = true;
                    return await stepContext.BeginDialogAsync(nameof(WelcomeDialog), null, cancellationToken);
                case Persistent.Command.Feedback:
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), null, cancellationToken);
                default:
                    return await stepContext.NextAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> GreetingStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string mess = stepContext.Context.Activity.Text;
            if (!mess.ContainsSynonyms(Synonyms.Topics.Greetings))
                return await stepContext.NextAsync(null, cancellationToken);

            var reply = MessageFactory.ContentUrl(
                url: await DialogsHelper.CreateGifUrlAsync("g", "hi", 10, new Random().Next(10), configuration["GiphyKey"]),
                contentType: "image/gif");
            await stepContext.Context.SendActivityAsync(reply);

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;
            var user = userRepository.FindUserFromLogin(provider, providerKey);
            
            reply = MessageFactory.Text($"Γεια σου {Greek.NameVocative(user.User.FirstName)}! 😊");
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> MultiRoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((stepContext.Options as MainOptions).RoleChecked)
                return await stepContext.NextAsync(null);

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;
            
            var user = userRepository.FindUserFromLogin(provider, providerKey);
            var userRoles = userRepository.FindRoles(user);

            // If user has 1 role, then don't ask
            if (userRoles.Count() == 1)
                return await stepContext.NextAsync(userRoles.First().Type);
            // If user has multiple non-contradictious roles (e.g. Teacher, Owner), then don't ask and select the hierarchly highest one
            if (userRoles.All(r => r.Type >= Role.Teacher))
                return await stepContext.NextAsync(userRoles.Max(r => r.Type));
            // If user has multiple roles and the include Student or Teacher, meaning they are contradictious, then ask which one they prefer

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνδεθείς ως:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε έναν από τους παρακάτω ρόλους:"),
                    Choices = ChoiceFactory.ToChoices(userRoles.Select(r => r.NormalizedName).ToList())
                });
        }

        private async Task<DialogTurnResult> MultiRoleSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!(stepContext.Options as MainOptions).RoleChecked)
            {
                (stepContext.Options as MainOptions).RoleChecked = true;

                var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);

                if (stepContext.Result is FoundChoice foundChoice)
                {
                    var roleSel = (await roleReposotory.Find(r => r.NormalizedName == foundChoice.Value)).Type;
                    userOptions.Role = (int)roleSel;
                }
                else
                    userOptions.Role = (int)stepContext.Result;

                await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
                //await userState.SaveChangesAsync(stepContext.Context);
            }

            if (!(stepContext.Options as MainOptions).UserWelcomed)
            {
                (stepContext.Options as MainOptions).UserWelcomed = true;
                return await stepContext.BeginDialogAsync(nameof(WelcomeDialog), null, cancellationToken);
            }
            
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            
            return (Role)userOptions.Role switch
            {
                Role.Student => await stepContext.BeginDialogAsync(nameof(StudentDialog), null, cancellationToken),
                var r when r >= Role.Teacher => await stepContext.BeginDialogAsync(nameof(TeacherDialog), null, cancellationToken),
                _ => await stepContext.CancelAllDialogsAsync(cancellationToken)
            };
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

        #endregion
    }
}
