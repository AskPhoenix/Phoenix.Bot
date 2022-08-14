using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Helpers;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs.Prompts.Options;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Senders;

namespace Phoenix.Bot.Dialogs.Auth
{
    public class AuthenticationDialog : StateDialog
    {
        private readonly IStatePropertyAccessor<AccessData> _accessDataAcsr;
        private readonly UserConnectionRepository _userConnectionRepository;
        private readonly OneTimeCodeRepository _otcRepository;
        private readonly IConfiguration _configuration;
        private readonly SmsSender _smsSender;

        public AuthenticationDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,
            IConfiguration configuration,
            SmsSender smsSender,
            VerificationDialog verificationDialog)
            : base(userState, convState, userManager, phoenixContext, nameof(AuthenticationDialog))
        {
            _accessDataAcsr = userState.CreateProperty<AccessData>(nameof(AccessData));
            _userConnectionRepository = new(phoenixContext);
            _otcRepository = new(phoenixContext);
            _configuration = configuration;
            _smsSender = smsSender;

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(PromptNames.Phone, PromptValidators.PhoneNumberPromptValidator));

            AddDialog(verificationDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Top,
                new WaterfallStep[]
                {
                    AskForCredentialsStepAsync,
                    LoginStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Credentials,
                new WaterfallStep[]
                {
                    AskPhoneStepAsync,
                    EligibilityStepAsync,
                    VerificationStepAsync,
                    PostVerificationStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.GenerateCode,
                new WaterfallStep[]
                {
                    FailsCheckStepAsync,
                    SendCodeStepAsync,
                    ReceiveCodeStepAsync,
                    CodeDelayedStepAsync,
                    PostCodeCheckStepAsync
                }));

            InitialDialogId = WaterfallNames.Auth.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> AskForCredentialsStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Αρχικά θα χρειαστώ το κινητό τηλέφωνο που δόθηκε κατά την εγγραφή.");

            return await stepCtx.BeginDialogAsync(WaterfallNames.Auth.Credentials, stepCtx.Options, canTkn);
        }

        private async Task<DialogTurnResult> LoginStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (stepCtx.Result is not int verifiedUserId)
                throw new BotException(BotError.AuthFailed);

            var appVerifiedUser = await _userManager.FindByIdAsync(verifiedUserId.ToString());
            var verifiedUser = await _userRepository.FindPrimaryAsync(verifiedUserId);

            if (verifiedUser is null || appVerifiedUser is null)
                throw new BotException(BotError.UserNotValid);

            verifiedUser.HasAcceptedTerms = true;
            await _userRepository.UpdateAsync(verifiedUser);
            
            appVerifiedUser.PhoneNumberConfirmed = true;
            await _userManager.UpdateAsync(appVerifiedUser);

            var userConnection = await _userConnectionRepository.RegisterAsync(
                stepCtx.GetProvider(), stepCtx.GetProviderKey(), verifiedUserId, activate: true, canTkn);

            await UData.RefreshAsync(userConnection, _userManager);
            await SetUserStateAsync(stepCtx.Context, canTkn);

