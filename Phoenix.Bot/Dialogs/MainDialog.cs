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
using Phoenix.DataHandle.Main;
using System;

namespace Phoenix.Bot.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<MainState> mainStateAccesor;
        private readonly IStatePropertyAccessor<ConversationData> conversationDataAccessor;
        private readonly BotState conversationState;

        private readonly AspNetUserRepository userRepository;

        public MainDialog(PhoenixContext phoenixContext, ConversationState conversationState,
            IntroductionDialog introductionDialog, HomeDialog homeDialog)
            : base(nameof(MainDialog))
        {
            this.mainStateAccesor = conversationState.CreateProperty<MainState>(nameof(MainState));
            this.conversationDataAccessor = conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            this.conversationState = conversationState;

            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(introductionDialog);
            AddDialog(homeDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Top,
                new WaterfallStep[]
                {
                    IntroStepAsync,
                    PostIntroStepAsync,
                    ForwardStepAsync,
                    LoopStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Main.Role,
                new WaterfallStep[]
                {
                    CheckRoleAsync,
                    AskRoleStepAsync,
                    SelectRoleStepAsync
                }));

            InitialDialogId = WaterfallNames.Main.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            if (!userRepository.HasLogin(provider, providerKey, onlyActive: true))
                return await stepContext.BeginDialogAsync(nameof(IntroductionDialog), null, cancellationToken);

            return await stepContext.NextAsync(true, cancellationToken);
        }

        private async Task<DialogTurnResult> PostIntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (((bool)stepContext.Result) is not true)
                return await this.LoopStepAsync(stepContext, cancellationToken);

            return await stepContext.BeginDialogAsync(WaterfallNames.Main.Role, null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            Role role = (Role)stepContext.Result;
            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            var user = userRepository.FindUserFromLogin(provider, providerKey);
            if (user == null)
            {
                await stepContext.Context.SendActivityAsync("Δυστυχώς υπήρξε ένα πρόβλημα. Παρακαλώ προσπαθήστε ξανά αργότερα " +
                    "ή επικοινωνήστε με το φροντιστήριό σας.");
                return await this.LoopStepAsync(stepContext, cancellationToken);
            }
            
            if (role == Role.Parent && !userRepository.AnyAffiliatedUsers(user.Id))
            {
                await stepContext.Context.SendActivityAsync("Δε βρέθηκαν χρήστες συσχετισμένοι με αυτόν τον λογαριασμό.");
                await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινωνήστε με το φροντιστήριό σας για την επίλυση του προβλήματος.");
                return await this.LoopStepAsync(stepContext, cancellationToken);
            }

            var conversationData = await conversationDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            var homeOptions = new HomeOptions(user.Id, role) { Action = BotAction.NoAction };
            
            if ((int)conversationData.Command >= CommandAttributes.ActionCommandsBase)
                homeOptions.Action = (BotAction)(conversationData.Command - CommandAttributes.ActionCommandsBase + 1);

            return await stepContext.BeginDialogAsync(nameof(HomeDialog), homeOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, null, cancellationToken);
        }

        #endregion

        #region Role Waterfall Dialog

        private async Task<DialogTurnResult> CheckRoleAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            AspNetUsers user;
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);

            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            if (mainState.RoleChecked)
            {
                if (mainState.HasMultipleRoles)
                    return await stepContext.EndDialogAsync(mainState.SelectedRole, cancellationToken);

                user = userRepository.FindUserFromLogin(provider, providerKey);
                return await stepContext.EndDialogAsync(userRepository.FindRoles(user).Single().Type, cancellationToken);
            }

            user = userRepository.FindUserFromLogin(provider, providerKey);
            var userRoles = userRepository.FindRoles(user).Select(r => r.Type).ToArray();
            Role r;

            if (userRoles.Length == 1 && !(r = userRoles.Single()).IsBackend())
            {
                mainState.RoleChecked = true;
                await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);
                return await stepContext.EndDialogAsync(r, cancellationToken);
            }

            bool isStaffOrBackend = userRoles.Any(r => r.IsStaff() || r.IsBackend());
            bool invalidMultipleRoles = userRoles.Length > 2 || (userRoles.Length == 2 && (!userRoles.Contains(Role.Parent) || !isStaffOrBackend));
            if (invalidMultipleRoles)
            {
                await stepContext.Context.SendActivityAsync("Δυστυχώς έχει γίνει κάποιο λάθος με την ιδιότητά σου στο φροντιστήριο.");
                await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινώνησε με το φροντιστήριο για να διορθωθεί το πρόβλημα");
                return await this.LoopStepAsync(stepContext, cancellationToken);
            }

            mainState.HasMultipleRoles = true;
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);
            return await stepContext.NextAsync(userRoles, cancellationToken);
        }

        private async Task<DialogTurnResult> AskRoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userRoles = (Role[])stepContext.Result;
            
            //TODO: Distinguish Role.SuperAdmin's behavior
            if (userRoles.Any(r => r.IsBackend()))
                userRoles = new Role[] { Role.Student, Role.Parent, Role.Teacher };

            stepContext.Values.Add("translatedRoles", userRoles);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνδεθείς ως:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε έναν από τους παρακάτω ρόλους:"),
                    Choices = ChoiceFactory.ToChoices(userRoles.Select(r => r.ToNormalizedString()).ToArray())
                });
        }

        private async Task<DialogTurnResult> SelectRoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            Role selRole = ((Role[])stepContext.Values["translatedRoles"]).ElementAt(foundChoice.Index);

            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            mainState.SelectedRole = selRole;
            mainState.RoleChecked = true;
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);

            return await stepContext.EndDialogAsync(selRole, cancellationToken);
        }

        #endregion
    }
}