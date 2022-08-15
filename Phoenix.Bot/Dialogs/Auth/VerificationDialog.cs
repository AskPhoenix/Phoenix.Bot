namespace Phoenix.Bot.Dialogs.Auth
{
    public class VerificationDialog : StateDialog
    {
        private readonly IStatePropertyAccessor<AccessData> _accessDataAcsr;
        private readonly OneTimeCodeRepository _otcRepository;

        public VerificationDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(VerificationDialog))
        {
            _accessDataAcsr = userState.CreateProperty<AccessData>(nameof(AccessData));
            _otcRepository = new(phoenixContext);

            AddDialog(new TextPrompt(PromptNames.Code, PromptValidators.CodePromptValidator));

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Verification.Top,
                new WaterfallStep[]
                {
                    AskCodeStepAsync,
                    CheckCodeStepAsync,
                    RecheckCodeStepAsync
                }));

            InitialDialogId = WaterfallNames.Auth.Verification.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> AskCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;

            string prompt, reprompt;
            if (options.IsOwnerAuthentication)
            {
                prompt = "Ωραία! Παρακαλώ πληκτρολόγησε τον κωδικό που έλαβες με SMS παρακάτω:";
                reprompt = "Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:";
            }
            else
            {
                prompt = "Ζήτησε από τον γονέα σου να σου δώσει τον κωδικό " +
                    "επαλήθευσής σου (κατηγορία «🗝 Πρόσβαση») και έπειτα πληκτρολόγησέ τον παρακάτω:";
                reprompt = "Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:";
            }

            return await stepCtx.PromptAsync(
                PromptNames.Code,
                new PromptOptions 
                {
                    Prompt = MessageFactory.Text(prompt),
                    RetryPrompt = MessageFactory.Text(reprompt)
                }, canTkn);
        }

        private async Task<DialogTurnResult> CheckCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;
            var result = ((string)stepCtx.Result).Trim();

            var phoneOwner = await _userRepository.FindPrimaryAsync(options.PhoneOwnerId, canTkn);
            if (phoneOwner is null)
                throw new BotException(BotError.UserNotValid);

            var otcs = options.IsOwnerAuthentication
                ? phoneOwner.OneTimeCodes
                : phoneOwner.Children.SelectMany(c => c.OneTimeCodes);

            var matchingOtcs = otcs.Where(otc => otc.Token.Equals(result));
            var activeOtcs = matchingOtcs.Where(otc => otc.ExpiresAt >= DateTime.UtcNow);

            if (!matchingOtcs.Any() || !activeOtcs.Any())
            {
                var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);
                accessData.AccessFailedCount += 1;
                await _accessDataAcsr.SetAsync(stepCtx.Context, accessData, canTkn);

                if (!matchingOtcs.Any())
                {
                    await stepCtx.Context.SendActivityAsync("Ο κωδικός που έγραψες δεν είναι έγκυρος.",
                        cancellationToken: canTkn);

                    return await stepCtx.NextAsync(null, canTkn);
                }

                await stepCtx.Context.SendActivityAsync("Ο κωδικός που έγραψες έχει λήξει.",
                    cancellationToken: canTkn);

                if (options.IsOwnerAuthentication)
                    return await stepCtx.EndDialogAsync(null, canTkn);

                return await stepCtx.NextAsync(null, canTkn);
            }

            var validOtc = activeOtcs.First();

            await _otcRepository.DeleteAsync(validOtc);
            await _accessDataAcsr.DeleteAsync(stepCtx.Context, canTkn);
            
            await stepCtx.Context.SendActivityAsync("Πολύ ωραία! Η σύνδεση ολοκληρώθηκε επιτυχώς! 😁",
                cancellationToken: canTkn);

            return await stepCtx.EndDialogAsync(validOtc.UserId, canTkn);
        }

        private async Task<DialogTurnResult> RecheckCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);

            if (accessData.AccessFailedCount <= AccessLimitations.MaxFails)
            {
                await stepCtx.Context.SendActivityAsync("Ας προσπαθήσουμε ξανά!", cancellationToken: canTkn);

                return await stepCtx.ReplaceDialogAsync(
                    WaterfallNames.Auth.Verification.Top, stepCtx.Options, canTkn);
            }

            throw new BotException(BotError.AuthMaxFails);
        }

        #endregion
    }
}
