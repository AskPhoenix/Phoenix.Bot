using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;

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

            AddDialog(new TextPrompt(PromptNames.VerificationCode, PromptValidators.VerificationCodePromptValidator));
            AddDialog(new TextPrompt(PromptNames.IdentificationCode, PromptValidators.IdentificationCodePromptValidator));

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

            if (options.IsOwnerAuthentication)
                return await stepCtx.PromptAsync(
                    PromptNames.VerificationCode,
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text("Ωραία! Παρακαλώ πληκτρολόγησε τον κωδικό που έλαβες με SMS παρακάτω:"),
                        RetryPrompt = MessageFactory.Text("Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                    }, canTkn);

            return await stepCtx.PromptAsync(
                PromptNames.IdentificationCode,
                new PromptOptions 
                {
                    Prompt = MessageFactory.Text("Ζήτησε από τον γονέα σου να σου δώσει τον κωδικό " +
                        "επαλήθευσής σου (κατηγορία «🗝 Πρόσβαση») και έπειτα πληκτρολόγησέ τον παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                }, canTkn);
        }

        private async Task<DialogTurnResult> CheckCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;
            var result = ((string)stepCtx.Result).Trim();

            var phoneOwner = await _userRepository.FindPrimaryAsync(options.PhoneOwnerId, canTkn);
            if (phoneOwner is null)
                return await ExitAsync(
                    message: "Το τηλέφωνο δεν αντιστοιχεί σε κάποιον χρήστη πλέον.",
                    solution: "Παρακαλώ προσπαθήστε ξανά.",
                    error: 0,
                    stepCtx, canTkn);

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
                    await stepCtx.Context.SendActivityAsync("Ο κωδικός που έγραψες δεν είναι έγκυρος.");

                    return await stepCtx.NextAsync(null, canTkn);
                }

                await stepCtx.Context.SendActivityAsync("Ο κωδικός που έγραψες έχει λήξει.");

                if (options.IsOwnerAuthentication)
                    return await stepCtx.EndDialogAsync(null, canTkn);

                return await stepCtx.NextAsync(null, canTkn);
            }

            var validOtc = activeOtcs.First();

            await _otcRepository.DeleteAsync(validOtc);
            await _accessDataAcsr.DeleteAsync(stepCtx.Context, canTkn);
            
            await stepCtx.Context.SendActivityAsync("Πολύ ωραία! Η σύνδεση ολοκληρώθηκε επιτυχώς! 😁");

            return await stepCtx.EndDialogAsync(validOtc.UserId, canTkn);
        }

        private async Task<DialogTurnResult> RecheckCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);

            if (accessData.AccessFailedCount <= AccessLimitations.MaxFails)
            {
                await stepCtx.Context.SendActivityAsync("Ας προσπαθήσουμε ξανά!");

                return await stepCtx.ReplaceDialogAsync(
                    WaterfallNames.Auth.Verification.Top, stepCtx.Options, canTkn);
            }

            return await ExitAsync(
                message: "Δυστυχώς έχεις υπερβεί το όριο αποτυχημένων προσπαθειών επαλήθευσης.",
                solution: "Παρακαλώ επικοινώνησε με το κέντρο για την επίλυση του προβλήματος.",
                error: 0,
                stepCtx, canTkn);
        }

        #endregion
    }
}
