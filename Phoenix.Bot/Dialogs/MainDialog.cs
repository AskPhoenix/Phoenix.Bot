using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Dialogs.Student;
using System;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.Bot.Dialogs.Teacher;
using static Phoenix.Bot.Helpers.DialogHelper;
using Phoenix.Bot.Helpers;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Phoenix.DataHandle.Main;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Extensions;
using Microsoft.Bot.Builder.Dialogs.Choices;

namespace Phoenix.Bot.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly IConfiguration _configuration;
        private readonly BotState _conversationState;
        private readonly BotState _userState;
        private readonly PhoenixContext _phoenixContext;

        private static class WaterfallNames
        {
            public const string Main = "Main_WaterfallDialog";
        }

        public MainDialog(IConfiguration configuration, ConversationState conversationState, UserState userState, PhoenixContext phoenixContext,
            AuthDialog authDialog, WelcomeDialog welcomeDialog, FeedbackDialog feedbackDialog, StudentDialog studentDialog, TeacherDialog teacherDialog)
            : base(nameof(MainDialog))
        {
            _configuration = configuration;
            _conversationState = conversationState;
            _userState = userState;
            _phoenixContext = phoenixContext;

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(authDialog);
            AddDialog(welcomeDialog);
            AddDialog(feedbackDialog);
            AddDialog(studentDialog);
            AddDialog(teacherDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Main,
                new WaterfallStep[]
                {
                    FirstTimeStepAsync,
                    UserRegisterStepAsync,
                    CommandHandleStepAsync,
                    GreetingStepAsync,
                    MultiRoleStepAsync,
                    MultiRoleSelectStepAsync,
                    ForwardStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Main;
        }

        #region Main Waterfall Dialog

        private async Task<DialogTurnResult> FirstTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var isAuthAcsr = _userState.CreateProperty<bool>("IsAuthenticated");
            bool isAuthenticated = await isAuthAcsr.GetAsync(stepContext.Context);
            if (isAuthenticated)
            {
                isAuthenticated &= _phoenixContext.AspNetUsers.Any(u => u.FacebookId == stepContext.Context.Activity.From.Id);
                if (!isAuthenticated)
                    await isAuthAcsr.SetAsync(stepContext.Context, false);
            }

            if (!isAuthenticated)
                return await stepContext.BeginDialogAsync(nameof(AuthDialog), null, cancellationToken);

            return await stepContext.NextAsync(isAuthenticated, cancellationToken);
        }

        private async Task<DialogTurnResult> UserRegisterStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!(stepContext.Result is bool) || !(bool)stepContext.Result)
                return await stepContext.CancelAllDialogsAsync(cancellationToken);

            var isAuthAcsr = _userState.CreateProperty<bool>("IsAuthenticated");
            bool oldIsAuth = await isAuthAcsr.GetAsync(stepContext.Context);
            if (oldIsAuth)
                return await stepContext.NextAsync(null, cancellationToken);

            bool acceptedTerms = await _userState.CreateProperty<bool>("AcceptedTerms").GetAsync(stepContext.Context);
            if (!acceptedTerms)
                return await stepContext.CancelAllDialogsAsync(cancellationToken);

            await isAuthAcsr.SetAsync(stepContext.Context, true);

            var phoneAcsr = _conversationState.CreateProperty<string>("Phone");
            string phone = await phoneAcsr.GetAsync(stepContext.Context);
            await phoneAcsr.DeleteAsync(stepContext.Context);

            await _userState.CreateProperty<int>("sms_left").DeleteAsync(stepContext.Context);

            //This is for the students and their parents who have registered with the same phone number
            var codeAcsr = _conversationState.CreateProperty<string>("OneTimeCode");
            string code = await codeAcsr.GetAsync(stepContext.Context);
            await codeAcsr.DeleteAsync(stepContext.Context);

            string schoolFbId = stepContext.Context.Activity.Recipient.Id;
            var user = _phoenixContext.UserSchool.
                Include(us => us.AspNetUser).
                SingleOrDefault(us => us.AspNetUser.PhoneNumber == phone && us.School.FacebookPageId == schoolFbId && us.AspNetUser.OneTimeCode == code).
                AspNetUser;

            user.User.TermsAccepted = acceptedTerms;
            user.FacebookId = stepContext.Context.Activity.From.Id;
            if (!string.IsNullOrEmpty(code))
                user.OneTimeCodeUsed = true;
            await _phoenixContext.SaveChangesAsync();

            await _conversationState.CreateProperty<bool>("NeedsWelcoming").SetAsync(stepContext.Context, true);
            await _conversationState.SaveChangesAsync(stepContext.Context);
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> CommandHandleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!Persistent.TryGetCommand(stepContext.Context.Activity.Text, out Persistent.Command cmd))
                return await stepContext.NextAsync(null, cancellationToken);

            switch (cmd)
            {
                case Persistent.Command.GetStarted:
                    await _userState.CreateProperty<Role>("RoleSelected").DeleteAsync(stepContext.Context);
                    return await stepContext.BeginDialogAsync(nameof(WelcomeDialog), null, cancellationToken);
                case Persistent.Command.Tutorial:
                    return await stepContext.BeginDialogAsync(nameof(WelcomeDialog), null, cancellationToken);
                case Persistent.Command.Feedback:
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), null, cancellationToken);
                default:
                    return await stepContext.NextAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> GreetingStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string mess = stepContext.Context.Activity.Text;
            if (!mess.ContainsSynonyms(SynonymHelper.Topics.Greetings))
                return await stepContext.NextAsync(null, cancellationToken);

            var reply = MessageFactory.ContentUrl(
                url: await ReceiveGifAsync("g", "hi", 10, new Random().Next(10), _configuration["GiphyKey"]),
                contentType: "image/gif");
            await stepContext.Context.SendActivityAsync(reply);

            var name = _phoenixContext.User.Single(u => u.AspNetUser.FacebookId == stepContext.Context.Activity.From.Id).FirstName;
            reply = MessageFactory.Text($"Γεια σου {GreekNameCall(name)}! 😊");
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> MultiRoleStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var roleSel = await _userState.CreateProperty<Role>("RoleSelected").GetAsync(stepContext.Context, () => Role.Undefined);
            if (roleSel != Role.Undefined)
                return await stepContext.NextAsync(null);

            var userRoles = _phoenixContext.AspNetUsers.
                Include(u => u.AspNetUserRoles).
                Single(u => u.FacebookId == stepContext.Context.Activity.From.Id).
                AspNetUserRoles.
                Select(ur => ur.Role).
                AsEnumerable();

            // If user has 1 role, then don't ask
            if (userRoles.Count() == 1)
                return await stepContext.NextAsync(userRoles.First().Type);
            // If user has multiple non-contradictious roles (e.g. Teacher, Owner), then don't ask and select the hierarchly highest one
            if (userRoles.All(r => (Role)r.Type >= Role.Teacher))
                return await stepContext.NextAsync(userRoles.Max(r => r.Type));
            // If user has multiple roles and the include Student or Teacher, meaning they are contradictious, then ask which one they prefer

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνδεθείς ως:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε έναν από τους παρακάτω ρόλους:"),
                    Choices = ChoiceFactory.ToChoices(userRoles.Select(r => r.NormalizedName).ToList())
                });
        }

        private async Task<DialogTurnResult> MultiRoleSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var roleAcsr = _userState.CreateProperty<Role>("RoleSelected");
            if (await roleAcsr.GetAsync(stepContext.Context, () => Role.Undefined) == Role.Undefined)
            {
                if (stepContext.Result is FoundChoice foundChoice)
                {
                    var roleSel = _phoenixContext.AspNetRoles.
                        Single(r => r.NormalizedName == foundChoice.Value).
                        Type;

                    await roleAcsr.SetAsync(stepContext.Context, (Role)roleSel);
                }
                else
                    await roleAcsr.SetAsync(stepContext.Context, (Role)stepContext.Result);
                await _userState.SaveChangesAsync(stepContext.Context);
            }

            var welcomingAcsr = _conversationState.CreateProperty<bool>("NeedsWelcoming");
            bool needsWelcoming = await welcomingAcsr.GetAsync(stepContext.Context);
            if (needsWelcoming)
            {
                await welcomingAcsr.DeleteAsync(stepContext.Context);
                return await stepContext.BeginDialogAsync(nameof(WelcomeDialog), null, cancellationToken);
            }
            
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var roleSel = await _userState.CreateProperty<Role>("RoleSelected").GetAsync(stepContext.Context);
            
            return roleSel switch
            {
                Role.Student => await stepContext.BeginDialogAsync(nameof(StudentDialog), null, cancellationToken),
                var r when r >= Role.Teacher => await stepContext.BeginDialogAsync(nameof(TeacherDialog), null, cancellationToken),
                _ => await stepContext.CancelAllDialogsAsync(cancellationToken)
            };
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

        #endregion
    }
}
