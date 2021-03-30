using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options.Authentification;
using Phoenix.DataHandle.Sms;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Authentication
{
    public class VerificationDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly IStatePropertyAccessor<UserData> userDataAccessor;

        public VerificationDialog(IConfiguration configuration, UserState userState)
            : base(nameof(VerificationDialog))
        {
            this.configuration = configuration;
            this.userDataAccessor = userState.CreateProperty<UserData>(nameof(UserData));

            AddDialog(new TextPrompt(PromptNames.Pin, PromptValidators.PinPromptValidator));
            AddDialog(new TextPrompt(nameof(TextPrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Verification.SendCode,
                new WaterfallStep[]
                {
                    SmsLeftCheckStepAsync,
                    SendCodeStepAsync,
                    ReceiveCodeStepAsync,
                    CodeDelayedStepAsync,
                    EndStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Verification.CheckCode,
                new WaterfallStep[]
                {
                    AskCodeStepAsync,
                    CheckCodeStepAsync,
                    RecheckCodeStepAsync
                }));

            InitialDialogId = WaterfallNames.Auth.Verification.SendCode;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            var verificationOptions = options as VerificationOptions;
            if (!verificationOptions.IsOwnerAuthentication)
                InitialDialogId = WaterfallNames.Auth.Verification.CheckCode;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Send Pin Waterfall Dialog

        private async Task<DialogTurnResult> SmsLeftCheckStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            var verificationOptions = stepContext.Options as VerificationOptions;

            if (userData.SmsCount < UserDataDefaults.MaxSmsNumber)
            {
                if (!verificationOptions.Phone.StartsWith("690000000"))
                {
                    userData.SmsCount += 1;
                    await userDataAccessor.SetAsync(stepContext.Context, userData, cancellationToken);
                }

                return await stepContext.NextAsync(null, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Δυστυχώς έχεις υπερβεί το όριο μηνυμάτων επαλήθευσης.");
            await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινώνησε με το φροντιστήριο για να συνεχίσεις.");

            return await stepContext.EndDialogAsync(false, cancellationToken);
        }

        private async Task<DialogTurnResult> SendCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var verificationOptions = stepContext.Options as VerificationOptions;
            string phone = verificationOptions.Phone;

            if (phone.StartsWith("690000000"))
                verificationOptions.Pin = configuration["TestPin"];
            else
            {
                verificationOptions.Pin = DialogsHelper.GenerateVerificationPin().ToString();
                var sms = new SmsService(configuration["NexmoSMS:ApiKey"], configuration["NexmoSMS:ApiSecret"]);
                //TODO: Το pin θα είναι έγκυρο για τα επόμενα 5 λεπτά.
                await sms.SendAsync(phone, $"Χρησιμοποίησε το pin {verificationOptions.Pin} για να συνδεθείς στο Ask Phoenix.");
            }
            
            return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Τέλεια! Μόλις σου έστειλα ένα SMS με ένα μοναδικό pin. Το έλαβες;"));
        }

        private async Task<DialogTurnResult> ReceiveCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var verificationOptions = stepContext.Options as VerificationOptions;
            var foundChoice = stepContext.Result as FoundChoice;

            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(WaterfallNames.Auth.Verification.CheckCode, verificationOptions, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("ΟΚ, μην ανησυχείς! Επειδή καμιά φορά αργεί, περίμενε μερικά λεπτά ακόμα."),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις για να συνεχίσουμε:"),
                    Choices = new Choice[] { new Choice("👌 Το έλαβα"), new Choice("🔁 Στείλε ξανά") }
                });
        }

        private async Task<DialogTurnResult> CodeDelayedStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is not FoundChoice foundChoice)
                return await stepContext.NextAsync(stepContext.Result, cancellationToken);

            var verificationOptions = stepContext.Options as VerificationOptions;
            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(WaterfallNames.Auth.Verification.CheckCode, verificationOptions, cancellationToken);

            return await stepContext.ReplaceDialogAsync(InitialDialogId, stepContext.Options, cancellationToken);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.EndDialogAsync(stepContext.Result, cancellationToken);
        }

        #endregion

        #region Check Pin / Code Waterfall Dialog

        private async Task<DialogTurnResult> AskCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var verificationOptions = stepContext.Options as VerificationOptions;
            if (verificationOptions.IsOwnerAuthentication)
                return await stepContext.PromptAsync(
                    PromptNames.Pin,
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text("Ωραία! Παρακαλώ πληκτρολόγησε το pin που έλαβες παρακάτω:"),
                        RetryPrompt = MessageFactory.Text("Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                    }, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(TextPrompt), 
                new PromptOptions 
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε τον κωδικό επαλήθευσης που σου έδωσε ο ιδιοκτήτης του αριθμού:") 
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> CheckCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var verificationOptions = stepContext.Options as VerificationOptions;
            var userData = await userDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            userData.LoginAttempts++;

            string result = stepContext.Result.ToString().Trim();
            int verifiedUserId = 0;

            bool valid = false;
            if (verificationOptions.IsOwnerAuthentication)
                valid = result == verificationOptions.Pin;
            else
                valid = verificationOptions.TryFindCode(result, out verifiedUserId);

            if (valid)
            {
                userData.LoginAttempts = 0;
                await stepContext.Context.SendActivityAsync("Πολύ ωραία! Η σύνδεση ολοκληρώθηκε επιτυχώς! 😁");
                return await stepContext.EndDialogAsync(verificationOptions.IsOwnerAuthentication ? true : verifiedUserId, cancellationToken);
            }

            await userDataAccessor.SetAsync(stepContext.Context, userData, cancellationToken);

            await stepContext.Context.SendActivityAsync("Ο κωδικός που έγραψες δεν είναι έγκυρος.");
            return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να προσπαθήσεις ξανά;"));
        }

        private async Task<DialogTurnResult> RecheckCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                var userData = await userDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
                if (userData.LoginAttempts <= UserDataDefaults.MaxLoginAttempts)
                    return await stepContext.ReplaceDialogAsync(WaterfallNames.Auth.Verification.CheckCode, stepContext.Options, cancellationToken);

                await stepContext.Context.SendActivityAsync("Λυπάμαι, αλλά έχεις υπερβεί τον μέγιστο αριθμό προσπαθειών.");
                await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινώνησε με το φροντστήριό σου και προσπάθησε ξανά.");
                return await stepContext.EndDialogAsync(false, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Κανένα πρόβλημα, εις το επανιδείν! 😊");
            return await stepContext.EndDialogAsync(false, cancellationToken);
        }

        #endregion
    }
}
