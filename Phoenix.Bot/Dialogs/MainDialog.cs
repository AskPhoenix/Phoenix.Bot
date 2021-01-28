using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Repositories;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Miscellaneous;

namespace Phoenix.Bot.Dialogs
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
            IntroductionDialog introductionDialog, HomeDialog homeDialog)
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
            AddDialog(homeDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Top,
                new WaterfallStep[]
                {
                    IntroStepAsync,
                    RoleStepAsync,
                    RoleSelectStepAsync,
                    ForwardStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Main.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!userRepository.AnyLogin(stepContext.Context.Activity, onlyActive: true))
                return await stepContext.BeginDialogAsync(nameof(IntroductionDialog), null, cancellationToken);

            return await stepContext.NextAsync(true, cancellationToken);
        }

        private async Task<DialogTurnResult> RoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            if (mainState.RolesOverlapChecked)
                return await stepContext.NextAsync(null, cancellationToken);

            var user = userRepository.FindUserFromLogin(stepContext.Context.Activity);
            var userRoles = userRepository.FindRoles(user);

            if (userRoles.Count() == 1)
                return await stepContext.NextAsync(null, cancellationToken);

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
            if (mainState.RolesOverlapChecked)
                return await stepContext.NextAsync(null, cancellationToken);
            
            if (stepContext.Result is FoundChoice foundChoice)
            {
                var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
                userData.SelectedOverlappingRole = (await roleRepository.Find(r => r.NormalizedName == foundChoice.Value)).Type;
                await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
            }

            mainState.RolesOverlapChecked = true;
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);
            
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var user = userRepository.FindUserFromLogin(stepContext.Context.Activity);
            if (user == null)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            var conversationData = await conversationDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            var homeOptions = new HomeOptions() { UserId = user.Id, Action = BotAction.NoAction };
            
            if (conversationData.Command >= (Command)30)
                homeOptions.Action = (BotAction)(conversationData.Command - 30 + 1);

            return await stepContext.BeginDialogAsync(nameof(HomeDialog), homeOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, null, cancellationToken);
        //TODO: Check ActiveDialog's value

        #endregion
    }
}