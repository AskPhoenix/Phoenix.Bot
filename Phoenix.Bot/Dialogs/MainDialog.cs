using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Linguistic;

namespace Phoenix.Bot.Dialogs
{
    public class MainDialog : StateDialog
    {
        private readonly UserConnectionRepository _userConnectionRepository;
        private readonly SchoolConnectionRepository _schoolConnectionRepository;

        public MainDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,
            IntroductionDialog introductionDialog,
            HomeDialog homeDialog)
            : base(userState, convState, userManager, phoenixContext, nameof(MainDialog))
        {
            _userConnectionRepository = new(phoenixContext);
            _schoolConnectionRepository = new(phoenixContext);

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

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (!UData.IsConnected)
                return await stepCtx.BeginDialogAsync(nameof(IntroductionDialog), null, canTkn);

            return await stepCtx.NextAsync(true, canTkn);
        }

        private async Task<DialogTurnResult> PostIntroStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (!(bool)stepCtx.Result)
                return await LoopStepAsync(stepCtx, canTkn);

            if (UData.SelectedRole.HasValue)
                return await stepCtx.NextAsync(null, canTkn);

            return await stepCtx.BeginDialogAsync(WaterfallNames.Main.Role, null, canTkn);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            // TODO: Check that lazy loader works here
            if (UData.SelectedRole == RoleRank.Parent && !UData.PhoenixUser!.Children.Any())
                throw new BotException(BotError.ParentHasNoAffiliations);

            HomeOptions homeOptions = new()
            {
                Action = CData.Command.ToBotAction()
            };

            return await stepCtx.BeginDialogAsync(nameof(HomeDialog), homeOptions, canTkn);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.ReplaceDialogAsync(stepCtx.ActiveDialog.Id, null, canTkn);
        }

        #endregion

        #region Role Waterfall Dialog

        private async Task<DialogTurnResult> CheckRoleAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var userRoles = await _userManager.GetRoleRanksAsync(UData.AppUser!);
            int roleCount = userRoles.Count;

            bool areValid = (roleCount == 1 && userRoles.Single() != RoleRank.None)
                || (roleCount == 2
                    && userRoles.Any(rr => rr.IsStaff()) && userRoles.Contains(RoleRank.Parent));

            if (!areValid)
                throw new BotException(BotError.RoleNotValid);

            if (UData.IsBackend)
            {
                stepCtx.Values.Add("RolesToSelect",
                    RoleExtensions.ClientRoleRanks.Concat(RoleExtensions.StaffRoleRanks).ToArray());

                return await stepCtx.NextAsync(null, canTkn);
            }

            if (roleCount == 2)
            {
                stepCtx.Values.Add("RolesToSelect", userRoles.ToArray());

                return await stepCtx.NextAsync(null, canTkn);
            }

            UData.SelectedRole = userRoles.Single();
            await SetUserStateAsync(stepCtx.Context, canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> AskRoleStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var rolesToSel = stepCtx.Values["RolesToSelect"] as RoleRank[];

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνδεθείς ως:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε έναν από τους παρακάτω ρόλους:"),
                    Choices = ChoiceFactory.ToChoices(rolesToSel!.Select(r => r.ToFriendlyString()).ToArray())
                },
                canTkn);
        }

        private async Task<DialogTurnResult> SelectRoleStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            var rolesToSel = stepCtx.Values["RolesToSelect"] as RoleRank[];

            UData.SelectedRole = rolesToSel!.ElementAt(foundChoice.Index);
            await SetUserStateAsync(stepCtx.Context, canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion
    }
}