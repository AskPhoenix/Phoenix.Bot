using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Sms;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Common.Authentication
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

            AddDialog(new TextPrompt(PromptNames.Code, PromptValidators.CodePromptValidator));

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
            if (options is not AuthenticationOptions authenticationOptions)
                return innerDc.EndDialogAsync(false);

            InitialDialogId = authenticationOptions.IsOwnerVerification 
                ? WaterfallNames.Auth.Verification.SendCode 
                : WaterfallNames.Auth.Verification.CheckCode;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Send Code Waterfall Dialog

        private async Task<DialogTurnResult> SmsLeftCheckStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            var authenticationOptions = stepContext.Options as AuthenticationOptions;

            if (userData.SmsCount < UserDataDefaults.MaxSmsNumber)
            {
                if (authenticationOptions.Phone != "6900000000")
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
            var authenticationOptions = stepContext.Options as AuthenticationOptions;
            string phone = authenticationOptions.Phone;
            string pin;

            if (phone == "6900000000")
                pin = configuration["TestPin"];
            else
            {
                pin = DialogsHelper.GenerateVerificationPin().ToString();
                var sms = new SmsService(configuration["NexmoSMS:ApiKey"], configuration["NexmoSMS:ApiSecret"]);
                await sms.SendAsync(phone, $"Το pin σου για το Phoenix είναι το {pin}.");   //TODO: και έχει διάρκεια 5 λεπτά."
            }
            
            stepContext.Values.Add("pin", pin);

            return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Τέλεια! Μόλις σου έστειλα ένα SMS με ένα μοναδικό pin. Το έλαβες;"));
        }

        private async Task<DialogTurnResult> ReceiveCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(WaterfallNames.Auth.Verification.CheckCode, stepContext.Values["pin"], cancellationToken);

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
            if (stepContext.Result is bool)
                return await stepContext.NextAsync(stepContext.Result, cancellationToken);

            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(WaterfallNames.Auth.Verification.CheckCode, stepContext.Values["pin"], cancellationToken);

            return await stepContext.ReplaceDialogAsync(InitialDialogId, stepContext.Options, cancellationToken);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.EndDialogAsync(stepContext.Result, cancellationToken);
        }

        #endregion

        #region Check Code Waterfall Dialog

        private async Task<DialogTurnResult> AskCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string validationType = InitialDialogId == WaterfallNames.Auth.Verification.CheckCode ? "code" : "pin";
            string promptMessage = InitialDialogId == WaterfallNames.Auth.Verification.CheckCode
                ? "Παρακαλώ πληκτρολόγησε τον κωδικό επαλήθευσης που σου έδωσε ο ιδιοκτήτης του αριθμού:"
                : "Ωραία! Παρακαλώ πληκτρολόγησε το pin που έλαβες παρακάτω:";

            return await stepContext.PromptAsync(
                PromptNames.Code,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(promptMessage),
                    RetryPrompt = MessageFactory.Text("Η μορφή του κωδικού που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ τον ξανά:"),
                    Validations = validationType
                });
        }

        private async Task<DialogTurnResult> CheckCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccessor.GetAsync(stepContext.Context, null, cancellationToken);
            userData.LoginAttempts++;

            string result = stepContext.Result.ToString();
            
            bool valid = false;
            if (stepContext.Options is AuthenticationOptions authenticationOptions && !authenticationOptions.IsOwnerVerification)
                valid = authenticationOptions.Codes.Contains(result);
            else if (stepContext.Options is string pin)
                valid = result == pin;

            if (valid)
            {
                userData.LoginAttempts = 0;
                await stepContext.Context.SendActivityAsync("Πολύ ωραία! Η σύνδεση ολοκληρώθηκε επιτυχώς! 😁");
                return await stepContext.EndDialogAsync(true, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Ο κωδικός που έγραψες δεν είναι σωστός.");
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
