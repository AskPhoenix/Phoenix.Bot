﻿using Microsoft.Bot.Builder;
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

        //TODO: Use Enum for errors
        private async Task<DialogTurnResult> ExitAsync(string message, string solution, int error, WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync($"{message} (Κωδ. σφάλματος: {error.ToString("D4"):0})");

            if (!string.IsNullOrEmpty(solution))
                await stepContext.Context.SendActivityAsync(solution);
            else
                await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινώνησε με το φροντιστήριο για την επίλυση του προβλήματος.");

            return await stepContext.CancelAllDialogsAsync(cancellationToken);
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

            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            if (mainState.RolesChecked)
                return await stepContext.NextAsync(cancellationToken: cancellationToken);

            return await stepContext.BeginDialogAsync(WaterfallNames.Main.Role, null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);

            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            var user = userRepository.FindUserFromLogin(provider, providerKey);
            if (user == null)
            {
                string errorMessage = "Δε βρέθηκε χρήστης με το λογαρισμό από τον οποίο προσπαθείς να συνδεθείς.";
                string solutionsMessage = "Γράψε την εντολή \"Logout\" και προσπάθησε ξανά.";
                
                return await this.ExitAsync(errorMessage, solutionsMessage, error: 1, stepContext, cancellationToken);
            }
            
            if (mainState.SelectedRoles.Contains(Role.Parent) && !userRepository.AnyAffiliatedUsers(user.Id))
            {
                string errorMessage = "Δε βρέθηκαν χρήστες συσχετισμένοι με αυτόν τον λογαριασμό.";

                return await this.ExitAsync(errorMessage, solution: null, error: 2, stepContext, cancellationToken);
            }

            var conversationData = await conversationDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            var homeOptions = new HomeOptions(user.Id, mainState.SelectedRoles) { Action = BotAction.NoAction };
            
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
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);

            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            AspNetUsers user = userRepository.FindUserFromLogin(provider, providerKey);
            var userRoles = userRepository.FindRoles(user).Select(r => r.Type).ToArray();

            bool isStaffOrBackend = userRoles.Any(r => r.IsStaff() || r.IsBackend());
            bool invalidMultipleRoles = userRoles.Length > 2 
                || (userRoles.Length == 2 && 
                (!(userRoles.Contains(Role.Parent) || userRoles.Contains(Role.Teacher)) || !isStaffOrBackend));

            if (invalidMultipleRoles || !userRoles.Any())
            {
                string errorMessage = "Δυστυχώς έχει γίνει κάποιο λάθος με την ιδιότητά σου στο φροντιστήριο.";

                return await this.ExitAsync(errorMessage, solution: null, error: 3, stepContext, cancellationToken);
            }

            if (userRoles.Any(r => r.IsBackend()) || (userRoles.Contains(Role.Parent) && userRoles.Any(r => r.IsStaff())))
                return await stepContext.NextAsync(userRoles, cancellationToken);

            mainState = new MainState
            {
                RolesChecked = true,
                SelectedRoles = userRoles
            };
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);

            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> AskRoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userRoles = (Role[])stepContext.Result;
            
            //TODO: Distinguish Role.SuperAdmin's behavior
            if (userRoles.Any(r => r.IsBackend()))
                userRoles = new Role[] { Role.Student, Role.Parent, Role.Teacher, Role.Secretary, Role.SchoolAdmin };

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

            var mainState = new MainState
            {
                RolesChecked = true,
                SelectedRoles = new Role[] { selRole }
            };
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);

            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }

        #endregion
    }
}