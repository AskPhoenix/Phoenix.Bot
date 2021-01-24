using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Dialogs.Student;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.Bot.Dialogs.Teacher;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Phoenix.DataHandle.Main;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Repositories;
using Phoenix.Bot.Dialogs.Parent;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Actions;

namespace Phoenix.Bot.Dialogs.Common
{
    public class MainDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;
        private readonly IStatePropertyAccessor<MainState> mainStateAccesor;
        private readonly IStatePropertyAccessor<ConversationData> conversationDataAccessor;
        private readonly BotState conversationState;

        private readonly AspNetUserRepository userRepository;
        private readonly Repository<AspNetRoles> roleRepository;

        public MainDialog(PhoenixContext phoenixContext, UserState userState, ConversationState conversationState,
            IntroductionDialog introductionDialog, HelpDialog welcomeDialog,
            StudentDialog studentDialog, TeacherDialog teacherDialog, ParentDialog parentDialog)
            : base(nameof(MainDialog))
        {
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));
            this.mainStateAccesor = conversationState.CreateProperty<MainState>(nameof(MainState));
            this.conversationDataAccessor = conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            this.conversationState = conversationState;

            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.roleRepository = new Repository<AspNetRoles>(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(introductionDialog);
            AddDialog(welcomeDialog);

            AddDialog(studentDialog);
            AddDialog(teacherDialog);
            AddDialog(parentDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Top,
                new WaterfallStep[]
                {
                    FirstTimeStepAsync,
                    RoleStepAsync,
                    RoleSelectStepAsync,
                    ForwardStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Main.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> FirstTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);

            if (!userData.IsAuthenticated)
            {
                LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
                string providerKey = stepContext.Context.Activity.From.Id;

                if (userRepository.AnyLogin(provider, providerKey, onlyActive: true))
                {
                    userData.IsAuthenticated = true;
                    await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
                }
                else
                    return await stepContext.BeginDialogAsync(nameof(IntroductionDialog), null, cancellationToken);
            }

            return await stepContext.NextAsync(true, cancellationToken);
        }

        private async Task<DialogTurnResult> RoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            if (mainState.RoleChecked)
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
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            if (mainState.RoleChecked)
                return await stepContext.NextAsync(null, cancellationToken);

            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            Role role = stepContext.Result is FoundChoice foundChoice
                ? (await roleRepository.Find(r => r.NormalizedName == foundChoice.Value)).Type
                : role = (Role)stepContext.Result;

            mainState.RoleChecked = true;
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);

            userData.Role = (int)role;
            await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
            
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            var conversationData = await conversationDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;
            var user = userRepository.FindUserFromLogin(provider, providerKey);
            if (user == null)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            //TODO: Generalize for all Roles
            var studentOptions = new StudentOptions() { StudentId = user.Id };
            if (conversationData.Command >= (Command)30)
                studentOptions.Action = (StudentAction)(conversationData.Command - 30 + 1);
            
            return (Role)userData.Role switch
            {
                Role.Student                    => await stepContext.BeginDialogAsync(nameof(StudentDialog), studentOptions, cancellationToken),
                Role.Parent                     => await stepContext.BeginDialogAsync(nameof(ParentDialog), null, cancellationToken),
                var r when r >= Role.Teacher    => await stepContext.BeginDialogAsync(nameof(TeacherDialog), null, cancellationToken),
                _                               => await stepContext.CancelAllDialogsAsync(cancellationToken)
            };
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

        #endregion
    }
}