            // Create password for Staff users
            var userRoles = await _userManager.GetRoleRanksAsync(appVerifiedUser);
            if (userRoles.Any(rr => rr.IsStaffOrBackend()))
            {
                if (!await _userManager.HasPasswordAsync(appVerifiedUser))
                {
                    string pass = CodeGenHelper.GeneratePassCode(8);
                    await _userManager.AddPasswordAsync(appVerifiedUser, pass);

                    var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);
                    accessData.TeacherPassword = pass;
                    await _accessDataAcsr.SetAsync(stepCtx.Context, accessData, canTkn);
                }
            }

            return await stepCtx.EndDialogAsync(true, canTkn);
        }

        #endregion

        #region Credentials Waterfall Dialog

        private async Task<DialogTurnResult> AskPhoneStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                PromptNames.Phone,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε τον αριθμό παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Ο αριθμός τηλεφώνου πρέπει να είναι στη μορφή 69xxxxxxxx. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                }, canTkn);
        }

        private async Task<DialogTurnResult> EligibilityStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;
            var phone = (string)stepCtx.Result;

            if (!phone.StartsWith('+'))
                phone += CData.School.SchoolSetting.PhoneCountryCode;

            options.Phone = phone;

            var appPhoneOwner = await _userManager.FindByPhoneNumberAsync(phone);
            if (appPhoneOwner is null)
            {
                await stepCtx.Context.SendActivityAsync("Το κινητό τηλέφωνο δε βρέθηκε. " +
                        "Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");
                return await stepCtx.ReplaceDialogAsync(InitialDialogId, new AuthenticationOptions(), canTkn);
            }

            int userId = int.Parse(await _userManager.GetUserIdAsync(appPhoneOwner));
            options.PhoneOwnerId = userId;

            var phoneOwner = (await _userRepository.FindPrimaryAsync(userId))!;

            var userRoles = await _userManager.GetRoleRanksAsync(appPhoneOwner);

            if (!phoneOwner.Schools.Contains(CData.School) && !userRoles.Any(rr => rr.IsSuper()))
            {
                await stepCtx.Context.SendActivityAsync("Το κινητό τηλέφωνο δε βρέθηκε. " +
                    "Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");
                return await stepCtx.ReplaceDialogAsync(
                    WaterfallNames.Auth.Credentials, new AuthenticationOptions(), canTkn);
            }

            if (phoneOwner.Children.Any())
            {
                return await stepCtx.PromptAsync(
                    nameof(UnaccentedChoicePrompt),
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text("Ποιος πρόκειται να συνδεθεί;"),
                        RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω επιλογές:"),
                        Choices = ChoiceFactory.ToChoices(new[] { "Γονέας", "Μαθητής" })
                    }, canTkn);
            }

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> VerificationStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;
            var result = (FoundChoice)stepCtx.Result;

            options.IsOwnerAuthentication = result is null || result.Index == 0;

            await stepCtx.Context.SendActivityAsync("Εντάξει, ας συνεχίσουμε!");

            if (options.IsOwnerAuthentication)
                return await stepCtx.BeginDialogAsync(WaterfallNames.Auth.GenerateCode, options, canTkn);

            return await stepCtx.BeginDialogAsync(nameof(VerificationDialog), options, canTkn);
        }

        private async Task<DialogTurnResult> PostVerificationStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.EndDialogAsync(stepCtx.Result, canTkn);
        }

        #endregion

        #region Generate Code Waterfall Dialog

        private async Task<DialogTurnResult> FailsCheckStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);

            if (accessData.SMSFailedCount < AccessLimitations.MaxFails ||
                accessData.AccessFailedCount < AccessLimitations.MaxFails)
                return await stepCtx.NextAsync(null, canTkn);

            throw new BotException(BotError.AuthMaxFails);
        }

        private async Task<DialogTurnResult> SendCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;

            OneTimeCode otc = new()
            {
                Purpose = OneTimeCodePurpose.Verification,
                Token = CodeGenHelper.GenerateCode(6).ToString(),
                UserId = options.PhoneOwnerId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            otc = await _otcRepository.CreateAsync(otc);

            if (options.Phone.StartsWith("690000000"))
                await stepCtx.Context.SendActivityAsync(otc.Token, cancellationToken: canTkn);
            else
                await _smsSender.SendAsync(options.Phone,
                    $"Χρησιμοποίησε τον κωδικό {otc.Token} εντός 5 λεπτών για να συνδεθείς στο Ask Phoenix.");

            return await stepCtx.PromptAsync(nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Μόλις σου έστειλα ένα SMS με έναν μοναδικό κωδικό. " +
                    "Το έλαβες;", simpleNo: true), canTkn);
        }

        private async Task<DialogTurnResult> ReceiveCodeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AuthenticationOptions)stepCtx.Options;
            var result = (FoundChoice)stepCtx.Result;

            if (result.Index == 0)
                return await stepCtx.BeginDialogAsync(nameof(VerificationDialog), options, canTkn);

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("ΟΚ, μην ανησυχείς! Επειδή καμιά φορά αργεί, περίμενε μερικά λεπτά ακόμα."),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις για να συνεχίσουμε:"),
                    Choices = new Choice[] { new Choice("👌 Το έλαβα"), new Choice("🔁 Στείλε ξανά") }
                }, canTkn);
        }

        private async Task<DialogTurnResult> CodeDelayedStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (stepCtx.Result is not FoundChoice foundChoice)
                return await stepCtx.NextAsync(stepCtx.Result, canTkn);

            var options = (AuthenticationOptions)stepCtx.Options;

            if (foundChoice.Index == 0)
                return await stepCtx.BeginDialogAsync(nameof(VerificationDialog), options, canTkn);

            var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);
            accessData.SMSFailedCount += 1;
            await _accessDataAcsr.SetAsync(stepCtx.Context, accessData, canTkn);

            return await stepCtx.ReplaceDialogAsync(WaterfallNames.Auth.GenerateCode, stepCtx.Options, canTkn);
        }

        private async Task<DialogTurnResult> PostCodeCheckStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (stepCtx.Result is null)
                return await stepCtx.ReplaceDialogAsync(
                    WaterfallNames.Auth.GenerateCode, stepCtx.Options, canTkn);

            return await stepCtx.EndDialogAsync(stepCtx.Result, canTkn);
        }

        #endregion
    }
}
