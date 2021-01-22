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
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Repositories;
using Phoenix.Bot.Utilities.State.DialogOptions;
using Phoenix.Bot.Dialogs.Parent;

namespace Phoenix.Bot.Dialogs.Common
{
    public class MainDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;

        private readonly AspNetUserRepository userRepository;
        private readonly Repository<AspNetRoles> roleRepository;

        public MainDialog(IConfiguration configuration, PhoenixContext phoenixContext, UserState userState,
            IntroductionDialog introductionDialog, StudentDialog studentDialog, TeacherDialog teacherDialog, ParentDialog parentDialog)
            : base(nameof(MainDialog))
        {
            this.configuration = configuration;
            this.userOptionsAccesor = userState.CreateProperty<UserOptions>(UserDefaults.PropertyName);

            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.roleRepository = new Repository<AspNetRoles>(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(introductionDialog);
            AddDialog(studentDialog);
            AddDialog(teacherDialog);
            AddDialog(parentDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Top,
                new WaterfallStep[]
                {
                    FirstTimeStepAsync,
                    CommandHandleStepAsync,
                    GreetingStepAsync,
                    RoleStepAsync,
                    RoleSelectStepAsync,
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

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> FirstTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);

            if (!userOptions.IsAuthenticated)
            {
                LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
                string providerKey = stepContext.Context.Activity.From.Id;

                if (userRepository.AnyLogin(provider, providerKey, onlyActive: true))
                {
                    userOptions.IsAuthenticated = true;
                    await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
                }
                else
                    return await stepContext.BeginDialogAsync(nameof(IntroductionDialog), null, cancellationToken);
            }

            return await stepContext.NextAsync(true, cancellationToken);
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

        private async Task<DialogTurnResult> RoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((stepContext.Options as MainOptions).RoleChecked)
                return await stepContext.NextAsync(null, cancellationToken);

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;
            
            var user = userRepository.FindUserFromLogin(provider, providerKey);
            var userRoles = userRepository.FindRoles(user);

            if (userRoles.Count() == 1)
                return await stepContext.NextAsync(userRoles.Single().Type, cancellationToken);
            if (userRoles.All(r => r.Type >= Role.Teacher))
                return await stepContext.NextAsync(userRoles.Max(r => r.Type), cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνδεθείς ως:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε έναν από τους παρακάτω ρόλους:"),
                    Choices = ChoiceFactory.ToChoices(userRoles.Select(r => r.NormalizedName).ToList())
                });
        }

        private async Task<DialogTurnResult> RoleSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((stepContext.Options as MainOptions).RoleChecked)
                return await stepContext.NextAsync(null, cancellationToken);

            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            Role role = stepContext.Result is FoundChoice foundChoice
                ? (await roleRepository.Find(r => r.NormalizedName == foundChoice.Value)).Type
                : role = (Role)stepContext.Result;

            (stepContext.Options as MainOptions).RoleChecked = true;
            userOptions.Role = (int)role;
            await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
            
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            
            return (Role)userOptions.Role switch
            {
                Role.Student => await stepContext.BeginDialogAsync(nameof(StudentDialog), null, cancellationToken),
                Role.Parent => await stepContext.BeginDialogAsync(nameof(ParentDialog), null, cancellationToken),
                var r when r >= Role.Teacher => await stepContext.BeginDialogAsync(nameof(TeacherDialog), null, cancellationToken),
                _ => await stepContext.CancelAllDialogsAsync(cancellationToken)
            };
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

        #endregion
    }
}
